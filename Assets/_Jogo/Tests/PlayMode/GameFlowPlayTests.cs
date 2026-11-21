using System.Collections;
using Entregador.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Entregador.Game.Tests
{
    public sealed class GameFlowPlayTests : SceneFixture
    {
        [UnityTest]
        public IEnumerator BikeMovesForwardWhenGasIsHeld()
        {
            var input = Flow.Bike.GetComponent<BikeInput>();
            Flow.Bike.Teleport(new Vector3(0f, 0.1f, 0f), 0f);
            Flow.Bike.SetLocked(false);
            yield return new WaitForFixedUpdate();

            Vector3 start = Flow.Bike.Position;
            input.SetVirtual(0f, 1f, 0f);
            yield return new WaitForSeconds(1f);
            input.SetVirtual(0f, 0f, 0f);

            float forward = Vector3.Dot(Flow.Bike.Position - start, Vector3.forward);
            Assert.Greater(forward, 3f, "A moto deveria andar para frente com o acelerador apertado.");
            Assert.Greater(Flow.Bike.ForwardSpeed, 3f);
            Assert.Less(Flow.Bike.Position.y, 1f, "A moto deveria ficar no chão.");
        }

        [UnityTest]
        public IEnumerator BikeTurnsWhenSteeringAndStopsWhenBraking()
        {
            var input = Flow.Bike.GetComponent<BikeInput>();
            Flow.Bike.Teleport(new Vector3(0f, 0.1f, 0f), 0f);
            Flow.Bike.SetLocked(false);
            input.SetVirtual(0f, 1f, 0f);
            yield return new WaitForSeconds(1.5f);

            float yawBefore = Flow.Bike.Yaw;
            input.SetVirtual(1f, 1f, 0f);
            yield return new WaitForSeconds(1f);
            Assert.Greater(Mathf.DeltaAngle(yawBefore, Flow.Bike.Yaw), 20f, "Direita deveria girar a moto no sentido horário.");

            input.SetVirtual(0f, 0f, 1f);
            yield return new WaitForSeconds(1.5f);
            input.SetVirtual(0f, 0f, 0f);
            Assert.Less(Flow.Bike.ForwardSpeed, 0.6f, "Freio deveria parar a moto (depois engata a ré).");
        }

        [UnityTest]
        public IEnumerator StartsOnDayOneWithTheDayPanel()
        {
            Assert.AreEqual(FlowState.DayStart, Flow.State);
            Assert.IsTrue(Flow.Ui.ModalVisible);
            StringAssert.Contains("Dia 1", Flow.Ui.ModalTitle);
            CollectionAssert.Contains(new System.Collections.Generic.List<string>(Flow.Ui.ModalButtonLabels), "Começar turno");
            Assert.IsTrue(Flow.Ui.ModalButtonLabels.Count == 3);
            yield return null;
        }

        [UnityTest]
        public IEnumerator FullDeliveryPaysTheCourier()
        {
            Flow.NewWeek(123);
            Assert.IsTrue(Flow.Ui.ClickModalButton("Começar turno"));
            Assert.AreEqual(FlowState.Offer, Flow.State);
            Assert.IsNotNull(Flow.CurrentOrder);

            Money before = Flow.Session.Wallet.Balance;
            Assert.IsTrue(Flow.Ui.ClickModalButton("Aceitar"));
            Assert.AreEqual(FlowState.DrivingToRestaurant, Flow.State);
            Assert.IsTrue(Flow.Route.HasTarget);
            Assert.Greater(Flow.Route.Route.Count, 1);

            Place restaurant = Flow.CurrentPlan.Restaurant;
            Place customer = Flow.CurrentPlan.Customer;

            TeleportNear(Flow, restaurant);
            yield return new WaitForSeconds(0.2f);
            Assert.AreEqual(FlowState.DrivingToCustomer, Flow.State, "Chegar no restaurante deveria retirar o pedido.");

            TeleportNear(Flow, customer);
            yield return new WaitForSeconds(0.2f);
            Assert.AreEqual(FlowState.AtDoor, Flow.State);

            Flow.TakePhoto();
            Assert.IsTrue(Flow.Tracker.PhotoTaken);
            Flow.Deliver();

            Assert.AreEqual(FlowState.Result, Flow.State);
            Assert.IsNotNull(Flow.LastResult, "Deveria existir um DeliveryResult.");
            Assert.Greater(Flow.LastResult.Pay.Cents, 0);
            Assert.AreNotEqual(before, Flow.Session.Wallet.Balance);
            Assert.AreEqual(1, Flow.Session.App.DeliveriesThisWeek);
            StringAssert.Contains("Pagamento", Flow.Ui.ModalBody);
        }

        [UnityTest]
        public IEnumerator SkipRunProducesAnAutomaticDelivery()
        {
            Flow.NewWeek(321);
            Flow.BeginShift();
            Money before = Flow.Session.Wallet.Balance;
            Flow.AcceptOffer();
            yield return null;

            Flow.SkipRun();

            Assert.AreEqual(FlowState.Result, Flow.State);
            Assert.IsNotNull(Flow.LastResult);
            Assert.IsTrue(Flow.LastResult.Skipped);
            Assert.AreNotEqual(before, Flow.Session.Wallet.Balance);
        }

        [UnityTest]
        public IEnumerator WholeWeekOffShowsTheWeekReport()
        {
            Flow.NewWeek(5);

            for (int day = 1; day <= 7; day++)
            {
                Assert.AreEqual(FlowState.DayStart, Flow.State, "Dia " + day);
                Assert.IsTrue(Flow.Ui.ClickModalButton("Folgar"));
            }

            Assert.AreEqual(FlowState.WeekReport, Flow.State);
            Assert.IsNotNull(Flow.WeekReport);
            Assert.AreEqual(WeekResult.NoVermelho, Flow.WeekReport.Result);
            Assert.AreEqual("No vermelho.", Flow.Ui.ModalTitle);
            StringAssert.Contains("Saldo final", Flow.Ui.ModalBody);
            Assert.IsTrue(Flow.Ui.ClickModalButton("Tentar de novo"));
            Assert.AreEqual(FlowState.DayStart, Flow.State);
            Assert.AreEqual(1, Flow.Session.Day);
            yield return null;
        }

        [UnityTest]
        public IEnumerator OrderRoutesMatchTheOfferedDistance()
        {
            Flow.NewWeek(77);
            Flow.BeginShift();

            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(FlowState.Offer, Flow.State);
                float planKm = Flow.CurrentPlan.DeliveryMeters * Flow.Tuning.WorldToGameKm;
                Assert.AreEqual(Flow.CurrentOrder.DistanceKm, planKm, 0.6, "Rota do pedido " + i);
                Flow.RejectOffer();
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator PotholeAndHardImpactLowerFoodCondition()
        {
            Flow.NewWeek(9);
            Flow.BeginShift();
            Flow.AcceptOffer();
            yield return null;

            float food = Flow.Tracker.FoodCondition;
            Flow.Bike.RegisterBump();
            Assert.Less(Flow.Tracker.FoodCondition, food);

            Flow.Bike.ApplyDamage(0.1f);
            Assert.AreEqual(0.1f, Flow.Tracker.DamageAdded, 0.001f);
        }

        [UnityTest]
        public IEnumerator FlatTireWithAdContinuesTheRunAndAddsDelay()
        {
            Flow.NewWeek(11);
            Flow.BeginShift();
            Flow.AcceptOffer();
            yield return null;

            Flow.ForceFlatTire();
            Assert.AreEqual(FlowState.FlatTire, Flow.State);
            Assert.AreEqual(3, Flow.Ui.ModalButtonLabels.Count);

            Assert.IsTrue(Flow.Ui.ClickModalButton("anúncio"));
            yield return new WaitForSecondsRealtime(2.6f);

            Assert.AreEqual(FlowState.DrivingToRestaurant, Flow.State);
            Assert.GreaterOrEqual(Flow.Tracker.ElapsedSeconds, 5f * Flow.Tuning.SecondsPerGameMinute - 0.01f);
            Assert.IsTrue(Flow.Tracker.Running);
        }

        [UnityTest]
        public IEnumerator FlatTirePushCostsMoneyAndCancelEndsTheDelivery()
        {
            Flow.NewWeek(12);
            Flow.BeginShift();
            Flow.AcceptOffer();
            yield return null;

            Money before = Flow.Session.Wallet.Balance;
            Flow.ForceFlatTire();
            Assert.IsTrue(Flow.Ui.ClickModalButton("borracharia"));
            Assert.AreEqual(before - Flow.Session.Economy.FlatTireRepair, Flow.Session.Wallet.Balance);
            Assert.AreEqual(FlowState.DrivingToRestaurant, Flow.State);

            Flow.ForceFlatTire();
            Assert.IsTrue(Flow.Ui.ClickModalButton("Cancelar"));
            Assert.AreEqual(FlowState.Result, Flow.State);
            StringAssert.Contains("cancelada", Flow.Ui.ModalTitle);
            Assert.IsTrue(Flow.Ui.ClickModalButton("Continuar"));
            Assert.AreEqual(FlowState.Offer, Flow.State);
        }
    }
}
