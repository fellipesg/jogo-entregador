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
    /// Gera do zero a cena Assets/_Jogo/Scenes/SantoAmaro.unity a partir do extrato do OpenStreetMap (Assets/_Jogo/Data/OSM): ruas,
    /// quarteirões, prédios procedurais, moto, câmeras, grafo de ruas, restaurantes, endereços, buracos, trânsito e o GameRoot do jogo.
    /// Uso: menu Jogo/Gerar cidade, ou -executeMethod Entregador.Editor.CityBuilder.BuildFromCommandLine.
    /// Dados do mapa © OpenStreetMap contributors (ODbL).
    /// </summary>
    public static partial class CityBuilder
    {
        public const string ScenePath = "Assets/_Jogo/Scenes/SantoAmaro.unity";

        private static Transform _roadsRoot;

        [MenuItem("Jogo/Gerar cidade (Santo Amaro)")]
        public static void Build()
        {
            RenderSetup.Apply();
            TmpSetup.EnsureFallbackFont();
            OsmMap map = OsmMap.Load(OsmMap.DataPath);

            Directory.CreateDirectory("Assets/_Jogo/Scenes");
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            MeshStore.Begin();
            ResetState();
            Materials m = new Materials();
            var world = new GameObject("Cidade");
            _roadsRoot = Group(world.transform, "Ruas");
            Transform blocks = Group(world.transform, "Quarteiroes");
            Transform buildings = Group(world.transform, "Predios");
            Transform props = Group(world.transform, "Detalhes");
            Transform markers = Group(world.transform, "Marcadores");
            Transform potholes = Group(world.transform, "Buracos");
            _signsRoot = Group(buildings, "Letreiros");
            _detailsRoot = props;

            // Ruas do OSM -> grade -> quarteirões.
            CollectRoads(map);
            CapParallelRoads();
            IndexNodes();
            _raster = new CityRaster(map.HalfX, map.HalfZ);
            PaintRoads(_raster, map);
            ComputeBlocks();
            PaintZones(map);
            BuildGround(world.transform, map);
            _graph = BuildGraph(world);
            _trafficLoops = FindTrafficLoops();

            // Pavimento: asfalto, faixas e travessias, calçadas e pisos.
            BuildRoadSurfaces(map);
            BuildMarkings(map);
            BuildSlabs();
            BuildZoneSurfaces();

            // Prédios: pegadas do OSM, restaurantes, frente dos quarteirões e fundos.
            PlaceOsmBuildings(map);
            ChooseRestaurantSites(_graph);
            Place[] restaurants = BuildRestaurants(markers, m);
            Place[] customers = BuildCustomers(markers, m, restaurants);
            FillFrontage();
            FillInterior();
            BuildLots();
            BuildRestaurantBuildings(m);
            BuildColliders(blocks);
            DumpRaster();

            List<Vector3> potholePositions = BuildPotholes(potholes, new System.Random(99), restaurants, customers);
            Vector3 spawn = ChooseSpawn(out float spawnYaw);

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

            BuildProps(props, new System.Random(55), avoid, map);
            FinishBatches(blocks);

            var cityMap = world.AddComponent<CityMap>();
            cityMap.Configure(restaurants, customers, _graph, spawn, spawnYaw, Vector2.zero, 2f * PlayableHalf);
            cityMap.SetViewPoints(BuildViewPoints(map));
            cityMap.SetBusLanes(BusLanes.ToArray());

            ArcadeBikeController bike = BikeFactory.Build(spawn, spawnYaw);
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
            RouteGuide route = BuildRoute(_graph, bike);

            var root = new GameObject("GameRoot");
            var tuning = root.AddComponent<GameTuning>();
            root.AddComponent<FakeAdService>();
            var ui = new GameObject("Interface").AddComponent<GameUI>();
            ui.transform.SetParent(root.transform, false);
            var flow = root.AddComponent<GameFlow>();
            flow.Configure(cityMap, bike, bike.GetComponent<BikeInput>(), tracker, route, main.GetComponent<FollowCamera>(), minimap, ui, tuning, sun, main);
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

        private static void ResetState()
        {
            GroundRenderers.Clear();
            GroundBatches.Clear();
            BuildBatches.Clear();
            Blocks.Clear();
            BlocksByLabel.Clear();
            Roads.Clear();
            Junctions.Clear();
            NodeUsers.Clear();
            GraphEdges.Clear();
            RestaurantSites.Clear();
            PendingLots.Clear();
            Poles.Clear();
            BumpPositions.Clear();
            _borrachariaDone = false;
            _footprintMaterial = null;
            LoopSegments.Clear();
            BorderViews.Clear();
            LargoSolids.Clear();
            _avenueAnchorSet = false;
            _fountain = LargoCenter;
            System.Array.Clear(KindStats, 0, KindStats.Length);
            System.Array.Clear(KindCount, 0, KindCount.Length);
        }

        /// <summary>Com JOGO_DEBUG_DIR definido, grava um PNG da grade (ruas por classe, quarteirões, zonas e ocupação) para conferir o layout.</summary>
        private static void DumpRaster()
        {
            string dir = Environment.GetEnvironmentVariable("JOGO_DEBUG_DIR");

            if (string.IsNullOrEmpty(dir))
            {
                return;
            }

            CityRaster r = _raster;
            const int step = 2;
            int w = r.W / step;
            int h = r.H / step;
            var texture = new Texture2D(w, h, TextureFormat.RGB24, false);
            Color[] roadColors =
            {
                new Color(0.55f, 0.75f, 0.45f), new Color(0.5f, 0.5f, 0.55f), new Color(0.2f, 0.7f, 0.8f), new Color(0.15f, 0.15f, 0.2f),
                new Color(0.9f, 0.6f, 0.1f), new Color(0.85f, 0.1f, 0.1f),
            };

            for (int iz = 0; iz < h; iz++)
            {
                for (int ix = 0; ix < w; ix++)
                {
                    int i = iz * step * r.W + ix * step;
                    Color c;

                    if (r.Block[i] == 0)
                    {
                        c = roadColors[r.Road[i]];
                    }
                    else if (r.Occ[i] == 2)
                    {
                        c = new Color(0.2f, 0.2f, 0.9f);
                    }
                    else if (r.Occ[i] == 1)
                    {
                        c = new Color(0.45f, 0.35f, 0.3f);
                    }
                    else
                    {
                        c = r.Zone[i] == ZoneLargo ? new Color(0.95f, 0.85f, 0.5f) : r.Zone[i] != 0 ? new Color(0.3f, 0.6f, 0.3f) : new Color(0.8f, 0.8f, 0.78f);
                    }

                    texture.SetPixel(ix, iz, c);
                }
            }

            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "grade.png"), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }

        // ---------- Chão ----------

        private static void BuildGround(Transform world, OsmMap map)
        {
            float half = Mathf.Max(map.HalfX, map.HalfZ) + 400f;

            // O collider do chão fica no y = 0; o visual (grama) um pouco abaixo, para as ruas aparecerem por cima sem brigar de profundidade.
            var ground = new GameObject("Chao");
            ground.transform.SetParent(world, false);
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.AddComponent<BoxCollider>().size = new Vector3(half * 2f, 1f, half * 2f);

            var visual = new MeshBatch();
            visual.FlatQuad(CityStyle.Lawn(), new Vector3(-half, -0.04f, -half), new Vector3(half, -0.04f, -half), new Vector3(half, -0.04f, half), new Vector3(-half, -0.04f, half));
            visual.Build(world, "ChaoVisual", false);
        }

        private sealed class Materials
        {
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
            camera.cullingMask = ~((1 << 2) | (1 << BuildingLayer));
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
    }
}
