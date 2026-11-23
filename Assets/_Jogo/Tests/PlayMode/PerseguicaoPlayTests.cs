using System.Collections;
using Entregador.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Entregador.Game.Tests
{
    /// <summary>Toda perseguição acaba: pega (abordagem), desiste (longe ou fora de vista) ou é dispensada; a sirene nunca fica ligada à toa.</summary>
    public sealed class PerseguicaoPlayTests : SceneFixture
    {
        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
        }

        private static IEnumerator WaitFor(System.Func<bool> condition, float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;

            while (!condition() && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }
        }

        /// <summary>Exploração livre com a moto parada e a viatura de ronda a 60 m dela.</summary>
        private PoliceUnit FreeRoamWithPatrolNearby(IGameRandom random = null)
        {
            if (random != null)
            {
                Flow.NewWeek(1, random);
            }

            Flow.EnterFreeRoam();
            PoliceUnit patrol = Flow.Patrol;
            Assert.IsNotNull(patrol);
            Assert.AreEqual(FlowState.FreeRoam, Flow.State);
            Vector3 at = Flow.Bike.Position;
            Vector3 far = new Vector3(at.x - 60f, 0.02f, at.z);
            var body = patrol.GetComponent<Rigidbody>();
            body.position = far;
            patrol.transform.position = far;

            return patrol;
        }

        [UnityTest]
        public IEnumerator FlagranteInFreeRoamGivesFeedbackThenEndsInAnAbordagemWithTheSirenOffAndACooldown()
        {
            PoliceUnit patrol = FreeRoamWithPatrolNearby();
            yield return null;
            Flow.ForceFlagrante(Infracao.Contramao);

            Assert.AreEqual(FlowState.Abordagem, Flow.State);
            Assert.AreEqual(PoliceMode.Perseguicao, patrol.Mode);
            StringAssert.Contains("Viatura te seguindo", Flow.Ui.ToastMessage, "Aviso claro de que a viatura começou a seguir.");
            yield return null;
            yield return null;
            Assert.IsTrue(Flow.Ui.PoliciaVisivel, "Chip vermelho da polícia no HUD durante a perseguição.");

            yield return WaitFor(() => Flow.AbordagemPanelShown, 6f);
            Assert.IsTrue(Flow.AbordagemPanelShown);
            Assert.IsTrue(Flow.Ui.ClickModalButton("Parar"));
            Assert.AreEqual(FlowState.AbordagemResultado, Flow.State);
            Assert.IsFalse(patrol.SirenActive, "Com o resultado na tela a viatura já encostou e a sirene calou.");

            Assert.IsTrue(Flow.Ui.ClickModalButton("Seguir viagem"));
            Assert.AreEqual(FlowState.FreeRoam, Flow.State);
            Assert.AreEqual(PoliceMode.Patrulha, patrol.Mode, "A ronda volta a rondar.");
            Assert.IsFalse(patrol.SirenActive);
            yield return null;
            yield return null;
            Assert.IsFalse(Flow.Ui.PoliciaVisivel, "Chip some quando a perseguição acaba.");
            Assert.GreaterOrEqual(patrol.NoFlagUntil - Time.time, 55f, "A mesma viatura não autua de novo por ~60 s.");
            Assert.IsFalse(Flow.Monitor.ReportarInfracao(Infracao.AvancarSinalVermelho), "E o monitor também espera.");
        }

        [UnityTest]
        public IEnumerator ChaseStartedOutsideAnyAbordagemStateIsDismissedWithinAFewSeconds()
        {
            PoliceUnit patrol = FreeRoamWithPatrolNearby();
            yield return null;
            patrol.StartChase(Flow.Bike, true);
            Assert.AreEqual(PoliceMode.Perseguicao, patrol.Mode);

            yield return WaitFor(() => patrol.Mode != PoliceMode.Perseguicao, 3f);
            Assert.AreNotEqual(PoliceMode.Perseguicao, patrol.Mode, "Sem abordagem no fluxo, a perseguição não pode continuar.");
            Assert.IsFalse(patrol.SirenActive);
            Assert.AreEqual(PoliceMode.Patrulha, patrol.Mode);
        }

        [UnityTest]
        public IEnumerator FleeingAndGettingFarAwayMakesTheUnitGiveUpAndKeepsTheOutcome()
        {
            var random = new ScriptedRandom();
            PoliceUnit patrol = FreeRoamWithPatrolNearby(random);
            Flow.Tuning.ChaseSeconds = 120f;
            yield return null;
            Flow.ForceFlagrante(Infracao.Contramao);
            yield return WaitFor(() => Flow.AbordagemPanelShown, 6f);

            random.Value = 0.1; // o Core decide: escapa
            Assert.IsTrue(Flow.Ui.ClickModalButton("Fugir"));
            random.Value = 0.5;
            Assert.AreEqual(FlowState.Perseguicao, Flow.State);
            Assert.AreEqual(PoliceMode.Perseguicao, patrol.Mode);

            Vector3 at = patrol.transform.position;
            Flow.Bike.Teleport(new Vector3(at.x + 320f, 0.1f, at.z), 0f);
            yield return WaitFor(() => Flow.State != FlowState.Perseguicao, 3f);

            Assert.AreEqual(FlowState.FreeRoam, Flow.State, "Longe demais: a viatura desiste e a fuga acaba.");
            Assert.AreEqual(PoliceMode.Patrulha, patrol.Mode, "Volta à ronda.");
            Assert.IsFalse(patrol.SirenActive);
            Assert.IsNotNull(Flow.LastOutcome);
            Assert.IsTrue(Flow.LastOutcome.Escapou, "As consequências decididas pelo Core se mantêm.");
            StringAssert.Contains("despistou a viatura", Flow.Ui.ToastMessage);
        }

        [UnityTest]
        public IEnumerator FleeingChaseAfterTheUnitHaltedBehindTheBikeStillReturnsTheUnitToPatrol()
        {
            var random = new ScriptedRandom();
            PoliceUnit patrol = FreeRoamWithPatrolNearby(random);
            Flow.Tuning.ChaseSeconds = 2f;
            yield return null;
            Flow.ForceFlagrante(Infracao.Contramao);
            yield return WaitFor(() => patrol.Mode == PoliceMode.Parada, 12f);
            Assert.AreEqual(PoliceMode.Parada, patrol.Mode, "A viatura encostou atrás da moto parada (sirene calada).");
            yield return WaitFor(() => Flow.AbordagemPanelShown, 6f);

            random.Value = 0.1;
            Assert.IsTrue(Flow.Ui.ClickModalButton("Fugir"));
            random.Value = 0.5;
            yield return WaitFor(() => Flow.State != FlowState.Perseguicao, 20f);

            Assert.AreEqual(FlowState.FreeRoam, Flow.State);
            Assert.IsTrue(patrol.gameObject.activeSelf, "A ronda não some da cidade depois da fuga.");
            Assert.AreEqual(PoliceMode.Patrulha, patrol.Mode);
        }
    }
}
