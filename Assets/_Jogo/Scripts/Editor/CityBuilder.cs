using System;
using System.Collections.Generic;
using System.IO;
using Entregador.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Entregador.Editor
{
    /// <summary>
    /// Gera do zero a cena Assets/_Jogo/Scenes/SantoAmaro.unity: cidade greybox de cerca de 1 km x 1 km,
    /// moto, câmeras, grafo de ruas, restaurantes, endereços, buracos e o objeto GameRoot que roda o jogo.
    /// Uso: menu Jogo/Gerar cidade, ou -executeMethod Entregador.Editor.CityBuilder.BuildFromCommandLine.
    /// </summary>
    public static partial class CityBuilder
    {
        public const string ScenePath = "Assets/_Jogo/Scenes/SantoAmaro.unity";

        // Medidas em metros.
        private const int StreetCount = 10;
        private const int BlockCount = StreetCount - 1;
        private const float BlockSize = 130f;
        private const float MinCustomerMeters = 220f;
        private const float StreetHalfWidth = 6f;
        private const float AvenueHalfWidth = 12f;
        private const int AvenueIndex = 4;
        private const float SidewalkInset = 3f;

        // Largo (praça): bloco entre as ruas verticais 3-4 e as horizontais 4-5.
        private const int LargoBlockX = 3;
        private const int LargoBlockZ = 4;

        private static readonly string[] CustomerStreets =
        {
            "Rua das Jabuticabeiras", "Rua Padre Anselmo Duarte", "Rua dos Ipês Roxos", "Travessa Dona Zulmira",
            "Rua Capitão Nogueira", "Rua das Hortênsias", "Rua Mestre Valentim", "Rua Tia Ciata",
            "Rua do Cambuci Velho", "Rua Joaquim Pinheiro", "Rua das Paineiras", "Rua Professora Lúcia",
            "Rua Seu Antônio Barbeiro", "Rua dos Tamoios", "Rua Santa Rita de Cássia", "Rua do Mercado Novo",
            "Rua Coronel Ribeiro", "Rua das Mangueiras", "Rua Dona Benta", "Rua Menino Jesus",
        };

        private struct EdgeSpot
        {
            public bool Horizontal;
            public int Street;
            public int Segment;
            public int Side;
        }

        private static float[] _cx;
        private static float[] _hwx;
        private static float[] _cz;
        private static float[] _hwz;

        private static float _span;

        [MenuItem("Jogo/Gerar cidade (Santo Amaro)")]
        public static void Build()
        {
            RenderSetup.Apply();
            TmpSetup.EnsureFallbackFont();
            ComputeGrid();

            Directory.CreateDirectory("Assets/_Jogo/Scenes");
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            MeshStore.Begin();
            GroundRenderers.Clear();
            Materials m = new Materials();
            var world = new GameObject("Cidade");
            Transform roads = Group(world.transform, "Ruas");
            Transform blocks = Group(world.transform, "Quarteiroes");
            Transform buildings = Group(world.transform, "Predios");
            Transform props = Group(world.transform, "Detalhes");
            Transform markers = Group(world.transform, "Marcadores");
            Transform potholes = Group(world.transform, "Buracos");

            BuildGround(world.transform, m);
            BuildRoads(roads, m);
            var random = new System.Random(2024);
            List<EdgeSpot> restaurantSpots = RestaurantSpots();
            BuildBlocks(blocks, buildings, props, m, random, restaurantSpots, out Vector3 largoCenter);

            RoadGraph graph = BuildGraph(world);

            Place[] restaurants = BuildRestaurants(markers, m, restaurantSpots);
            Place[] customers = BuildCustomers(markers, m, restaurantSpots);
            List<Vector3> potholePositions = BuildPotholes(potholes, m, new System.Random(99), restaurants, customers);

            Vector3 spawn = largoCenter + new Vector3(14f, 0.1f, -18f);

            // Pontos que mobiliário e carros parados evitam: marcadores, buracos e a saída da moto.
            var avoid = new List<Vector3>(potholePositions) { spawn };

            foreach (Place place in restaurants)
            {
                avoid.Add(place.Position);
            }

            foreach (Place place in customers)
            {
                avoid.Add(place.Position);
            }

            BuildProps(props, new System.Random(55), avoid);
            var map = world.AddComponent<CityMap>();
            map.Configure(restaurants, customers, graph, spawn, 0f, Vector2.zero, _span);

            ArcadeBikeController bike = BikeFactory.Build(spawn, 0f);
            var tracker = bike.gameObject.AddComponent<DeliveryRunTracker>();
            tracker.Configure(bike);

            var traffic = new GameObject("Transito");
            BuildTraffic(traffic.transform, bike.transform);

            var dryMaterials = new[] { CityStyle.Asphalt(false), CityStyle.AsphaltRed(false), CityStyle.Sidewalk(false), CityStyle.Paving(false) };
            var wetMaterials = new[] { CityStyle.Asphalt(true), CityStyle.AsphaltRed(true), CityStyle.Sidewalk(true), CityStyle.Paving(true) };
            WeatherLook look = LightingSetup.Build(GroundRenderers.ToArray(), dryMaterials, wetMaterials, out Light sun);
            Camera main = BuildMainCamera(bike);
            RainEffect rain = BuildRain(main.transform);
            MinimapView minimap = BuildMinimap(bike);
            RouteGuide route = BuildRoute(graph, bike);

            var root = new GameObject("GameRoot");
            var tuning = root.AddComponent<GameTuning>();
            root.AddComponent<FakeAdService>();
            var ui = new GameObject("Interface").AddComponent<GameUI>();
            ui.transform.SetParent(root.transform, false);
            var flow = root.AddComponent<GameFlow>();
            flow.Configure(map, bike, bike.GetComponent<BikeInput>(), tracker, route, main.GetComponent<FollowCamera>(), minimap, ui, tuning, sun, main);
            flow.AttachRain(rain);
            flow.AttachLook(look);


            MeshStore.Save();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[CityBuilder] Cena gerada em " + ScenePath);
        }

        /// <summary>Ponto de entrada do -executeMethod: gera a cena e sai com código de erro se falhar.</summary>
        public static void BuildFromCommandLine()
        {
            try
            {
                Build();
            }
            catch (Exception exception)
            {
                Debug.LogError("[CityBuilder] Falhou: " + exception);
                EditorApplication.Exit(1);
            }
        }

        // ---------- Grade ----------

        private static void ComputeGrid()
        {
            _cx = new float[StreetCount];
            _hwx = new float[StreetCount];
            _cz = new float[StreetCount];
            _hwz = new float[StreetCount];

            for (int i = 0; i < StreetCount; i++)
            {
                _hwx[i] = i == AvenueIndex ? AvenueHalfWidth : StreetHalfWidth;
                _hwz[i] = StreetHalfWidth;
            }

            for (int i = 1; i < StreetCount; i++)
            {
                _cx[i] = _cx[i - 1] + BlockSize + _hwx[i - 1] + _hwx[i];
                _cz[i] = _cz[i - 1] + BlockSize + _hwz[i - 1] + _hwz[i];
            }

            float shiftX = _cx[StreetCount - 1] * 0.5f;
            float shiftZ = _cz[StreetCount - 1] * 0.5f;

            for (int i = 0; i < StreetCount; i++)
            {
                _cx[i] -= shiftX;
                _cz[i] -= shiftZ;
            }

            _span = Mathf.Max(_cx[StreetCount - 1] - _cx[0], _cz[StreetCount - 1] - _cz[0]) + 2f * AvenueHalfWidth;
        }

        private static float BlockX0(int i) => _cx[i] + _hwx[i];

        private static float BlockX1(int i) => _cx[i + 1] - _hwx[i + 1];

        private static float BlockZ0(int j) => _cz[j] + _hwz[j];

        private static float BlockZ1(int j) => _cz[j + 1] - _hwz[j + 1];

        private sealed class Materials
        {
            public Material Grass = CityMaterials.Lit("Grama", new Color(0.36f, 0.5f, 0.3f));
            public Material Sidewalk = CityMaterials.Lit("Calcada", new Color(0.62f, 0.61f, 0.58f));
            public Material Yard = CityMaterials.Lit("Quintal", new Color(0.4f, 0.55f, 0.3f));
            public Material Courtyard = CityMaterials.Lit("Patio", new Color(0.42f, 0.42f, 0.44f));
            public Material Plaza = CityMaterials.Lit("Praca", new Color(0.55f, 0.49f, 0.38f));
            public Material BusLane = CityMaterials.Lit("Corredor", new Color(0.62f, 0.25f, 0.22f));
            public Material Pothole = CityMaterials.Lit("Buraco", new Color(0.06f, 0.06f, 0.07f));
            public Material Fountain = CityMaterials.Lit("Fonte", new Color(0.55f, 0.65f, 0.7f));
            public Material RestaurantPad = CityMaterials.Unlit("MarcadorRestaurante", new Color(1f, 0.55f, 0.1f));
            public Material CustomerPad = CityMaterials.Unlit("MarcadorCliente", new Color(0.1f, 0.85f, 0.75f));
            public Material Beam = CityMaterials.Unlit("Feixe", new Color(1f, 1f, 0.3f));
            public Material[] Restaurants =
            {
                CityMaterials.Lit("Restaurante1", new Color(0.95f, 0.6f, 0.15f)),
                CityMaterials.Lit("Restaurante2", new Color(0.85f, 0.25f, 0.2f)),
                CityMaterials.Lit("Restaurante3", new Color(0.3f, 0.65f, 0.35f)),
                CityMaterials.Lit("Restaurante4", new Color(0.9f, 0.8f, 0.2f)),
            };
        }

        private static Transform Group(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            return go.transform;
        }

        // ---------- Grafo ----------

        private static RoadGraph BuildGraph(GameObject world)
        {
            var nodes = new Vector3[StreetCount * StreetCount];
            var from = new List<int>();
            var to = new List<int>();

            for (int i = 0; i < StreetCount; i++)
            {
                for (int j = 0; j < StreetCount; j++)
                {
                    nodes[i * StreetCount + j] = new Vector3(_cx[i], 0f, _cz[j]);

                    if (j + 1 < StreetCount)
                    {
                        from.Add(i * StreetCount + j);
                        to.Add(i * StreetCount + j + 1);
                    }

                    if (i + 1 < StreetCount)
                    {
                        from.Add(i * StreetCount + j);
                        to.Add((i + 1) * StreetCount + j);
                    }
                }
            }

            var graph = world.AddComponent<RoadGraph>();
            graph.Configure(nodes, from.ToArray(), to.ToArray());

            return graph;
        }

        // ---------- Restaurantes e endereços ----------

        private static Vector3 SpotPosition(EdgeSpot spot)
        {
            if (spot.Horizontal)
            {
                float x = (_cx[spot.Segment] + _cx[spot.Segment + 1]) * 0.5f;

                return new Vector3(x, 0f, _cz[spot.Street] + spot.Side * (_hwz[spot.Street] - 1.5f));
            }

            float z = (_cz[spot.Segment] + _cz[spot.Segment + 1]) * 0.5f;

            return new Vector3(_cx[spot.Street] + spot.Side * (_hwx[spot.Street] - 1.5f), 0f, z);
        }

        private static List<EdgeSpot> RestaurantSpots() => new List<EdgeSpot>
        {
            new EdgeSpot { Horizontal = false, Street = LargoBlockX, Segment = LargoBlockZ, Side = -1 },
            new EdgeSpot { Horizontal = true, Street = LargoBlockZ + 1, Segment = LargoBlockX, Side = 1 },
            new EdgeSpot { Horizontal = true, Street = LargoBlockZ, Segment = LargoBlockX, Side = -1 },
            new EdgeSpot { Horizontal = false, Street = AvenueIndex, Segment = LargoBlockZ, Side = 1 },
        };

        /// <summary>Marcadores e letreiros dos restaurantes. O prédio, os toldos e a faixa colorida saem em BuildBlocks.</summary>
        private static Place[] BuildRestaurants(Transform markers, Materials m, List<EdgeSpot> spots)
        {
            string[] names = { "Pastelaria do Largo", "Pizzaria Bella Amaro", "Marmitaria da Dona Cida", "Hamburgueria Zona Sul" };
            var places = new Place[names.Length];

            for (int k = 0; k < names.Length; k++)
            {
                Vector3 position = SpotPosition(spots[k]);
                GameObject beam = BuildMarker(markers, names[k], position, m.RestaurantPad, m);
                places[k] = new Place(names[k], position, beam);
            }

            return places;
        }

        private static Place[] BuildCustomers(Transform markers, Materials m, List<EdgeSpot> restaurantSpots)
        {
            var random = new System.Random(7);
            var candidates = new List<EdgeSpot>();

            for (int street = 0; street < StreetCount; street++)
            {
                for (int segment = 0; segment < BlockCount; segment++)
                {
                    foreach (int side in new[] { -1, 1 })
                    {
                        // Vertical (não a avenida) e horizontal; só lados que têm quarteirão.
                        if (street + side >= 0 && street + side < StreetCount)
                        {
                            if (street != AvenueIndex)
                            {
                                candidates.Add(new EdgeSpot { Horizontal = false, Street = street, Segment = segment, Side = side });
                            }

                            candidates.Add(new EdgeSpot { Horizontal = true, Street = street, Segment = segment, Side = side });
                        }
                    }
                }
            }

            // Embaralha de forma determinística.
            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int k = random.Next(i + 1);
                (candidates[i], candidates[k]) = (candidates[k], candidates[i]);
            }

            // Endereços espalhados de perto a longe do largo (onde ficam os restaurantes), para existir uma rota
            // para qualquer distância de pedido: o alvo de cada endereço sobe de MinCustomerMeters ao mais distante.
            var chosen = new List<Vector3>();
            var places = new List<Place>();
            var restaurantPositions = restaurantSpots.ConvertAll(SpotPosition);
            Vector3 largoCenter = new Vector3((BlockX0(LargoBlockX) + BlockX1(LargoBlockX)) * 0.5f, 0f, (BlockZ0(LargoBlockZ) + BlockZ1(LargoBlockZ)) * 0.5f);

            var pool = new List<(Vector3 position, float distance)>();

            foreach (EdgeSpot spot in candidates)
            {
                Vector3 position = SpotPosition(spot);
                bool tooClose = Vector3.Distance(position, largoCenter) < 70f;

                foreach (Vector3 other in restaurantPositions)
                {
                    tooClose |= Vector3.Distance(position, other) < 80f;
                }

                if (!tooClose)
                {
                    pool.Add((position, Mathf.Abs(position.x - largoCenter.x) + Mathf.Abs(position.z - largoCenter.z)));
                }
            }

            float maxDistance = 0f;

            foreach (var entry in pool)
            {
                maxDistance = Mathf.Max(maxDistance, entry.distance);
            }

            for (int k = 0; k < CustomerStreets.Length; k++)
            {
                float target = Mathf.Lerp(MinCustomerMeters, maxDistance, k / (float)(CustomerStreets.Length - 1));
                int best = -1;
                float bestError = float.MaxValue;

                for (int c = 0; c < pool.Count; c++)
                {
                    bool tooClose = false;

                    foreach (Vector3 other in chosen)
                    {
                        tooClose |= Vector3.Distance(pool[c].position, other) < 60f;
                    }

                    float error = Mathf.Abs(pool[c].distance - target);

                    if (!tooClose && error < bestError)
                    {
                        bestError = error;
                        best = c;
                    }
                }

                if (best < 0)
                {
                    break;
                }

                Vector3 position = pool[best].position;
                chosen.Add(position);
                string name = $"{CustomerStreets[places.Count]}, {random.Next(20, 900)}";
                GameObject beam = BuildMarker(markers, name, position, m.CustomerPad, m);
                places.Add(new Place(name, position, beam));
            }

            if (places.Count < CustomerStreets.Length)
            {
                throw new InvalidOperationException($"Só foi possível posicionar {places.Count} endereços de cliente.");
            }

            return places.ToArray();
        }

        /// <summary>Marcador no chão (disco) com um feixe de luz alto, que só acende quando é o alvo.</summary>
        private static GameObject BuildMarker(Transform parent, string name, Vector3 position, Material padMaterial, Materials m)
        {
            Transform holder = Group(parent, "Marcador_" + name);
            holder.position = position;
            CityPieces.Cylinder(holder, "Disco", position + Vector3.up * 0.06f, 3.5f, 0.08f, padMaterial);
            GameObject beam = CityPieces.Cylinder(holder, "Feixe", position + Vector3.up * 15f, 0.7f, 30f, m.Beam);
            beam.SetActive(false);

            return beam;
        }

        // ---------- Buracos ----------

        private static List<Vector3> BuildPotholes(Transform parent, Materials m, System.Random random, Place[] restaurants, Place[] customers)
        {
            var positions = new List<Vector3>();
            int made = 0;
            int guard = 0;

            while (made < 14 && guard++ < 800)
            {
                bool horizontal = random.Next(2) == 0;
                int street = random.Next(StreetCount);
                int segment = random.Next(BlockCount);

                if (!horizontal && street == AvenueIndex)
                {
                    continue;
                }

                float t = 0.3f + 0.4f * (float)random.NextDouble();
                float lateral = ((float)random.NextDouble() - 0.5f) * 5f;
                Vector3 position = horizontal
                    ? new Vector3(Mathf.Lerp(_cx[segment], _cx[segment + 1], t), 0f, _cz[street] + lateral)
                    : new Vector3(_cx[street] + lateral, 0f, Mathf.Lerp(_cz[segment], _cz[segment + 1], t));

                bool nearMarker = false;

                foreach (Place place in restaurants)
                {
                    nearMarker |= Vector3.Distance(place.Position, position) < 25f;
                }

                foreach (Place place in customers)
                {
                    nearMarker |= Vector3.Distance(place.Position, position) < 25f;
                }

                if (nearMarker)
                {
                    continue;
                }

                // Buraco: remendo de asfalto escuro, borda esfarelada e a cratera quase preta no meio (irregulares).
                var patch = new MeshBatch();
                float spin = (float)random.NextDouble() * 6f;
                float[] outer = new float[10];
                float[] mid = new float[10];
                float[] pit = new float[10];

                for (int v = 0; v < 10; v++)
                {
                    float jitter = 0.78f + 0.4f * (float)random.NextDouble();
                    outer[v] = 1.55f * jitter;
                    mid[v] = 1.15f * (0.8f + 0.4f * (float)random.NextDouble());
                    pit[v] = 0.7f * (0.7f + 0.5f * (float)random.NextDouble());
                }

                patch.Fan(CityStyle.AsphaltPatch(), position + Vector3.up * 0.012f, outer, spin);
                patch.Fan(PbrMaterials.Flat("BuracoBorda", new Color(0.09f, 0.09f, 0.1f), 0.1f), position + Vector3.up * 0.016f, mid, spin + 0.3f);
                patch.Fan(PbrMaterials.Flat("BuracoFundo", new Color(0.025f, 0.025f, 0.03f), 0.05f), position + Vector3.up * 0.02f, pit, spin + 0.7f);
                GameObject hole = patch.Build(parent, "Buraco" + made, false);
                GameObject zone = new GameObject("ZonaBuraco" + made);
                zone.transform.SetParent(hole.transform.parent, false);
                zone.transform.position = position + Vector3.up * 1f;
                var box = zone.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(3.4f, 2f, 3.4f);
                zone.AddComponent<PotholeZone>();
                positions.Add(position);

                // Cones de obra em volta do buraco.
                for (int c = 0; c < 3; c++)
                {
                    float angle = (c * 120f + 40f * (float)random.NextDouble()) * Mathf.Deg2Rad;
                    Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 2.6f;
                    CityPieces.Model(hole.transform.parent, Kit.CityKitRoads, "construction-cone", position + offset + Vector3.up * 0.04f, 0f, 9f).name = "Cone";
                }

                made++;
            }

            return positions;
        }

        // ---------- Luz, câmeras, rota ----------

        private static Camera BuildMainCamera(ArcadeBikeController bike)
        {
            var go = new GameObject("Camera");
            go.tag = "MainCamera";
            var camera = go.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 600f;
            camera.fieldOfView = 62f;
            go.AddComponent<AudioListener>();
            go.AddComponent<FollowCamera>().Configure(bike);

            var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;

            go.transform.position = bike.transform.position + new Vector3(0f, 3.6f, -7.5f);

            return camera;
        }

        private static MinimapView BuildMinimap(ArcadeBikeController bike)
        {
            var go = new GameObject("CameraMinimapa");
            var camera = go.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.36f, 0.5f, 0.3f);
            camera.orthographic = true;
            camera.orthographicSize = 120f;
            camera.nearClipPlane = 1f;
            camera.farClipPlane = 400f;
            camera.cullingMask = ~(1 << 2);
            camera.depth = -10f;
            var data = camera.GetUniversalAdditionalCameraData();
            data.renderShadows = false;
            data.renderPostProcessing = true;

            go.transform.position = bike.transform.position + Vector3.up * 150f;
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            var view = go.AddComponent<MinimapView>();
            view.Configure(bike);

            return view;
        }

        /// <summary>Textura de setas (chevrons) do GPS: uma seta por 2,4 m de fita, apontando para a frente da linha.</summary>
        private static Texture2D ChevronTexture()
        {
            const string path = CityMaterials.Folder + "/RotaSeta.png";
            const int width = 192;
            const int height = 80;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color32[width * height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width;
                    float v = (y + 0.5f) / height;

                    // Seta ">" com pontas em u = 0,2 e 0,75 e vértice no meio da largura.
                    float spine = 0.75f - Mathf.Abs(v - 0.5f) * 1.25f;
                    bool arrow = Mathf.Abs(u - spine) < 0.09f && v > 0.08f && v < 0.92f;
                    pixels[y * width + x] = arrow ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            Directory.CreateDirectory(CityMaterials.Folder);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static RouteGuide BuildRoute(RoadGraph graph, ArcadeBikeController bike)
        {
            var go = new GameObject("RotaGPS");
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.alignment = LineAlignment.TransformZ;
            line.textureMode = LineTextureMode.Tile;
            line.widthMultiplier = RouteWidth;
            line.numCornerVertices = 2;
            line.numCapVertices = 0;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;

            Material material = CityMaterials.Unlit("RotaGPS", new Color(0.15f, 0.85f, 1f), true);
            material.SetTexture("_BaseMap", ChevronTexture());
            material.SetFloat("_AlphaClip", 1f);
            material.SetFloat("_Cutoff", 0.5f);
            material.EnableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)RenderQueue.AlphaTest;
            material.SetTextureScale("_BaseMap", new Vector2(1f / 2.4f, 1f));
            EditorUtility.SetDirty(material);
            line.sharedMaterial = material;
            line.positionCount = 0;

            var guide = go.AddComponent<RouteGuide>();
            guide.Configure(graph, line, bike);

            return guide;
        }

        /// <summary>Largura da fita do GPS em metros (a câmera de captura dos testes a alarga para o mapa de cima).</summary>
        public const float RouteWidth = 1f;

        /// <summary>Chuva leve: partículas (traços finos) numa caixa sobre a câmera. Só liga nos dias de chuva.</summary>
        private static RainEffect BuildRain(Transform camera)
        {
            var holder = new GameObject("Chuva");
            holder.transform.SetParent(camera, false);
            holder.layer = 2;
            var rain = holder.AddComponent<RainEffect>();

            var drops = new GameObject("Gotas");
            drops.transform.SetParent(holder.transform, false);
            drops.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            drops.layer = 2;
            var system = drops.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 0.55f;
            main.startSpeed = 28f;
            main.startSize = 0.05f;
            main.startColor = new Color(0.85f, 0.9f, 1f, 0.55f);
            main.maxParticles = 1500;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 1100f;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(44f, 44f, 0.1f);

            var renderer = drops.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 1f;
            renderer.velocityScale = 0.035f;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = RainMaterial();

            rain.Configure(system, camera);

            return rain;
        }

        private static Material RainMaterial()
        {
            Material material = CityMaterials.Unlit("Chuva", new Color(0.85f, 0.9f, 1f, 0.6f));
            material.shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetColor("_BaseColor", new Color(0.85f, 0.9f, 1f, 0.6f));
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);

            return material;
        }

        private static void MarkStatic(Transform root)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.GetComponent<RoadGraph>() == null && child.GetComponent<CityMap>() == null && child.GetComponent<FaceCamera>() == null && child.gameObject.activeSelf)
                {
                    GameObjectUtility.SetStaticEditorFlags(child.gameObject, StaticEditorFlags.BatchingStatic);
                }
            }
        }
    }
}
