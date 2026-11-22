using System.Collections;
using Entregador.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Entregador.Game.Tests
{
    public sealed partial class ScreenshotTests
    {
        private void StartAgenda()
        {
            Flow.OfertasImediatas = false;
            Flow.OfertasNaCorridaAutomaticas = false;
            StartDryWeek();
            Flow.BeginShift();
        }

        /// <summary>Online: pílula "Procurando pedidos…" e as zonas quentes no minimapa.</summary>
        [UnityTest]
        public IEnumerator CaptureProcurandoPedidos()
        {
            Camera main = Camera.main;
            StartAgenda();
            ZonaQuente zone = Flow.ZonasAtuais[0];

            // O ponto de vista (rua de verdade) mais perto da zona quente.
            ViewPoint best = null;
            float bestDistance = float.MaxValue;

            foreach (ViewPoint point in Flow.Map.ViewPoints)
            {
                float d = Vector2.Distance(new Vector2(point.Position.x, point.Position.z), new Vector2((float)zone.X, (float)zone.Z));

                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = point;
                }
            }

            Flow.Bike.Teleport(best.Position, best.Yaw);
            Flow.Bike.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
            Flow.ForcarEsperaDaAgenda(60f);
            Flow.Ui.ClearToast();
            yield return new WaitForSeconds(0.8f);
            Flow.Ui.ClearToast();
            yield return null;

            Assert.AreEqual(FlowState.Online, Flow.State);
            SaveAll(Dir, "r1_procurando_pedidos.png", main);
        }

        /// <summary>Cartão compacto de oferta no meio da corrida, com a moto andando.</summary>
        [UnityTest]
        public IEnumerator CaptureOfertaNaCorrida()
        {
            Camera main = Camera.main;
            StartAgenda();
            Flow.ForcarOfertaAgora();
            Flow.AcceptOffer();
            yield return PickUpOrder(Flow);
            Flow.ForcarOfertaNaCorrida();

            yield return new WaitForSeconds(0.8f);
            Flow.Ui.ClearToast();
            yield return null;

            Assert.IsTrue(Flow.Ui.OfferCompactVisible);
            SaveAll(Dir, "r2_oferta_na_corrida.png", main);
        }

        /// <summary>Entrega: o botão Entregar pedido, o diálogo com o porteiro e o aviso de ganho.</summary>
        [UnityTest]
        public IEnumerator CaptureEntrega()
        {
            Camera main = Camera.main;
            StartAgenda();
            Flow.ForcarOfertaAgora();
            Flow.AcceptOffer();
            yield return PickUpOrder(Flow);
            Place customer = Flow.CurrentPlan.Customer;
            Flow.EntregaForcada = EntregaSituacao.PedeParaSubir;
            TeleportNear(Flow, customer);
            Flow.Bike.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;

            for (int i = 0; i < 60 && !Flow.Balcao.PromptVisible; i++)
            {
                yield return null;
            }

            SaveAll(Dir, "r3_prompt_entregar.png", main);
            Assert.IsTrue(Flow.Balcao.ClickPrompt());
            yield return null;
            yield return null;
            SaveAll(Dir, "r4_dialogo_entrega.png", main);

            // Recusa subir: a discussão.
            Assert.IsTrue(Flow.Balcao.ClickChoice("Recusar"));
            yield return null;
            SaveAll(Dir, "r5_discussao.png", main);

            // Cede e sobe: a espera com barra e a fala mudando.
            Flow.SegundosPorMinuto = 2f;
            Assert.IsTrue(Flow.Balcao.ClickChoice("Ceder"));

            for (int i = 0; i < 100; i++)
            {
                yield return null;
            }

            SaveAll(Dir, "r6_espera_subindo.png", main);

            float until = Time.realtimeSinceStartup + 120f;

            while (Flow.State == FlowState.AtDoor && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }

            Flow.SegundosPorMinuto = RelogioJogo.SegundosPorMinuto;
            Assert.AreEqual(FlowState.Online, Flow.State);
            yield return new WaitForSeconds(0.7f);
            StringAssert.Contains("Pedido entregue", Flow.Ui.ToastMessage);
            SaveAll(Dir, "r7_toast_ganho.png", main);
        }
    }
}
