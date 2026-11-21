using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Entregador.Game.Tests
{
    /// <summary>
    /// Gera imagens da cena para revisão. Só grava quando a variável de ambiente JOGO_SHOTS_DIR existe.
    /// Rodar sem -nographics (precisa de GPU).
    /// </summary>
    public sealed class ScreenshotTests : SceneFixture
    {
        private const int Width = 1600;
        private const int Height = 900;

        private static string Dir
        {
            get
            {
                string dir = System.Environment.GetEnvironmentVariable("JOGO_SHOTS_DIR");

                if (string.IsNullOrEmpty(dir))
                {
                    Assert.Ignore("JOGO_SHOTS_DIR não definido.");
                }

                Directory.CreateDirectory(dir);

                return dir;
            }
        }

        /// <summary>Começa uma semana cujo dia 1 é seco (as capturas de dia seco precisam de céu azul).</summary>
        private void StartDryWeek()
        {
            for (int seed = 1; seed < 60; seed++)
            {
                Flow.NewWeek(seed);

                if (!Flow.Session.IsRainingToday)
                {
                    return;
                }
            }

            Assert.Fail("Nenhuma semana com o dia 1 seco.");
        }

        /// <summary>Procura, ao longo da rua vertical street, um ponto da pista sem nada sólido por perto (nem na moto nem na câmera).</summary>
        private Vector3 ClearStreetSpot(int street, float laneOffset)
        {
            for (int j = 0; j < 9; j++)
            {
                for (float t = 0.3f; t <= 0.71f; t += 0.1f)
                {
                    Vector3 p = Vector3.Lerp(Node(street, j), Node(street, j + 1), t) + new Vector3(laneOffset, 0f, 0f);
                    bool clear = !Physics.CheckSphere(p + Vector3.up * 1.3f, 1.2f);

                    for (float back = 3f; back <= 9f && clear; back += 3f)
                    {
                        clear &= !Physics.CheckSphere(p + new Vector3(0f, 1.3f, -back), 1.2f);
                    }

                    if (clear)
                    {
                        return p;
                    }
                }
            }

            return Vector3.Lerp(Node(street, 1), Node(street, 2), 0.5f) + new Vector3(laneOffset, 0f, 0f);
        }

        private Vector3 Node(int i, int j) => Flow.Map.Graph.NodePosition(i * 10 + j);

        /// <summary>Põe a moto na posição, virada para o primeiro trecho da rota do GPS, com a câmera atrás.</summary>
        private IEnumerator PlaceOnRoute(Vector3 position, float fallbackYaw)
        {
            Flow.Bike.SetLocked(true);
            Flow.Bike.Teleport(new Vector3(position.x, 0.1f, position.z), fallbackYaw);
            yield return null;
            Flow.Route.Recalculate();
            var route = Flow.Route.Route;
            float yaw = fallbackYaw;

            // Aponta para o primeiro ponto da rota a mais de 6 m (o começo é só o ajuste para o centro da rua).
            for (int i = 1; i < route.Count; i++)
            {
                Vector3 d = route[i] - route[0];

                if (d.magnitude > 6f)
                {
                    yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;

                    break;
                }
            }

            Flow.Bike.Teleport(new Vector3(position.x, 0.1f, position.z), yaw);
            Camera.main.GetComponent<FollowCamera>().Snap();
            yield return null;
            yield return null;
            Flow.Route.Recalculate();
        }

        private IEnumerator PlaceFacing(Vector3 position, Vector3 lookAt)
        {
            Vector3 d = lookAt - position;
            Flow.Bike.SetLocked(true);
            Flow.Bike.Teleport(new Vector3(position.x, 0.1f, position.z), Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg);
            Camera.main.GetComponent<FollowCamera>().Snap();
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator CaptureScreens()
        {
            string dir = Dir;
            Camera main = Camera.main;
            Assert.IsNotNull(main);
            var follow = main.GetComponent<FollowCamera>();

            // Painel de início do dia (texto em TextMeshPro, com acentos).
            follow.Snap();
            yield return null;
            Save(dir, "f_inicio_do_dia.png", main);

            // Oferta de pedido (dia seco).
            StartDryWeek();
            Flow.BeginShift();
            yield return null;
            Save(dir, "g_oferta_pedido.png", main);

            // (a) Rua residencial em terceira pessoa, com HUD e a fita do GPS.
            Flow.AcceptOffer();
            yield return null;
            Vector3 residential = ClearStreetSpot(1, 2f);
            yield return PlaceOnRoute(residential, 0f);
            Debug.Log($"[Captura] residencial em {residential} yaw {Flow.Bike.Yaw}");
            Save(dir, "a_rua_residencial_gps.png", main);
            ReportStats(dir, "a_rua_residencial_gps", main);

            // (f) Rua com postes e fios: câmera baixa olhando um pouco para cima, ao longo da rua.
            follow.enabled = false;
            Vector3 poleStreet = ClearStreetSpot(6, 2f);
            Flow.Bike.Teleport(new Vector3(poleStreet.x, 0.1f, poleStreet.z), 0f);
            yield return null;
            main.transform.position = poleStreet + new Vector3(-1f, 1.9f, -4f);
            main.transform.rotation = Quaternion.Euler(-9f, 0f, 0f);
            yield return null;
            Save(dir, "f_postes_e_fios.png", main);
            follow.enabled = true;

            // (d) Close-up da Borracharia 24h.
            GameObject borracharia = GameObject.Find("Borracharia");
            Assert.IsNotNull(borracharia, "A Borracharia 24h não foi gerada.");
            follow.enabled = false;
            Vector3 shop = borracharia.transform.position;
            Vector3 shopForward = borracharia.transform.forward;
            Vector3 shopRight = Vector3.Cross(Vector3.up, shopForward);
            Flow.Bike.Teleport(new Vector3(shop.x, 0.1f, shop.z) + shopForward * 8f, 0f);
            yield return null;
            main.transform.position = shop + shopForward * 6.5f - shopRight * 4.5f + Vector3.up * 0.8f;
            main.transform.rotation = Quaternion.LookRotation(shop - main.transform.position + Vector3.up * 0.6f, Vector3.up);
            yield return null;
            Save(dir, "d_borracharia.png", main);
            follow.enabled = true;

            // (b) O largo com os restaurantes (visto da moto, dentro da praça, olhando para dois deles).
            Place[] restaurants = Flow.Map.Restaurants;
            yield return PlaceFacing(Flow.Map.BikeSpawn, restaurants[0].Position);
            Save(dir, "b_largo_restaurante_1.png", main);
            yield return PlaceFacing(Flow.Map.BikeSpawn + new Vector3(-30f, 0f, 20f), restaurants[1].Position);
            Save(dir, "b_largo_restaurante_2.png", main);
            yield return PlaceFacing(restaurants[2].Position + new Vector3(-20f, 0f, 0f), restaurants[2].Position);
            Save(dir, "b_largo_restaurante_3.png", main);

            // (c) Avenida com trânsito: pista externa da avenida, virada para o norte.
            Vector3 avenue = Node(4, 3) + new Vector3(8f, 0f, 30f);
            TrafficLoop lead = null;

            for (int attempt = 0; attempt < 30 && lead == null; attempt++)
            {
                yield return new WaitForSeconds(0.5f);

                foreach (TrafficLoop car in Object.FindObjectsByType<TrafficLoop>(FindObjectsSortMode.None))
                {
                    Vector3 p = car.transform.position;
                    float yaw = Mathf.Repeat(car.transform.eulerAngles.y + 180f, 360f) - 180f;
                    bool alongAvenue = Mathf.Abs(yaw) < 8f || Mathf.Abs(Mathf.Abs(yaw) - 180f) < 8f;
                    bool onAvenue = alongAvenue && Mathf.Abs(p.x - Node(4, 0).x) < 12f && p.z > Node(4, 1).z + 40f && p.z < Node(4, 8).z - 40f && car.DistanceToNextWaypoint > 50f;

                    if (onAvenue)
                    {
                        lead = car;

                        break;
                    }
                }
            }

            Assert.IsNotNull(lead, "Nenhum carro na avenida.");
            avenue = lead.transform.position - lead.transform.forward * 24f;
            Flow.Bike.SetLocked(true);
            Flow.Bike.Teleport(new Vector3(avenue.x, 0.1f, avenue.z), lead.transform.eulerAngles.y);
            follow.Snap();
            yield return new WaitForSeconds(0.3f);
            Flow.Bike.Teleport(new Vector3(avenue.x + 0f, 0.1f, avenue.z) , lead.transform.eulerAngles.y);
            follow.Snap();
            yield return null;
            Save(dir, "c_avenida_transito.png", main);
            ReportStats(dir, "c_avenida_transito", main);

            // (d) Visão de cima do mapa todo, com a rota até o cliente.
            Flow.Bike.SetLocked(false);
            Vector3 restaurant = Flow.CurrentPlan.Restaurant.Position;
            Flow.Bike.Teleport(new Vector3(restaurant.x, 0.1f, restaurant.z), 0f);
            yield return new WaitForSeconds(0.2f);
            Flow.Route.Recalculate();
            Flow.Map.Highlight(Flow.CurrentPlan.Customer);
            Flow.Ui.SetHudVisible(false);

            var lineRenderer = Flow.Route.GetComponent<LineRenderer>();
            lineRenderer.widthMultiplier = 9f;
            Texture chevrons = lineRenderer.sharedMaterial.GetTexture("_BaseMap");
            lineRenderer.sharedMaterial.SetTexture("_BaseMap", null);

            var overview = new GameObject("CameraVisaoGeral").AddComponent<Camera>();
            overview.orthographic = true;
            overview.orthographicSize = 720f;
            overview.clearFlags = CameraClearFlags.SolidColor;
            overview.backgroundColor = new Color(0.36f, 0.5f, 0.3f);
            overview.nearClipPlane = 1f;
            overview.farClipPlane = 1100f;
            overview.transform.position = new Vector3(0f, 500f, 0f);
            overview.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            bool fog = RenderSettings.fog;
            RenderSettings.fog = false;
            main.enabled = false;
            yield return null;
            Save(dir, "g_mapa_visto_de_cima.png", overview, false);
            RenderSettings.fog = fog;
            main.enabled = true;
            lineRenderer.widthMultiplier = 1f;
            lineRenderer.sharedMaterial.SetTexture("_BaseMap", chevrons);
            Object.Destroy(overview.gameObject);
            Flow.Ui.SetHudVisible(true);

            // (e) Dia de chuva: avança os dias de folga até chover.
            Flow.NewWeek(123);

            for (int day = 1; day <= 7 && !Flow.Session.IsRainingToday; day++)
            {
                Flow.Ui.ClickModalButton("Folgar");
            }

            Assert.IsTrue(Flow.Session.IsRainingToday, "A semana deveria ter um dia de chuva.");
            Flow.Ui.ClickModalButton("Começar turno");
            Flow.AcceptOffer();
            yield return null;
            yield return PlaceOnRoute(residential, 0f);
            yield return new WaitForSeconds(1.5f);
            Save(dir, "e_dia_de_chuva.png", main);

            // Rua da avenida na chuva, para ver a chuva sobre os prédios.
            yield return PlaceFacing(Node(4, 3) + new Vector3(8f, 0f, 30f), Node(4, 3) + new Vector3(8f, 0f, 80f));
            yield return new WaitForSeconds(0.5f);
            Save(dir, "e_chuva_avenida.png", main);
        }

        private static void Look(Camera camera, Vector3 position, Vector3 target)
        {
            camera.transform.position = position;
            camera.transform.rotation = Quaternion.LookRotation(target - position, Vector3.up);
        }

        /// <summary>Closes da moto e do piloto: 3/4 frontal parada (pé no chão), lateral, curva inclinada, cinco personagens e os dois modelos.</summary>
        [UnityTest]
        public IEnumerator CaptureMoto()
        {
            string dir = Dir;
            Camera main = Camera.main;
            var follow = main.GetComponent<FollowCamera>();
            StartDryWeek();
            Flow.Ui.SetHudVisible(false);
            Vector3 spot = ClearStreetSpot(1, 2f);
            Flow.Bike.SetLocked(true);
            Flow.Bike.Teleport(new Vector3(spot.x, 0.1f, spot.z), 0f);
            follow.Snap();
            yield return new WaitForSeconds(0.8f);
            MotoVisual visual = Flow.Bike.GetComponentInChildren<MotoVisual>();
            Assert.IsNotNull(visual, "A moto não tem MotoVisual.");
            File.AppendAllText(Path.Combine(dir, "stats.txt"), $"moto+piloto triangulos={visual.Triangulos}\n");

            // Vista normal de trás, parada.
            Save(dir, "m0_tras_normal.png", main, false);
            follow.enabled = false;
            Vector3 p = Flow.Bike.transform.position;
            Vector3 c = p + Vector3.up * 0.85f;

            Look(main, p + new Vector3(2.2f, 1.5f, 2.4f), c);
            main.fieldOfView = 40f;
            yield return null;
            Save(dir, "m1_tres_quartos_frontal_titan.png", main, false);

            Look(main, p + new Vector3(3.6f, 1.05f, 0f), c);
            yield return null;
            Save(dir, "m2_lateral_titan.png", main, false);

            Look(main, p + new Vector3(-2.4f, 1.7f, -3f), c);
            yield return null;
            Save(dir, "m3_tras_3_4_titan.png", main, false);

            // Twister (troca em jogo).
            visual.Configurar(MotoModelo.Twister, MotoCor.Azul, Personagem.Camila);
            yield return null;
            Look(main, p + new Vector3(2.2f, 1.5f, 2.4f), c);
            yield return null;
            Save(dir, "m4_tres_quartos_frontal_twister.png", main, false);
            Look(main, p + new Vector3(3.6f, 1.05f, 0f), c);
            yield return null;
            Save(dir, "m5_lateral_twister.png", main, false);
            visual.Configurar(MotoModelo.Titan, MotoCor.Vermelha, Personagem.Jefinho);

            // Curva: acelera e esterça, câmera ao lado e um pouco atrás.
            Flow.Bike.SetLocked(false);
            Flow.Bike.GetComponent<BikeInput>().SetVirtual(0f, 1f, 0f);
            yield return new WaitForSeconds(1.6f);
            Flow.Bike.GetComponent<BikeInput>().SetVirtual(1f, 1f, 0f);
            yield return new WaitForSeconds(0.9f);
            Transform bt = Flow.Bike.transform;
            Look(main, bt.position + bt.forward * 3.2f + bt.right * 2.8f + Vector3.up * 1.3f, bt.position + Vector3.up * 0.9f);
            yield return null;
            Save(dir, "m6_curva_inclinada.png", main, false);
            Look(main, bt.position - bt.forward * 3.4f + bt.right * 1.2f + Vector3.up * 1.6f, bt.position + Vector3.up * 0.9f);
            yield return null;
            Save(dir, "m6b_curva_tras.png", main, false);
            Flow.Bike.GetComponent<BikeInput>().SetVirtual(0f, 0f, 1f);
            yield return new WaitForSeconds(0.25f);
            Look(main, bt.position - bt.forward * 3.0f + bt.right * 1.5f + Vector3.up * 1.4f, bt.position + Vector3.up * 0.9f);
            yield return null;
            Save(dir, "m6c_freando.png", main, false);
            Flow.Bike.GetComponent<BikeInput>().ClearAll();
            Flow.Bike.SetLocked(true);
            yield return new WaitForSeconds(1f);

            // Cinco personagens lado a lado (Titã) e dois Tornados atrás.
            Vector3 origin = Flow.Map.BikeSpawn + new Vector3(0f, 0f, 4f);
            float groundY = Flow.Bike.transform.position.y;
            Personagem[] all = { Personagem.Jefinho, Personagem.Robsao, Personagem.Marcao, Personagem.SeuValdir, Personagem.Camila };
            MotoCor[] cores = { MotoCor.Vermelha, MotoCor.Preta, MotoCor.Azul, MotoCor.Branca, MotoCor.Vermelha };

            for (int i = 0; i < 5; i++)
            {
                Vector3 pos = new Vector3(origin.x, groundY, origin.z + (i - 2) * 1.5f);
                MotoVisual.CriarPrevia(visual, pos, 270f, MotoModelo.Titan, cores[i], all[i]);
                MotoVisual.CriarPrevia(visual, pos + new Vector3(0f, 0f, 12f), 270f, MotoModelo.Twister, cores[(i + 1) % 5], all[(i + 2) % 5]);
            }

            yield return null;
            yield return null;
            Look(main, origin + new Vector3(-5.4f, 1.6f, 0f), origin + new Vector3(1f, 0.85f, 0f));
            main.fieldOfView = 55f;
            yield return null;
            Save(dir, "m7_cinco_personagens.png", main, false);
            Look(main, origin + new Vector3(-5.2f, 1.6f, 12f), origin + new Vector3(1f, 0.85f, 12f));
            yield return null;
            Save(dir, "m8_cinco_personagens_twister.png", main, false);
            Look(main, origin + new Vector3(-2.2f, 1.2f, 4.2f), origin + new Vector3(0f, 0.95f, 1.5f));
            yield return null;
            Save(dir, "m9_personagens_proximo.png", main, false);
            follow.enabled = true;
        }

        /// <summary>Grava batches, SetPass e triângulos do último quadro renderizado (UnityStats, só no Editor).</summary>
        private static void ReportStats(string dir, string label, Camera camera)
        {
#if UNITY_EDITOR
            camera.Render();
            string line = $"{label}: drawCalls={UnityEditor.UnityStats.drawCalls} setPass={UnityEditor.UnityStats.setPassCalls} tris={UnityEditor.UnityStats.triangles} verts={UnityEditor.UnityStats.vertices} shadowCasters={UnityEditor.UnityStats.shadowCasters}";
            Debug.Log("[Stats] " + line);
            File.AppendAllText(Path.Combine(dir, "stats.txt"), line + "\n");
#endif
        }

        private static void Save(string dir, string name, Camera camera, bool withUi = true)
        {
            Canvas canvas = Object.FindFirstObjectByType<Canvas>();
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            RenderTexture previous = camera.targetTexture;

            RenderMode oldMode = RenderMode.ScreenSpaceOverlay;

            if (withUi && canvas != null)
            {
                oldMode = canvas.renderMode;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1f;
                Canvas.ForceUpdateCanvases();
            }

            camera.targetTexture = rt;
            camera.Render();

            RenderTexture.active = rt;
            var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            texture.Apply();
            RenderTexture.active = null;

            File.WriteAllBytes(Path.Combine(dir, name), texture.EncodeToPNG());

            camera.targetTexture = previous;

            if (withUi && canvas != null)
            {
                canvas.renderMode = oldMode;
            }

            Object.Destroy(rt);
            Object.Destroy(texture);
        }
    }
}
