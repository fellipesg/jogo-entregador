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
    public static class CityBuilder
    {
        public const string ScenePath = "Assets/_Jogo/Scenes/SantoAmaro.unity";

        // Medidas em metros.
        private const int StreetCount = 10;
        private const int BlockCount = StreetCount - 1;
        private const float BlockSize = 100f;
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
            ComputeGrid();

            Directory.CreateDirectory("Assets/_Jogo/Scenes");
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

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
            BuildBlocks(blocks, buildings, props, m, random, out Vector3 largoCenter);

            RoadGraph graph = BuildGraph(world);

            Place[] restaurants = BuildRestaurants(markers, buildings, m, out List<EdgeSpot> restaurantSpots);
            Place[] customers = BuildCustomers(markers, m, restaurantSpots);
            BuildPotholes(potholes, m, new System.Random(99), restaurants, customers);

            Vector3 spawn = largoCenter + new Vector3(14f, 0.1f, -18f);
            var map = world.AddComponent<CityMap>();
            map.Configure(restaurants, customers, graph, spawn, 0f, Vector2.zero, _span);

            ArcadeBikeController bike = BikeFactory.Build(spawn, 0f);
            var tracker = bike.gameObject.AddComponent<DeliveryRunTracker>();
            tracker.Configure(bike);

            BuildLighting(m, out Light sun);
            Camera main = BuildMainCamera(bike);
            MinimapView minimap = BuildMinimap(bike);
            RouteGuide route = BuildRoute(graph, bike);

            var root = new GameObject("GameRoot");
            var tuning = root.AddComponent<GameTuning>();
            root.AddComponent<FakeAdService>();
            var ui = new GameObject("Interface").AddComponent<GameUI>();
            ui.transform.SetParent(root.transform, false);
            var flow = root.AddComponent<GameFlow>();
            flow.Configure(map, bike, bike.GetComponent<BikeInput>(), tracker, route, main.GetComponent<FollowCamera>(), minimap, ui, tuning, sun, main);

            MarkStatic(world.transform);

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
            public Material Asphalt = CityMaterials.Lit("Asfalto", new Color(0.22f, 0.23f, 0.25f));
            public Material Sidewalk = CityMaterials.Lit("Calcada", new Color(0.62f, 0.61f, 0.58f));
            public Material Plaza = CityMaterials.Lit("Praca", new Color(0.78f, 0.72f, 0.6f));
            public Material Line = CityMaterials.Lit("FaixaRua", new Color(0.9f, 0.85f, 0.4f));
            public Material BusLane = CityMaterials.Lit("Corredor", new Color(0.62f, 0.25f, 0.22f));
            public Material Pothole = CityMaterials.Lit("Buraco", new Color(0.06f, 0.06f, 0.07f));
            public Material Trunk = CityMaterials.Lit("Tronco", new Color(0.4f, 0.28f, 0.18f));
            public Material Leaves = CityMaterials.Lit("Folhas", new Color(0.25f, 0.5f, 0.25f));
            public Material Fountain = CityMaterials.Lit("Fonte", new Color(0.55f, 0.65f, 0.7f));
            public Material RestaurantPad = CityMaterials.Unlit("MarcadorRestaurante", new Color(1f, 0.55f, 0.1f));
            public Material CustomerPad = CityMaterials.Unlit("MarcadorCliente", new Color(0.1f, 0.85f, 0.75f));
            public Material Beam = CityMaterials.Unlit("Feixe", new Color(1f, 1f, 0.3f));
            public Material Pole = CityMaterials.Lit("Poste", new Color(0.3f, 0.3f, 0.32f));
            public Material Route = CityMaterials.Unlit("RotaGPS", new Color(0.15f, 0.85f, 1f), true);
            public Material[] Buildings =
            {
                CityMaterials.Lit("Predio1", new Color(0.72f, 0.66f, 0.58f)),
                CityMaterials.Lit("Predio2", new Color(0.6f, 0.64f, 0.66f)),
                CityMaterials.Lit("Predio3", new Color(0.7f, 0.58f, 0.52f)),
                CityMaterials.Lit("Predio4", new Color(0.55f, 0.6f, 0.52f)),
                CityMaterials.Lit("Predio5", new Color(0.78f, 0.74f, 0.66f)),
                CityMaterials.Lit("Predio6", new Color(0.5f, 0.52f, 0.58f)),
            };
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

        // ---------- Chão e ruas ----------

        private static void BuildGround(Transform world, Materials m)
        {
            float size = _span + 400f;
            CityPieces.Box(world, "Chao", new Vector3(0f, -0.5f, 0f), new Vector3(size, 1f, size), m.Grass, true);

            // Paredes invisíveis na borda do mapa.
            float half = _span * 0.5f + 20f;
            Wall(world, "LimiteNorte", new Vector3(0f, 2.5f, half), new Vector3(2f * half, 5f, 2f));
            Wall(world, "LimiteSul", new Vector3(0f, 2.5f, -half), new Vector3(2f * half, 5f, 2f));
            Wall(world, "LimiteLeste", new Vector3(half, 2.5f, 0f), new Vector3(2f, 5f, 2f * half));
            Wall(world, "LimiteOeste", new Vector3(-half, 2.5f, 0f), new Vector3(2f, 5f, 2f * half));
        }

        private static void Wall(Transform parent, string name, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.AddComponent<BoxCollider>().size = size;
        }

        private static void BuildRoads(Transform parent, Materials m)
        {
            float zMin = _cz[0] - StreetHalfWidth;
            float zMax = _cz[StreetCount - 1] + StreetHalfWidth;
            float xMin = _cx[0] - _hwx[0];
            float xMax = _cx[StreetCount - 1] + _hwx[StreetCount - 1];

            for (int i = 0; i < StreetCount; i++)
            {
                CityPieces.Box(parent, "RuaV" + i, new Vector3(_cx[i], 0.01f, (zMin + zMax) * 0.5f), new Vector3(_hwx[i] * 2f, 0.03f, zMax - zMin), m.Asphalt);

                if (i == AvenueIndex)
                {
                    CityPieces.Box(parent, "CorredorOnibus", new Vector3(_cx[i], 0.03f, (zMin + zMax) * 0.5f), new Vector3(6f, 0.03f, zMax - zMin), m.BusLane);
                }
            }

            for (int j = 0; j < StreetCount; j++)
            {
                CityPieces.Box(parent, "RuaH" + j, new Vector3((xMin + xMax) * 0.5f, 0.015f, _cz[j]), new Vector3(xMax - xMin, 0.03f, StreetHalfWidth * 2f), m.Asphalt);
            }

            // Faixa central entre cruzamentos (a avenida tem o corredor de ônibus no lugar).
            for (int i = 0; i < StreetCount; i++)
            {
                if (i == AvenueIndex)
                {
                    continue;
                }

                for (int j = 0; j < BlockCount; j++)
                {
                    float z0 = BlockZ0(j) + 4f;
                    float z1 = BlockZ1(j) - 4f;
                    CityPieces.Box(parent, "FaixaV", new Vector3(_cx[i], 0.045f, (z0 + z1) * 0.5f), new Vector3(0.3f, 0.03f, z1 - z0), m.Line);
                }
            }

            for (int j = 0; j < StreetCount; j++)
            {
                for (int i = 0; i < BlockCount; i++)
                {
                    float x0 = BlockX0(i) + 4f;
                    float x1 = BlockX1(i) - 4f;
                    CityPieces.Box(parent, "FaixaH", new Vector3((x0 + x1) * 0.5f, 0.05f, _cz[j]), new Vector3(x1 - x0, 0.03f, 0.3f), m.Line);
                }
            }
        }

        // ---------- Quarteirões ----------

        private static void BuildBlocks(Transform blocks, Transform buildings, Transform props, Materials m, System.Random random, out Vector3 largoCenter)
        {
            largoCenter = Vector3.zero;

            for (int i = 0; i < BlockCount; i++)
            {
                for (int j = 0; j < BlockCount; j++)
                {
                    float x0 = BlockX0(i);
                    float x1 = BlockX1(i);
                    float z0 = BlockZ0(j);
                    float z1 = BlockZ1(j);
                    var center = new Vector3((x0 + x1) * 0.5f, 0f, (z0 + z1) * 0.5f);

                    if (i == LargoBlockX && j == LargoBlockZ)
                    {
                        largoCenter = center;
                        BuildLargo(blocks, props, m, x0, x1, z0, z1);

                        continue;
                    }

                    CityPieces.Box(blocks, $"Calcada_{i}_{j}", new Vector3(center.x, 0.09f, center.z), new Vector3(x1 - x0, 0.18f, z1 - z0), m.Sidewalk);
                    Wall(blocks, $"Barreira_{i}_{j}", new Vector3(center.x, 1.5f, center.z), new Vector3(x1 - x0, 3f, z1 - z0));

                    bool nearAvenue = i == AvenueIndex - 1 || i == AvenueIndex;
                    float cellW = (x1 - x0 - 2f * SidewalkInset - 1.5f) * 0.5f;
                    float cellD = (z1 - z0 - 2f * SidewalkInset - 1.5f) * 0.5f;

                    for (int cx = 0; cx < 2; cx++)
                    {
                        for (int cz = 0; cz < 2; cz++)
                        {
                            float px = x0 + SidewalkInset + cellW * (cx + 0.5f) + 1.5f * cx;
                            float pz = z0 + SidewalkInset + cellD * (cz + 0.5f) + 1.5f * cz;
                            float w = cellW * (0.8f + 0.2f * (float)random.NextDouble());
                            float d = cellD * (0.8f + 0.2f * (float)random.NextDouble());
                            float h = nearAvenue ? 20f + 25f * (float)random.NextDouble() : 8f + 16f * (float)random.NextDouble();
                            Material material = m.Buildings[random.Next(m.Buildings.Length)];
                            CityPieces.Building(buildings, $"Predio_{i}_{j}_{cx}{cz}", new Vector3(px, 0.18f, pz), new Vector3(w, h, d), material);
                        }
                    }
                }
            }
        }

        private static void BuildLargo(Transform blocks, Transform props, Materials m, float x0, float x1, float z0, float z1)
        {
            var center = new Vector3((x0 + x1) * 0.5f, 0f, (z0 + z1) * 0.5f);
            CityPieces.Box(blocks, "LargoPiso", new Vector3(center.x, 0.05f, center.z), new Vector3(x1 - x0, 0.1f, z1 - z0), m.Plaza);
            CityPieces.Cylinder(props, "LargoFonte", new Vector3(center.x, 0.6f, center.z), 4f, 1.2f, m.Fountain, true);
            CityPieces.Cylinder(props, "LargoFonteTopo", new Vector3(center.x, 1.8f, center.z), 1.1f, 1.6f, m.Fountain);

            // Árvores em volta, longe das ruas e do caminho do meio.
            var spots = new[]
            {
                new Vector2(-32f, -32f), new Vector2(32f, -32f), new Vector2(-32f, 32f), new Vector2(32f, 32f),
                new Vector2(0f, 36f), new Vector2(0f, -36f), new Vector2(-36f, 0f), new Vector2(36f, 0f),
            };

            for (int k = 0; k < spots.Length; k++)
            {
                CityPieces.Tree(props, "LargoArvore" + k, new Vector3(center.x + spots[k].x, 0f, center.z + spots[k].y), m.Trunk, m.Leaves);
            }
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

        private static Place[] BuildRestaurants(Transform markers, Transform buildings, Materials m, out List<EdgeSpot> spots)
        {
            string[] names = { "Pastelaria do Largo", "Pizzaria Bella Amaro", "Marmitaria da Dona Cida", "Hamburgueria Zona Sul" };
            spots = new List<EdgeSpot>
            {
                new EdgeSpot { Horizontal = false, Street = LargoBlockX, Segment = LargoBlockZ, Side = -1 },
                new EdgeSpot { Horizontal = true, Street = LargoBlockZ + 1, Segment = LargoBlockX, Side = 1 },
                new EdgeSpot { Horizontal = true, Street = LargoBlockZ, Segment = LargoBlockX, Side = -1 },
                new EdgeSpot { Horizontal = false, Street = AvenueIndex, Segment = LargoBlockZ, Side = 1 },
            };

            var places = new Place[names.Length];

            for (int k = 0; k < names.Length; k++)
            {
                Vector3 position = SpotPosition(spots[k]);
                GameObject beam = BuildMarker(markers, names[k], position, m.RestaurantPad, m);
                places[k] = new Place(names[k], position, beam);

                // Fachada colorida do restaurante, do lado do quarteirão.
                EdgeSpot spot = spots[k];
                float hw = spot.Horizontal ? _hwz[spot.Street] : _hwx[spot.Street];
                float depth = hw + SidewalkInset + 6f;
                Vector3 center = spot.Horizontal
                    ? new Vector3(position.x, 0.18f, _cz[spot.Street] + spot.Side * depth)
                    : new Vector3(_cx[spot.Street] + spot.Side * depth, 0.18f, position.z);
                Vector3 size = spot.Horizontal ? new Vector3(24f, 7f, 12f) : new Vector3(12f, 7f, 24f);
                CityPieces.Building(buildings, "Fachada_" + names[k], center, size, m.Restaurants[k % m.Restaurants.Length]);

                // Letreiro: poste alto com texto que olha para a câmera.
                var label = new GameObject("Letreiro");
                label.transform.SetParent(beam.transform.parent, false);
                label.transform.position = position + Vector3.up * 11f;
                var text = label.AddComponent<TextMesh>();
                text.text = names[k];
                text.fontSize = 64;
                text.characterSize = 0.14f;
                text.anchor = TextAnchor.MiddleCenter;
                text.alignment = TextAlignment.Center;
                text.color = Color.white;
                label.AddComponent<FaceCamera>();
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

            var chosen = new List<Vector3>();
            var places = new List<Place>();
            var restaurantPositions = restaurantSpots.ConvertAll(SpotPosition);
            Vector3 largoCenter = new Vector3((BlockX0(LargoBlockX) + BlockX1(LargoBlockX)) * 0.5f, 0f, (BlockZ0(LargoBlockZ) + BlockZ1(LargoBlockZ)) * 0.5f);

            foreach (EdgeSpot spot in candidates)
            {
                if (places.Count >= CustomerStreets.Length)
                {
                    break;
                }

                Vector3 position = SpotPosition(spot);
                bool tooClose = Vector3.Distance(position, largoCenter) < 70f;

                foreach (Vector3 other in chosen)
                {
                    tooClose |= Vector3.Distance(position, other) < 95f;
                }

                foreach (Vector3 other in restaurantPositions)
                {
                    tooClose |= Vector3.Distance(position, other) < 80f;
                }

                if (tooClose)
                {
                    continue;
                }

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

        private static void BuildPotholes(Transform parent, Materials m, System.Random random, Place[] restaurants, Place[] customers)
        {
            int made = 0;
            int guard = 0;

            while (made < 10 && guard++ < 500)
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

                GameObject hole = CityPieces.Cylinder(parent, "Buraco" + made, position + Vector3.up * 0.06f, 1.6f, 0.06f, m.Pothole);
                GameObject zone = new GameObject("ZonaBuraco" + made);
                zone.transform.SetParent(hole.transform.parent, false);
                zone.transform.position = position + Vector3.up * 1f;
                var box = zone.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(3.4f, 2f, 3.4f);
                zone.AddComponent<PotholeZone>();
                made++;
            }
        }

        // ---------- Luz, câmeras, rota ----------

        private static void BuildLighting(Materials m, out Light sun)
        {
            var lightObject = new GameObject("Sol");
            lightObject.transform.rotation = Quaternion.Euler(52f, -30f, 0f);
            sun = lightObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.intensity = 1.2f;
            sun.shadows = LightShadows.Hard;

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.58f, 0.64f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 260f;
            RenderSettings.fogEndDistance = 650f;
            RenderSettings.fogColor = new Color(0.55f, 0.75f, 0.95f);
            RenderSettings.sun = sun;
        }

        private static Camera BuildMainCamera(ArcadeBikeController bike)
        {
            var go = new GameObject("Camera");
            go.tag = "MainCamera";
            var camera = go.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.55f, 0.75f, 0.95f);
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 800f;
            camera.fieldOfView = 62f;
            go.AddComponent<AudioListener>();
            go.AddComponent<FollowCamera>().Configure(bike);

            var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;

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
            camera.depth = -10f;
            var data = camera.GetUniversalAdditionalCameraData();
            data.renderShadows = false;
            data.renderPostProcessing = false;

            go.transform.position = bike.transform.position + Vector3.up * 150f;
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            var view = go.AddComponent<MinimapView>();
            view.Configure(bike);

            return view;
        }

        private static RouteGuide BuildRoute(RoadGraph graph, ArcadeBikeController bike)
        {
            var go = new GameObject("RotaGPS");
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.alignment = LineAlignment.TransformZ;
            line.widthMultiplier = 1.2f;
            line.numCornerVertices = 4;
            line.numCapVertices = 4;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = CityMaterials.Unlit("RotaGPS", new Color(0.15f, 0.85f, 1f), true);
            line.positionCount = 0;

            var guide = go.AddComponent<RouteGuide>();
            guide.Configure(graph, line, bike);

            return guide;
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
