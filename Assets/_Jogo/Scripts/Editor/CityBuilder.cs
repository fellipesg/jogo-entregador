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

            BuildLighting(m, out Light sun);
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

        // ---------- Ruas (tiles do Kit City Roads) ----------

        /// <summary>Altura do meio-fio das calçadas, em metros. A moto sobe por uma rampa de colisão.</summary>
        private const float CurbHeight = 0.15f;

        /// <summary>Quanto a rampa avança na calçada (m). 0,15 m em 0,8 m dá cerca de 10,6 graus.</summary>
        private const float CurbRun = 0.8f;

        /// <summary>Distância do alinhamento da frente dos prédios até a esquina do quarteirão.</summary>
        private const float FrontInset = SidewalkInset;

        /// <summary>Fundo máximo de um prédio na fileira (m); o miolo do quarteirão fica vazio.</summary>
        private const float RowDepth = 15f;

        private const float TileLength = 25f;

        private static void BuildRoads(Transform parent, Materials m)
        {
            // Ruas verticais (ao longo de Z). A avenida são duas colunas de tile de 12 m lado a lado.
            for (int i = 0; i < StreetCount; i++)
            {
                float[] columns = i == AvenueIndex ? new[] { _cx[i] - StreetHalfWidth, _cx[i] + StreetHalfWidth } : new[] { _cx[i] };

                foreach (float x in columns)
                {
                    for (int j = 0; j < StreetCount; j++)
                    {
                        CityPieces.RoadTile(parent, "road-crossroad", new Vector3(x, 0f, _cz[j]), 0f, 12f, 12f);
                    }

                    for (int j = 0; j < BlockCount; j++)
                    {
                        LaySpan(parent, new Vector3(x, 0f, BlockZ0(j)), new Vector3(x, 0f, BlockZ1(j)), 90f);
                    }
                }
            }

            // Ruas horizontais (ao longo de X): só os trechos entre os cruzamentos, que já foram colocados acima.
            for (int j = 0; j < StreetCount; j++)
            {
                for (int i = 0; i < BlockCount; i++)
                {
                    LaySpan(parent, new Vector3(BlockX0(i), 0f, _cz[j]), new Vector3(BlockX1(i), 0f, _cz[j]), 0f);
                }
            }

            // Corredor de ônibus vermelho no meio da avenida, só nos trechos (os cruzamentos ficam livres).
            for (int j = 0; j < BlockCount; j++)
            {
                float z0 = BlockZ0(j);
                float z1 = BlockZ1(j);
                CityPieces.Box(parent, "CorredorOnibus", new Vector3(_cx[AvenueIndex], 0.05f, (z0 + z1) * 0.5f), new Vector3(5.2f, 0.02f, z1 - z0), m.BusLane);
            }
        }

        /// <summary>Cobre o trecho de rua de a até b com tiles de 25 m (12 m de largura).</summary>
        private static void LaySpan(Transform parent, Vector3 a, Vector3 b, float yaw)
        {
            float length = Vector3.Distance(a, b);
            int count = Mathf.Max(1, Mathf.RoundToInt(length / TileLength));
            float step = length / count;
            Vector3 direction = (b - a).normalized;

            for (int k = 0; k < count; k++)
            {
                Vector3 center = a + direction * (step * (k + 0.5f));
                CityPieces.RoadTile(parent, "road-straight", center, yaw, 12f, step);
            }
        }

        // ---------- Quarteirões ----------

        private static bool IsCommercialBlock(int i, int j) =>
            i == AvenueIndex - 1 || i == AvenueIndex || (j >= LargoBlockZ - 1 && j <= LargoBlockZ + 1 && i >= AvenueIndex - 2 && i <= AvenueIndex + 1);

        private static readonly string[] CommercialModels =
        {
            "building-a", "building-b", "building-c", "building-d", "building-e", "building-f", "building-g",
            "building-h", "building-i", "building-j", "building-k", "building-l", "building-m", "building-n",
        };

        private static readonly string[] SkyscraperModels =
        {
            "building-skyscraper-a", "building-skyscraper-b", "building-skyscraper-c", "building-skyscraper-d", "building-skyscraper-e",
        };

        private static readonly string[] HouseModels =
        {
            "building-type-a", "building-type-b", "building-type-c", "building-type-d", "building-type-e", "building-type-f",
            "building-type-g", "building-type-h", "building-type-i", "building-type-j", "building-type-k", "building-type-l",
            "building-type-m", "building-type-n", "building-type-o", "building-type-p", "building-type-q", "building-type-r",
            "building-type-s", "building-type-t", "building-type-u",
        };

        private static void BuildBlocks(Transform blocks, Transform buildings, Transform props, Materials m, System.Random random, List<EdgeSpot> restaurantSpots, out Vector3 largoCenter)
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

                    Pavement(blocks, $"Calcada_{i}_{j}", x0, x1, z0, z1, CurbHeight, m.Sidewalk);

                    bool commercialBlock = IsCommercialBlock(i, j);

                    // Miolo (quintais ou pátio de concreto) visto de cima, escondido atrás das fileiras de prédios.
                    CityPieces.Box(blocks, $"Miolo_{i}_{j}", new Vector3(center.x, CurbHeight + 0.01f, center.z), new Vector3(x1 - x0 - 14f, 0.02f, z1 - z0 - 14f), commercialBlock ? m.Courtyard : m.Yard);

                    // Miolo sólido: o que há atrás da frente dos prédios. Um collider por quarteirão.
                    var solid = new GameObject($"Predios_{i}_{j}");
                    solid.transform.SetParent(blocks, false);
                    solid.transform.position = new Vector3(center.x, CurbHeight + 4f, center.z);
                    solid.AddComponent<BoxCollider>().size = new Vector3(x1 - x0 - 2f * FrontInset, 8f, z1 - z0 - 2f * FrontInset);

                    bool commercial = commercialBlock;
                    var rowRandom = new System.Random(1000 + i * 31 + j);

                    // Quatro fileiras de fachadas voltadas para as ruas.
                    FillRow(buildings, props, commercial, i, new Vector3(x0 + FrontInset, 0f, z0 + FrontInset), Vector3.right, Vector3.back, x1 - x0 - 2f * FrontInset, FindRestaurant(restaurantSpots, true, j, i, 1), rowRandom, m);
                    FillRow(buildings, props, commercial, i, new Vector3(x1 - FrontInset, 0f, z1 - FrontInset), Vector3.left, Vector3.forward, x1 - x0 - 2f * FrontInset, FindRestaurant(restaurantSpots, true, j + 1, i, -1), rowRandom, m);
                    float sideLength = z1 - z0 - 2f * FrontInset - 2f * RowDepth;
                    FillRow(buildings, props, commercial, i, new Vector3(x0 + FrontInset, 0f, z0 + FrontInset + RowDepth), Vector3.forward, Vector3.left, sideLength, FindRestaurant(restaurantSpots, false, i, j, 1), rowRandom, m);
                    FillRow(buildings, props, commercial, i, new Vector3(x1 - FrontInset, 0f, z1 - FrontInset - RowDepth), Vector3.back, Vector3.right, sideLength, FindRestaurant(restaurantSpots, false, i + 1, j, -1), rowRandom, m);
                }
            }
        }

        /// <summary>Índice do restaurante cujo ponto está nesta borda do quarteirão, ou -1.</summary>
        private static int FindRestaurant(List<EdgeSpot> spots, bool horizontal, int street, int segment, int side)
        {
            for (int k = 0; k < spots.Count; k++)
            {
                EdgeSpot s = spots[k];

                if (s.Horizontal == horizontal && s.Street == street && s.Segment == segment && s.Side == side)
                {
                    return k;
                }
            }

            return -1;
        }

        /// <summary>
        /// Calçada/piso: caixa visual de altura height (sem collider), uma laje plana com collider e quatro rampas
        /// de meio-fio. Assim a moto sobe a calçada em vez de bater numa parede.
        /// </summary>
        private static void Pavement(Transform parent, string name, float x0, float x1, float z0, float z1, float height, Material material)
        {
            float cx = (x0 + x1) * 0.5f;
            float cz = (z0 + z1) * 0.5f;
            Transform holder = Group(parent, name);
            CityPieces.Box(holder, "Piso", new Vector3(cx, height * 0.5f, cz), new Vector3(x1 - x0, height, z1 - z0), material);

            var slab = new GameObject("Laje");
            slab.transform.SetParent(holder, false);
            slab.transform.position = new Vector3(cx, height * 0.5f, cz);
            slab.AddComponent<BoxCollider>().size = new Vector3(x1 - x0 - 2f * CurbRun, height, z1 - z0 - 2f * CurbRun);

            Ramp(holder, new Vector3(x0, 0f, cz), Vector3.right, z1 - z0, height);
            Ramp(holder, new Vector3(x1, 0f, cz), Vector3.left, z1 - z0, height);
            Ramp(holder, new Vector3(cx, 0f, z0), Vector3.forward, x1 - x0, height);
            Ramp(holder, new Vector3(cx, 0f, z1), Vector3.back, x1 - x0, height);
        }

        private static void Ramp(Transform parent, Vector3 edgePoint, Vector3 inward, float edgeLength, float height)
        {
            Vector3 climb = (inward * CurbRun + Vector3.up * height).normalized;
            Quaternion rotation = Quaternion.LookRotation(climb, Vector3.up);
            float slope = Mathf.Sqrt(CurbRun * CurbRun + height * height);
            const float thickness = 0.1f;

            var go = new GameObject("RampaMeioFio");
            go.transform.SetParent(parent, false);
            Vector3 surfaceMiddle = edgePoint + inward * (CurbRun * 0.5f) + Vector3.up * (height * 0.5f);
            go.transform.SetPositionAndRotation(surfaceMiddle - (rotation * Vector3.up) * (thickness * 0.5f), rotation);
            go.AddComponent<BoxCollider>().size = new Vector3(edgeLength, thickness, slope);
        }

        /// <summary>
        /// Preenche uma borda do quarteirão com prédios (comerciais ou casas) lado a lado, voltados para a rua.
        /// start é o início da borda na linha da frente; along é a direção ao longo dela; facing é a direção da frente.
        /// Um restaurante (índice >= 0) ocupa o meio da borda com prédio maior, toldos, guarda-sóis e letreiro.
        /// </summary>
        private static void FillRow(Transform parent, Transform props, bool commercial, int column, Vector3 start, Vector3 along, Vector3 facing, float length, int restaurant, System.Random random, Materials m)
        {
            if (restaurant < 0)
            {
                FillSegment(parent, props, commercial, column, start, along, facing, length, random);

                return;
            }

            const float slot = 26f;
            float half = (length - slot) * 0.5f;
            FillSegment(parent, props, commercial, column, start, along, facing, half, random);
            FillSegment(parent, props, commercial, column, start + along * (half + slot), along, facing, half, random);
            BuildRestaurantFront(parent, props, restaurant, start + along * (half + slot * 0.5f), facing, m);
        }

        private static void FillSegment(Transform parent, Transform props, bool commercial, int column, Vector3 start, Vector3 along, Vector3 facing, float length, System.Random random)
        {
            Kit kit = commercial ? Kit.CityKitCommercial : Kit.CityKitSuburban;
            float maxScale = commercial ? CityPieces.CityScale : CityPieces.SuburbScale;
            float depthLimit = commercial ? RowDepth : RowDepth - 1f;
            bool skyscrapers = commercial && (column == AvenueIndex - 1 || column == AvenueIndex);
            var chosen = new List<(string model, float scale, float width)>();
            float stretch = 1f;

            for (int attempt = 0; attempt < 60; attempt++)
            {
                chosen.Clear();
                float sum = 0f;

                for (int guard = 0; guard < 40 && sum < length - 3f; guard++)
                {
                    string model = PickModel(commercial, skyscrapers, random);
                    Bounds b = CityPieces.ModelBounds(kit, model);
                    float scale = Mathf.Min(maxScale, depthLimit / b.size.z);
                    float width = b.size.x * scale;

                    if (sum + width > length * 1.12f)
                    {
                        continue;
                    }

                    chosen.Add((model, scale, width));
                    sum += width;
                }

                stretch = sum > 0f ? length / sum : 1f;

                if (chosen.Count > 0 && stretch > 0.9f && stretch < 1.14f)
                {
                    break;
                }
            }

            // Orientação: no kit Commercial a frente é -Z do modelo; no Suburban é +Z.
            float yaw = commercial ? Mathf.Atan2(-facing.x, -facing.z) * Mathf.Rad2Deg : Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
            Vector3 inward = -facing;
            float cursor = 0f;

            foreach (var (model, scale, width) in chosen)
            {
                float w = width * stretch;
                Bounds b = CityPieces.ModelBounds(kit, model);
                float depth = b.size.z * scale;
                Vector3 position = start + along * (cursor + w * 0.5f) + inward * (depth * 0.5f);
                var stretched = new Vector3(scale * stretch, scale, scale);

                // Altura: um pouco de variação no kit comercial, sem deformar as janelas demais.
                if (commercial && model.StartsWith("building-skyscraper") == false)
                {
                    stretched.y *= 0.95f + 0.2f * (float)random.NextDouble();
                }

                CityPieces.Model(parent, kit, model, position, yaw, stretched);
                cursor += w;
            }

            // Árvores de calçada nas ruas residenciais, entre a frente das casas e o meio-fio.
            if (!commercial)
            {
                int trees = Mathf.Max(1, Mathf.RoundToInt(length / 22f));

                for (int t = 0; t < trees; t++)
                {
                    float at = length * (t + 0.5f + ((float)random.NextDouble() - 0.5f) * 0.4f) / trees;
                    Vector3 treePosition = start + along * at + facing * 1.6f;
                    CityPieces.Tree(props, "ArvoreRua", treePosition + Vector3.up * CurbHeight, 4.5f + 1.5f * (float)random.NextDouble(), false, false);
                }
            }
        }

        private static string PickModel(bool commercial, bool skyscrapers, System.Random random)
        {
            if (!commercial)
            {
                return HouseModels[random.Next(HouseModels.Length)];
            }

            if (skyscrapers && random.NextDouble() < 0.1)
            {
                return SkyscraperModels[random.Next(SkyscraperModels.Length)];
            }

            return CommercialModels[random.Next(CommercialModels.Length)];
        }

        /// <summary>
        /// Restaurante: prédio largo (building-k) recuado do meio-fio para caber o terraço, com toldos e guarda-sóis
        /// na frente e uma faixa colorida (a cor de cada restaurante) bem visível acima da porta.
        /// </summary>
        private static void BuildRestaurantFront(Transform parent, Transform props, int index, Vector3 slotCenterOnFrontLine, Vector3 facing, Materials m)
        {
            const string model = "building-k";
            const float terrace = 3f;
            Bounds b = CityPieces.ModelBounds(Kit.CityKitCommercial, model);
            float width = 24f;
            float scale = width / b.size.x;
            float depth = b.size.z * scale;
            Vector3 inward = -facing;
            Vector3 right = Vector3.Cross(Vector3.up, facing).normalized;
            Vector3 frontLine = slotCenterOnFrontLine + inward * terrace;
            float yaw = Mathf.Atan2(-facing.x, -facing.z) * Mathf.Rad2Deg;

            CityPieces.Model(parent, Kit.CityKitCommercial, model, frontLine + inward * (depth * 0.5f), yaw, scale).name = "Restaurante_" + index;

            // Faixa colorida do restaurante acima das portas.
            GameObject banner = CityPieces.Box(props, "FaixaRestaurante" + index, Vector3.zero, new Vector3(width * 0.9f, 1.6f, 0.4f), m.Restaurants[index % m.Restaurants.Length]);
            banner.transform.SetPositionAndRotation(frontLine + facing * 0.35f + Vector3.up * 5.2f, Quaternion.LookRotation(facing, Vector3.up));

            // Toldos (rotacionados 180 graus: o lado de fora do modelo aponta para a rua) e guarda-sóis no terraço.
            float awningScale = 8f;
            float awningYaw = yaw + 180f;

            for (int k = -1; k <= 1; k++)
            {
                Vector3 position = frontLine + right * (k * 6.6f) + facing * (-0.1f * awningScale) + Vector3.up * CurbHeight;
                CityPieces.Model(props, Kit.CityKitCommercial, "detail-awning-wide", position, awningYaw, awningScale).name = "Toldo";
            }

            for (int k = 0; k < 4; k++)
            {
                float side = (k - 1.5f) * 5.5f;
                Vector3 position = frontLine + right * side + facing * 2.6f + Vector3.up * CurbHeight;
                CityPieces.Model(props, Kit.CityKitCommercial, k % 2 == 0 ? "detail-parasol-a" : "detail-parasol-b", position, k * 70f, 5.5f).name = "GuardaSol";
            }
        }

        private static void BuildLargo(Transform blocks, Transform props, Materials m, float x0, float x1, float z0, float z1)
        {
            var center = new Vector3((x0 + x1) * 0.5f, 0f, (z0 + z1) * 0.5f);
            Pavement(blocks, "LargoPiso", x0, x1, z0, z1, 0.1f, m.Plaza);
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
                CityPieces.Tree(props, "LargoArvore" + k, new Vector3(center.x + spots[k].x, 0.1f, center.z + spots[k].y), 8f + (k % 3), true);
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

                // Letreiro: texto alto que olha para a câmera.
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

                GameObject hole = CityPieces.Cylinder(parent, "Buraco" + made, position + Vector3.up * 0.06f, 1.6f, 0.06f, m.Pothole);
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

        // ---------- Mobiliário urbano, carros parados e trânsito ----------

        private static readonly string[] ParkedCarModels = { "sedan", "hatchback-sports", "suv", "taxi", "van", "delivery", "truck", "sedan", "hatchback-sports" };

        /// <summary>Distância do centro da rua até o meio de um carro estacionado (rente ao meio-fio).</summary>
        private static float ParkOffset(float halfWidth) => halfWidth - 1.6f;

        /// <summary>Faixa de trânsito (distância do centro da rua): na avenida a pista externa.</summary>
        private static float LaneOffset(bool avenue) => avenue ? 8f : 2f;

        private static bool NearAny(Vector3 position, List<Vector3> points, float distance)
        {
            foreach (Vector3 other in points)
            {
                if (Vector3.Distance(position, other) < distance)
                {
                    return true;
                }
            }

            return false;
        }

        private static float YawFacing(Vector3 direction) => Mathf.Atan2(-direction.x, -direction.z) * Mathf.Rad2Deg;

        /// <summary>Poste com collider fino só na base (o braço do poste fica acima da moto).</summary>
        private static GameObject SolidPole(Transform parent, string model, Kit kit, Vector3 position, float yaw, float scale)
        {
            GameObject pole = CityPieces.Model(parent, kit, model, position, yaw, scale);
            var capsule = pole.AddComponent<CapsuleCollider>();
            capsule.radius = 0.22f / scale;
            capsule.height = 3f / scale;
            capsule.center = new Vector3(0f, 1.5f / scale, 0f);

            return pole;
        }

        private static void BuildProps(Transform parent, System.Random random, List<Vector3> avoid)
        {
            Transform lights = Group(parent, "Postes");
            Transform signs = Group(parent, "Placas");
            Transform cars = Group(parent, "CarrosParados");
            Transform dumpsters = Group(parent, "Cacambas");
            var parked = new List<Vector3>();

            // Postes de luz: um por trecho de rua, alternando o lado, com o braço sobre a rua.
            for (int i = 0; i < StreetCount; i++)
            {
                for (int j = 0; j < BlockCount; j++)
                {
                    float t = (i + j) % 2 == 0 ? 0.25f : 0.75f;
                    int side = (i + j) % 2 == 0 ? 1 : -1;
                    var position = new Vector3(_cx[i] + side * (_hwx[i] - 0.7f), 0.04f, Mathf.Lerp(_cz[j], _cz[j + 1], t));

                    if (NearAny(position, avoid, 9f))
                    {
                        continue;
                    }

                    SolidPole(lights, "light-square", Kit.CityKitRoads, position, YawFacing(new Vector3(-side, 0f, 0f)), 10f);
                }
            }

            for (int j = 0; j < StreetCount; j++)
            {
                for (int i = 0; i < BlockCount; i++)
                {
                    float t = (i + j) % 2 == 0 ? 0.75f : 0.25f;
                    int side = (i + j) % 2 == 0 ? -1 : 1;
                    var position = new Vector3(Mathf.Lerp(_cx[i], _cx[i + 1], t), 0.04f, _cz[j] + side * (_hwz[j] - 0.7f));

                    if (NearAny(position, avoid, 9f))
                    {
                        continue;
                    }

                    SolidPole(lights, "light-square", Kit.CityKitRoads, position, YawFacing(new Vector3(0f, 0f, -side)), 10f);
                }
            }

            // Semáforos nos cruzamentos da avenida (cantos opostos) e placas de PARE nas ruas pequenas.
            for (int j = 0; j < StreetCount; j++)
            {
                Vector3 center = new Vector3(_cx[AvenueIndex], 0.04f, _cz[j]);

                foreach (int sign in new[] { 1, -1 })
                {
                    var corner = center + new Vector3(sign * (AvenueHalfWidth - 0.8f), 0f, sign * (StreetHalfWidth - 0.8f));
                    SolidPole(signs, "traffic-light", Kit.CityKitRoads, corner, YawFacing(center - corner), 12f);
                }
            }

            for (int k = 0; k < 14; k++)
            {
                int i = random.Next(StreetCount);

                if (i == AvenueIndex || i == AvenueIndex - 1 || i == AvenueIndex + 1)
                {
                    continue;
                }

                int j = random.Next(StreetCount);
                Vector3 center = new Vector3(_cx[i], 0.04f, _cz[j]);
                var corner = center + new Vector3(StreetHalfWidth - 0.8f, 0f, StreetHalfWidth - 0.8f);
                SolidPole(signs, k % 3 == 0 ? "road-sign-street" : "road-sign-stop", Kit.CityKitRoads, corner, YawFacing(center - corner), 6f);
            }

            // Caçambas de lixo na calçada.
            for (int k = 0; k < 9; k++)
            {
                int i = random.Next(BlockCount);
                int j = random.Next(BlockCount);

                if (i == LargoBlockX && j == LargoBlockZ)
                {
                    continue;
                }

                float along = 0.2f + 0.6f * (float)random.NextDouble();
                bool west = random.Next(2) == 0;
                float x = west ? BlockX0(i) + 2.0f : BlockX1(i) - 2.0f;
                float z = Mathf.Lerp(BlockZ0(j), BlockZ1(j), along);
                var position = new Vector3(x, CurbHeight, z);

                if (NearAny(position, avoid, 14f))
                {
                    continue;
                }

                CityPieces.Model(dumpsters, Kit.CityKitRoads, "dumpster", position, west ? 0f : 180f, 6f, true);
            }

            // Carros estacionados, rente ao meio-fio, no sentido do trânsito (mão direita).
            for (int i = 0; i < StreetCount; i++)
            {
                for (int j = 0; j < BlockCount; j++)
                {
                    ParkAlong(cars, random, avoid, parked, true, i, j);
                }
            }

            for (int j = 0; j < StreetCount; j++)
            {
                for (int i = 0; i < BlockCount; i++)
                {
                    ParkAlong(cars, random, avoid, parked, false, j, i);
                }
            }
        }

        private static void ParkAlong(Transform parent, System.Random random, List<Vector3> avoid, List<Vector3> parked, bool vertical, int street, int segment)
        {
            int count = random.Next(0, 3);
            float halfWidth = vertical ? _hwx[street] : _hwz[street];

            for (int n = 0; n < count; n++)
            {
                float t = 0.2f + 0.6f * (float)random.NextDouble();
                int side = random.Next(2) == 0 ? 1 : -1;
                string model = ParkedCarModels[random.Next(ParkedCarModels.Length)];
                Vector3 position;
                float yaw;

                if (vertical)
                {
                    position = new Vector3(_cx[street] + side * ParkOffset(halfWidth), 0.02f, Mathf.Lerp(_cz[segment], _cz[segment + 1], t));
                    yaw = side > 0 ? 0f : 180f;
                }
                else
                {
                    position = new Vector3(Mathf.Lerp(_cx[segment], _cx[segment + 1], t), 0.02f, _cz[street] + side * ParkOffset(halfWidth));
                    yaw = side < 0 ? 90f : 270f;
                }

                if (NearAny(position, avoid, 16f) || NearAny(position, parked, 8f))
                {
                    continue;
                }

                parked.Add(position);
                CityPieces.Car(parent, model, position, yaw + ((float)random.NextDouble() - 0.5f) * 4f);
            }
        }

        /// <summary>Oito carros do kit dando voltas em quarteirões (sentido horário), a 7 a 9 m/s, cinemáticos.</summary>
        private static void BuildTraffic(Transform parent, Transform player)
        {
            var blocks = new[]
            {
                new Vector2Int(3, 3), new Vector2Int(4, 3), new Vector2Int(3, 4), new Vector2Int(4, 4),
                new Vector2Int(3, 5), new Vector2Int(4, 5), new Vector2Int(2, 4), new Vector2Int(5, 4),
            };
            string[] models = { "taxi", "sedan", "van", "suv", "hatchback-sports", "delivery", "sedan", "truck" };
            float[] speeds = { 8f, 7f, 9f, 8f, 7.5f, 8.5f, 7f, 9f };
            float[] starts = { 0.1f, 0.6f, 0.35f, 0.85f, 0.2f, 0.7f, 0.5f, 0.05f };

            for (int k = 0; k < blocks.Length; k++)
            {
                int i = blocks[k].x;
                int j = blocks[k].y;
                float west = LaneOffset(i == AvenueIndex);
                float east = LaneOffset(i + 1 == AvenueIndex);
                var corners = new[]
                {
                    new Vector3(_cx[i] + west, 0.02f, _cz[j] + 2f),
                    new Vector3(_cx[i] + west, 0.02f, _cz[j + 1] - 2f),
                    new Vector3(_cx[i + 1] - east, 0.02f, _cz[j + 1] - 2f),
                    new Vector3(_cx[i + 1] - east, 0.02f, _cz[j] + 2f),
                };

                // Posição inicial: fração do perímetro.
                float perimeter = 0f;

                for (int c = 0; c < 4; c++)
                {
                    perimeter += Vector3.Distance(corners[c], corners[(c + 1) % 4]);
                }

                float distance = starts[k] * perimeter;
                int segment = 0;

                while (distance > Vector3.Distance(corners[segment], corners[(segment + 1) % 4]))
                {
                    distance -= Vector3.Distance(corners[segment], corners[(segment + 1) % 4]);
                    segment++;
                }

                Vector3 a = corners[segment];
                Vector3 b = corners[(segment + 1) % 4];
                Vector3 direction = (b - a).normalized;
                Vector3 start = a + direction * distance;
                float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;

                var car = new GameObject("Transito_" + models[k] + "_" + k);
                car.transform.SetParent(parent, false);
                car.transform.SetPositionAndRotation(start, Quaternion.Euler(0f, yaw, 0f));
                Bounds bounds = CityPieces.ModelBounds(Kit.CarKit, models[k]);
                var box = car.AddComponent<BoxCollider>();
                box.center = bounds.center * CityPieces.CarScale;
                box.size = bounds.size * CityPieces.CarScale;
                car.AddComponent<Rigidbody>().isKinematic = true;
                CityPieces.Model(car.transform, Kit.CarKit, models[k], start, yaw, CityPieces.CarScale).name = "Carro";
                car.AddComponent<TrafficLoop>().Configure(corners, speeds[k], player, segment);
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
            camera.cullingMask = ~(1 << 2);
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
