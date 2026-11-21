using System;
using NUnit.Framework;

namespace Entregador.Core.Tests
{
    public class IncidentSystemTests
    {
        private readonly Money _repair = Money.Reais(40);

        [Test]
        public void PneuFurado_SorteiaPelaChance()
        {
            var system = new IncidentSystem();

            Assert.IsNull(system.RollDuringRun(new FakeRandom(0.5), false));
            Assert.IsNull(system.RollDuringRun(new FakeRandom(0.08), false));

            Incident incident = system.RollDuringRun(new FakeRandom(0.07), false);

            Assert.AreEqual(IncidentKind.PneuFurado, incident.Kind);
            CollectionAssert.AreEqual(
                new[] { IncidentChoice.EmpurrarAteBorracharia, IncidentChoice.VerAnuncio, IncidentChoice.CancelarEntrega },
                incident.Choices);
        }

        [Test]
        public void PneuFurado_RuaEsburacadaDobraAChance()
        {
            var system = new IncidentSystem();

            Assert.IsNull(system.RollDuringRun(new FakeRandom(0.12), false));
            Assert.IsNotNull(system.RollDuringRun(new FakeRandom(0.12), true));
            Assert.IsNull(system.RollDuringRun(new FakeRandom(0.16), true));
        }

        [Test]
        public void Empurrar_CustaConsertoEAtrasa15Min()
        {
            var system = new IncidentSystem();
            Incident incident = system.RollDuringRun(new FakeRandom(0.0), false);

            IncidentOutcome outcome = system.Resolve(incident, IncidentChoice.EmpurrarAteBorracharia, _repair, new FakeRandom());

            Assert.AreEqual(_repair, outcome.Cost);
            Assert.AreEqual(15, outcome.DelayMinutes);
            Assert.IsFalse(outcome.CancelsDelivery);
            Assert.IsFalse(outcome.RequiresRewardedAd);
        }

        [Test]
        public void VerAnuncio_NaoCustaDinheiroEAtrasa5Min()
        {
            var system = new IncidentSystem();
            Incident incident = system.RollDuringRun(new FakeRandom(0.0), false);

            IncidentOutcome outcome = system.Resolve(incident, IncidentChoice.VerAnuncio, _repair, new FakeRandom());

            Assert.AreEqual(Money.Zero, outcome.Cost);
            Assert.AreEqual(5, outcome.DelayMinutes);
            Assert.IsTrue(outcome.RequiresRewardedAd);
        }

        [Test]
        public void Cancelar_EncerraAEntrega()
        {
            var system = new IncidentSystem();
            Incident incident = system.RollDuringRun(new FakeRandom(0.0), false);

            IncidentOutcome outcome = system.Resolve(incident, IncidentChoice.CancelarEntrega, _repair, new FakeRandom());

            Assert.IsTrue(outcome.CancelsDelivery);
            Assert.AreEqual(Money.Zero, outcome.Cost);
        }

        [Test]
        public void NaoRecebeu_ComFoto_TemOpcaoMostrarFoto()
        {
            var system = new IncidentSystem();

            Incident incident = system.RollAfterDelivery(new FakeRandom(0.0), true);

            Assert.AreEqual(IncidentKind.ClienteDizQueNaoRecebeu, incident.Kind);
            CollectionAssert.AreEqual(
                new[] { IncidentChoice.MostrarFoto, IncidentChoice.Contestar, IncidentChoice.Aceitar },
                incident.Choices);
        }

        [Test]
        public void NaoRecebeu_SemFoto_NaoTemOpcaoMostrarFoto()
        {
            var system = new IncidentSystem();

            Incident incident = system.RollAfterDelivery(new FakeRandom(0.0), false);

            CollectionAssert.AreEqual(
                new[] { IncidentChoice.Contestar, IncidentChoice.Aceitar },
                incident.Choices);
        }

        [Test]
        public void NaoRecebeu_SorteioFalha_DevolveNull()
        {
            Assert.IsNull(new IncidentSystem().RollAfterDelivery(new FakeRandom(0.5), true));
        }

        [Test]
        public void MostrarFoto_ResolveSemPerda()
        {
            var system = new IncidentSystem();
            Incident incident = system.RollAfterDelivery(new FakeRandom(0.0), true);

            IncidentOutcome outcome = system.Resolve(incident, IncidentChoice.MostrarFoto, _repair, new FakeRandom());

            Assert.IsFalse(outcome.LosesPay);
            Assert.IsNull(outcome.ForcedStars);
        }

        [Test]
        public void Contestar_Ganha50PorCento()
        {
            var system = new IncidentSystem();
            Incident incident = system.RollAfterDelivery(new FakeRandom(0.0), false);

            IncidentOutcome won = system.Resolve(incident, IncidentChoice.Contestar, _repair, new FakeRandom(0.49));
            IncidentOutcome lost = system.Resolve(incident, IncidentChoice.Contestar, _repair, new FakeRandom(0.5));

            Assert.IsFalse(won.LosesPay);
            Assert.IsTrue(lost.LosesPay);
        }

        [Test]
        public void Aceitar_PerdeOPagamentoEForca1Estrela()
        {
            var system = new IncidentSystem();
            Incident incident = system.RollAfterDelivery(new FakeRandom(0.0), false);

            IncidentOutcome outcome = system.Resolve(incident, IncidentChoice.Aceitar, _repair, new FakeRandom());

            Assert.IsTrue(outcome.LosesPay);
            Assert.AreEqual(1, outcome.ForcedStars);
        }

        [Test]
        public void EscolhaInvalida_Lanca()
        {
            var system = new IncidentSystem();
            Incident noPhoto = system.RollAfterDelivery(new FakeRandom(0.0), false);
            Incident tire = system.RollDuringRun(new FakeRandom(0.0), false);

            Assert.Throws<ArgumentException>(() =>
                system.Resolve(noPhoto, IncidentChoice.MostrarFoto, _repair, new FakeRandom()));
            Assert.Throws<ArgumentException>(() =>
                system.Resolve(tire, IncidentChoice.Aceitar, _repair, new FakeRandom()));
        }
    }
}
