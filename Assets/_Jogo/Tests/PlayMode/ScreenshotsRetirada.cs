using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Entregador.Core;

namespace Entregador.Game.Tests
{
    public sealed partial class ScreenshotTests
    {
        /// <summary>Como Save, mas desenha todos os Canvas de sobreposição (inclusive os do balcão e da navegação, que são próprios).</summary>
        private static void SaveAll(string dir, string name, Camera camera)
        {
            Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            var modes = new RenderMode[canvases.Length];

            for (int i = 0; i < canvases.Length; i++)
            {
                modes[i] = canvases[i].renderMode;

                if (modes[i] == RenderMode.ScreenSpaceOverlay)
                {
                    canvases[i].renderMode = RenderMode.ScreenSpaceCamera;
                    canvases[i].worldCamera = camera;
                    canvases[i].planeDistance = 1f;
                }
            }

            Canvas.ForceUpdateCanvases();
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            RenderTexture previous = camera.targetTexture;
            camera.targetTexture = rt;
            camera.Render();
            RenderTexture.active = rt;
            var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            texture.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(dir, name), texture.EncodeToPNG());
            camera.targetTexture = previous;

            for (int i = 0; i < canvases.Length; i++)
            {
                canvases[i].renderMode = modes[i];
            }

            Object.Destroy(rt);
            Object.Destroy(texture);
        }

        /// <summary>A atendente no balcão do restaurante, o botão Retirar pedido e o diálogo (código do pedido) abertos.</summary>
        [UnityTest]
        public IEnumerator CaptureRetirada()
        {
            string dir = Dir;
            Camera main = Camera.main;
            var follow = main.GetComponent<FollowCamera>();
            StartDryWeek();
            Flow.BeginShift();
            Flow.AcceptOffer();
            yield return null;
            Place restaurant = Flow.CurrentPlan.Restaurant;
            Assert.IsTrue(restaurant.TemBalcao);

            Vector3 counterForward = Quaternion.Euler(0f, restaurant.BalcaoYaw, 0f) * Vector3.forward;
            Vector3 npcAt = restaurant.Balcao;

            // A atendente, de perto e de frente, da rua.
            follow.enabled = false;
            Look(main, npcAt + counterForward * 4.2f + Vector3.up * 1.55f + Vector3.Cross(Vector3.up, counterForward) * 0.8f, npcAt + Vector3.up * 1.1f);
            main.fieldOfView = 45f;
            yield return new WaitForSeconds(0.6f);
            Save(Dir, "t1_atendente_perto.png", main, false);

            // A fachada com o balcão, de mais longe.
            Look(main, npcAt + counterForward * 11f + Vector3.up * 2.6f + Vector3.Cross(Vector3.up, counterForward) * 3f, npcAt + Vector3.up * 1.4f);
            main.fieldOfView = 55f;
            yield return null;
            Save(dir, "t2_balcao_fachada.png", main, false);

            // Moto parada na zona: aparece o botão Retirar pedido.
            follow.enabled = true;
            main.fieldOfView = 62f;
            Vector3 toCounter = npcAt - restaurant.Position;
            float yaw = Mathf.Atan2(toCounter.x, toCounter.z) * Mathf.Rad2Deg;
            Flow.SituacaoForcada = BalcaoSituacao.PedeCodigo;
            Flow.Bike.Teleport(new Vector3(restaurant.Position.x, 0.1f, restaurant.Position.z), yaw);
            Flow.Bike.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
            follow.Snap();

            for (int i = 0; i < 60 && !Flow.Balcao.PromptVisible; i++)
            {
                yield return null;
            }

            Assert.IsTrue(Flow.Balcao.PromptVisible);
            SaveAll(dir, "t3_prompt_retirar.png", main);

            // Diálogo: a atendente pede o código (três opções e o atalho para olhar o app).
            Flow.CartaoNavegacao.Visivel = false;
            Flow.BeginCounter();
            Assert.AreEqual(FlowState.NoBalcao, Flow.State);
            follow.enabled = false;
            Look(main, restaurant.Position - Vector3.Normalize(new Vector3(toCounter.x, 0f, toCounter.z)) * 2f + Vector3.up * 2.4f + Vector3.Cross(Vector3.up, counterForward) * 1.5f, npcAt + Vector3.up * 1.2f);
            main.fieldOfView = 50f;
            yield return null;
            yield return null;
            SaveAll(dir, "t4_dialogo_codigo.png", main);

            // Espera: "tá saindo".
            Flow.Balcao.ClickChoice("Olhar o código");
            yield return null;
            SaveAll(dir, "t5_dialogo_olhar_app.png", main);
        }

        /// <summary>A navegação passo a passo: cartão provisório, seta no chão e placa de chevron numa curva fechada.</summary>
        [UnityTest]
        public IEnumerator CaptureNavegacao()
        {
            string dir = Dir;
            Camera main = Camera.main;
            var follow = main.GetComponent<FollowCamera>();
            StartDryWeek();
            Flow.BeginShift();
            Flow.AcceptOffer();
            Flow.CartaoNavegacao.Visivel = true;
            yield return null;

            RouteGuide guide = Flow.Route;
            Instrucao turn = null;

            foreach (Instrucao inst in guide.Instrucoes)
            {
                if (inst.Manobra == Manobra.VireADireita || inst.Manobra == Manobra.VireAEsquerda)
                {
                    turn = inst;

                    break;
                }
            }

            if (turn == null)
            {
                Assert.Ignore("A rota não tem curva fechada.");
            }

            // Põe a moto 60 m antes da curva, na rota, olhando para ela.
            Vector3 at = new Vector3((float)turn.X, 0.1f, (float)turn.Z);
            Vector3 entry = new Vector3((float)turn.EntradaX, 0f, (float)turn.EntradaZ);
            Vector3 start = at - entry * 55f;
            Flow.Bike.SetLocked(true);
            Flow.Bike.Teleport(new Vector3(start.x, 0.1f, start.z), Mathf.Atan2(entry.x, entry.z) * Mathf.Rad2Deg);
            follow.Snap();
            yield return null;
            yield return null;
            Flow.Route.Recalculate();
            yield return null;
            yield return null;
            SaveAll(dir, "n1_navegacao_55m.png", main);

            Vector3 closer = at - entry * 18f;
            Flow.Bike.Teleport(new Vector3(closer.x, 0.1f, closer.z), Mathf.Atan2(entry.x, entry.z) * Mathf.Rad2Deg);
            follow.Snap();
            yield return null;
            yield return null;
            SaveAll(dir, "n2_navegacao_18m.png", main);
            Flow.CartaoNavegacao.Visivel = false;
        }
    }
}
