using System.Collections;
using Entregador.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Entregador.Game.Tests
{
    public sealed partial class ScreenshotTests
    {
        /// <summary>Corrida com o HUD novo: cartão de navegação (manobra do RouteGuide) e o chip de sinal vermelho com câmera.</summary>
        [UnityTest]
        public IEnumerator CaptureIntegracaoHud()
        {
            string dir = Dir;
            Camera main = Camera.main;
            var follow = main.GetComponent<FollowCamera>();
            StartDryWeek();
            Flow.BeginShift();
            yield return null;
            yield return null;
            SaveAll(dir, "x1_oferta.png", main);
            Flow.AcceptOffer();
            yield return null;

            Instrucao turn = null;

            foreach (Instrucao inst in Flow.Route.Instrucoes)
            {
                if (inst.Manobra == Manobra.VireADireita || inst.Manobra == Manobra.VireAEsquerda)
                {
                    turn = inst;

                    break;
                }
            }

            if (turn != null)
            {
                Vector3 at = new Vector3((float)turn.X, 0.1f, (float)turn.Z);
                Vector3 entry = new Vector3((float)turn.EntradaX, 0f, (float)turn.EntradaZ);
                Vector3 start = at - entry * 70f;
                Flow.Bike.SetLocked(true);
                Flow.Bike.Teleport(new Vector3(start.x, 0.1f, start.z), Mathf.Atan2(entry.x, entry.z) * Mathf.Rad2Deg);
                follow.Snap();

                for (int i = 0; i < 6; i++)
                {
                    yield return null;
                }

                Flow.Route.Recalculate();
                yield return null;
                yield return null;
                yield return null;
            }

            SaveAll(dir, "x2_corrida_navegacao.png", main);

            // Sinal vermelho à frente, com fotossensor.
            SemaforoControlador camera = null;

            foreach (SemaforoControlador c in SemaforoControlador.Lista)
            {
                if (c.Fotossensor)
                {
                    camera = c;

                    break;
                }
            }

            Assert.IsNotNull(camera);
            SemaforoAproximacao a = camera.Aproximacoes[0];
            camera.Fixar(a.Grupo, SemaforoEstado.Vermelho, 1.0);
            Vector3 spot = a.Linha - a.Direcao * 35f;
            Flow.Bike.SetLocked(true);
            Flow.Bike.Teleport(new Vector3(spot.x, 0.1f, spot.z), Mathf.Atan2(a.Direcao.x, a.Direcao.z) * Mathf.Rad2Deg);
            follow.Snap();

            for (int i = 0; i < 8; i++)
            {
                yield return null;
            }

            Assert.IsTrue(Flow.Ui.SinalVisivel, "Vermelho a 35 m: chip de sinal.");
            Assert.IsTrue(Flow.Ui.CameraSinalVisivel, "Cruzamento com fotossensor: chip da câmera.");
            SaveAll(dir, "x3_sinal_vermelho_chip.png", main);
            camera.Liberar();
        }

        /// <summary>Cada atendente (um personagem do Mixamo por restaurante, com avental) de perto, e o balcão com o diálogo aberto.</summary>
        [UnityTest]
        public IEnumerator CaptureIntegracaoAtendentes()
        {
            string dir = Dir;
            Camera main = Camera.main;
            var follow = main.GetComponent<FollowCamera>();
            StartDryWeek();
            Flow.BeginShift();
            Flow.AcceptOffer();
            yield return null;
            Flow.Ui.SetHudVisible(false);
            Flow.Bike.SetLocked(true);
            follow.enabled = false;
            Place[] places = Flow.Map.Restaurants;

            for (int k = 0; k < places.Length; k++)
            {
                Place p = places[k];
                Vector3 forward = Quaternion.Euler(0f, p.BalcaoYaw, 0f) * Vector3.forward;
                Vector3 right = Vector3.Cross(Vector3.up, forward);
                Look(main, p.Balcao + forward * 2.7f + Vector3.up * 1.55f + right * 0.45f, p.Balcao + Vector3.up * 1.15f);
                main.fieldOfView = 40f;
                yield return new WaitForSeconds(0.3f);
                Save(dir, $"y{k + 1}_atendente_{k}.png", main, false);
            }

            // Balcão com o diálogo aberto, atendente à vista.
            Place restaurant = Flow.CurrentPlan.Restaurant;
            Vector3 f = Quaternion.Euler(0f, restaurant.BalcaoYaw, 0f) * Vector3.forward;
            Flow.SituacaoForcada = BalcaoSituacao.SaindoJaJa;
            Flow.Ui.SetHudVisible(true);
            Flow.Bike.Teleport(new Vector3(restaurant.Position.x, 0.1f, restaurant.Position.z), 0f);
            Flow.Bike.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;

            for (int i = 0; i < 60 && !Flow.Balcao.PromptVisible; i++)
            {
                yield return null;
            }

            SaveAll(dir, "y5_prompt.png", main);
            Flow.BeginCounter();
            Flow.Balcao.ClickChoice("Esperar");
            Look(main, restaurant.Balcao + f * 3.4f + Vector3.up * 2.1f + Vector3.Cross(Vector3.up, f) * 0.9f, restaurant.Balcao + Vector3.up * 1.0f + Vector3.down * 0f);
            main.fieldOfView = 55f;
            yield return null;
            yield return null;
            SaveAll(dir, "y6_balcao_dialogo.png", main);
            main.fieldOfView = 60f;
            follow.enabled = true;
        }
    }
}
