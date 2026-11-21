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

        [UnityTest]
        public IEnumerator CaptureScreens()
        {
            string dir = Dir;
            Camera main = Camera.main;
            Assert.IsNotNull(main);
            var follow = main.GetComponent<FollowCamera>();

            // (c) Painel de início do dia.
            follow.Snap();
            yield return null;
            Save(dir, "c_inicio_do_dia.png", main);

            // (d) Oferta de pedido.
            Flow.NewWeek(123);
            Flow.BeginShift();
            yield return null;
            Save(dir, "d_oferta_pedido.png", main);

            // (a) Direção em terceira pessoa, com HUD e linha do GPS.
            Flow.AcceptOffer();
            yield return null;
            Flow.Route.Recalculate();
            var route = Flow.Route.Route;
            Vector3 start = route[1];
            Vector3 next = route.Count > 2 ? route[2] : route[route.Count - 1];
            Vector3 dir2 = next - start;
            float yaw = Mathf.Atan2(dir2.x, dir2.z) * Mathf.Rad2Deg;
            Flow.Bike.Teleport(new Vector3(start.x, 0.1f, start.z), yaw);
            follow.Snap();
            Flow.Bike.SetLocked(true);
            yield return null;
            yield return null;
            Flow.Route.Recalculate();
            Save(dir, "a_direcao_terceira_pessoa.png", main);

            // (b) Visão de cima do mapa todo, com a rota até o cliente.
            Flow.Bike.SetLocked(false);
            Vector3 restaurant = Flow.CurrentPlan.Restaurant.Position;
            Flow.Bike.Teleport(new Vector3(restaurant.x, 0.1f, restaurant.z), 0f);
            yield return new WaitForSeconds(0.2f);
            Flow.Route.Recalculate();
            Flow.Map.Highlight(Flow.CurrentPlan.Customer);
            Flow.Ui.SetHudVisible(false);

            var lineRenderer = Flow.Route.GetComponent<LineRenderer>();
            lineRenderer.widthMultiplier = 7f;

            var overview = new GameObject("CameraVisaoGeral").AddComponent<Camera>();
            overview.orthographic = true;
            overview.orthographicSize = 540f;
            overview.clearFlags = CameraClearFlags.SolidColor;
            overview.backgroundColor = new Color(0.36f, 0.5f, 0.3f);
            overview.nearClipPlane = 1f;
            overview.farClipPlane = 1000f;
            overview.transform.position = new Vector3(0f, 500f, 0f);
            overview.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            bool fog = RenderSettings.fog;
            RenderSettings.fog = false;
            main.enabled = false;
            yield return null;
            Save(dir, "b_mapa_visto_de_cima.png", overview, false);
            RenderSettings.fog = fog;
            main.enabled = true;
            lineRenderer.widthMultiplier = 1.2f;
            Object.Destroy(overview.gameObject);
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
