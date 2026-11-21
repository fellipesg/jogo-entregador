using NUnit.Framework;

namespace Entregador.Core.Tests
{
    public class DeliveryAppTests
    {
        private static DeliveryApp NewApp() => new DeliveryApp(new AppConfig());

        [Test]
        public void Pagamento_De3ponto5Km_E15Reais()
        {
            Order order = NewApp().Offer(new FakeRandom(0.5), false);

            Assert.AreEqual(3.5, order.DistanceKm, 0.0001);
            Assert.AreEqual(Money.Reais(15), order.Pay);
            Assert.IsFalse(order.IsRaining);
        }

        [Test]
        public void Chuva_Soma30PorCento()
        {
            Order order = NewApp().Offer(new FakeRandom(0.5), true);

            Assert.AreEqual(Money.Reais(19.5m), order.Pay);
            Assert.IsTrue(order.IsRaining);
        }

        [Test]
        public void Distancia_FicaNoIntervaloEArredondaEmUmDecimal()
        {
            DeliveryApp app = NewApp();

            Assert.AreEqual(1.5, app.Offer(new FakeRandom(0.0), false).DistanceKm, 0.0001);
            Assert.AreEqual(5.5, app.Offer(new FakeRandom(0.9999), false).DistanceKm, 0.0001);
        }

        [Test]
        public void Nota_ComecaEm5()
        {
            Assert.AreEqual(5.0, NewApp().Rating, 0.0001);
        }

        [Test]
        public void Nota_UmaEstrelaDerrubaPara4ponto8()
        {
            DeliveryApp app = NewApp();

            app.Rate(1, 1);

            Assert.AreEqual(4.8, app.Rating, 0.0001);
            Assert.AreEqual(0, app.BlockedDay);
        }

        [Test]
        public void Nota_JanelaEmpurraNotasAntigas()
        {
            DeliveryApp app = NewApp();

            app.Rate(1, 1);

            for (int i = 0; i < 20; i++)
            {
                app.Rate(5, 1);
            }

            Assert.AreEqual(5.0, app.Rating, 0.0001);
        }

        [Test]
        public void Nota_AbaixoDe4ponto3_BloqueiaNoDiaSeguinte()
        {
            DeliveryApp app = NewApp();

            for (int i = 0; i < 3; i++)
            {
                app.Rate(1, 2);
            }

            Assert.AreEqual(4.4, app.Rating, 0.0001);
            Assert.IsFalse(app.IsBlockedOn(3));

            app.Rate(1, 2);

            Assert.AreEqual(4.2, app.Rating, 0.0001);
            Assert.IsTrue(app.IsBlockedOn(3));
            Assert.IsFalse(app.IsBlockedOn(2));
        }

        [Test]
        public void Nota_FicaEntre1e5()
        {
            DeliveryApp app = NewApp();

            app.Rate(0, 1);
            app.Rate(9, 1);

            Assert.AreEqual((18 * 5 + 1 + 5) / 20.0, app.Rating, 0.0001);
        }

        [Test]
        public void Aceite_SemOfertasEh100PorCento()
        {
            DeliveryApp app = NewApp();

            Assert.AreEqual(1.0, app.AcceptanceRate, 0.0001);
            Assert.AreEqual(3, app.OrdersForShift());
        }

        [Test]
        public void Aceite_Abaixo80PorCento_OfereceMenosPedidos()
        {
            DeliveryApp app = NewApp();
            var random = new FakeRandom(0.5);

            for (int i = 0; i < 5; i++)
            {
                app.Offer(random, false);
            }

            app.Accept();
            app.Accept();
            app.Accept();
            app.Accept();

            Assert.AreEqual(0.8, app.AcceptanceRate, 0.0001);
            Assert.AreEqual(3, app.OrdersForShift());

            app.Offer(random, false);

            Assert.AreEqual(4.0 / 6, app.AcceptanceRate, 0.0001);
            Assert.AreEqual(2, app.OrdersForShift());
        }

        [Test]
        public void Cancelar_ContaComoRecusa()
        {
            DeliveryApp app = NewApp();

            app.Offer(new FakeRandom(), false);
            app.Accept();
            app.Cancel();

            Assert.AreEqual(0, app.Accepted);
            Assert.AreEqual(0.0, app.AcceptanceRate, 0.0001);
        }

        [Test]
        public void Meta_Com20EntregasBateAMeta()
        {
            DeliveryApp app = NewApp();

            for (int i = 0; i < 19; i++)
            {
                app.CountDelivery();
            }

            Assert.IsFalse(app.GoalReached);

            app.CountDelivery();

            Assert.IsTrue(app.GoalReached);

            app.StartNewWeek();

            Assert.AreEqual(0, app.DeliveriesThisWeek);
        }
    }
}
