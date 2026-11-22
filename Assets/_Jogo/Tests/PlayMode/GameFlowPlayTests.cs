using System.Collections;
using Entregador.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Entregador.Game.Tests
{
    /// <summary>Sorte controlada: devolve sempre Value (mude entre as etapas para escolher o que acontece).</summary>
    public sealed class ScriptedRandom : IGameRandom
    {
        public double Value = 0.5;

        public double NextDouble() => Value;
    }

    public sealed class GameFlowPlayTests : SceneFixture
    {
        [UnityTest]
        public IEnumerator BikeMovesForwardWhenGasIsHeld()
        {
            var input = Flow.Bike.GetComponent<BikeInput>();
            Flow.Bike.Teleport(Flow.Map.BikeSpawn, Flow.Map.BikeSpawnYaw);
            Flow.Bike.SetLocked(false);
            yield return new WaitForFixedUpdate();

            Vector3 start = Flow.Bike.Position;
            input.SetVirtual(0f, 1f, 0f);
            yield return new WaitForSeconds(2f);
            input.SetVirtual(0f, 0f, 0f);

            // Moto de verdade (Titã 160: 0 a 60 km/h em uns 6 s): em 2 s anda uns 5 m e chega perto de 30 km/h.
            float forward = Vector3.Dot(Flow.Bike.Position - start, Heading(Flow.Map.BikeSpawnYaw));
            Assert.Greater(forward, 3f, "A moto deveria andar para frente com o acelerador apertado.");
            Assert.Greater(Flow.Bike.ForwardSpeed, 5f);
            Assert.Less(Flow.Bike.Position.y, 1f, "A moto deveria ficar no chão.");
        }

        [UnityTest]
        public IEnumerator BikeTurnsWhenSteeringAndStopsWhenBraking()
        {
            var input = Flow.Bike.GetComponent<BikeInput>();
            Flow.Bike.Teleport(Flow.Map.BikeSpawn, Flow.Map.BikeSpawnYaw);
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

            yield return PickUpOrder(Flow);

            yield return ArriveAtCustomer(Flow);

            Flow.TakePhoto();
            Assert.IsTrue(Flow.Tracker.PhotoTaken);
            Flow.Deliver();

            Assert.AreEqual(FlowState.Offer, Flow.State, "Entrega comum: sem painel de resultado, só o aviso de ganho, e o app já oferece o próximo.");
            StringAssert.Contains("Pedido entregue", Flow.Ui.ToastMessage);
            Assert.IsNotNull(Flow.LastResult, "Deveria existir um DeliveryResult.");
            Assert.Greater(Flow.LastResult.Pay.Cents, 0);
            Assert.AreNotEqual(before, Flow.Session.Wallet.Balance);
            Assert.AreEqual(1, Flow.Session.App.DeliveriesThisWeek);
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
            Assert.AreEqual(FlowState.FreeRoam, Flow.State, "A nova semana começa na exploração livre, sem painel do dia empurrando o turno.");
            Assert.IsFalse(Flow.Ui.ModalVisible);
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
            Assert.GreaterOrEqual(Flow.Tracker.ElapsedSeconds, 5f * Flow.SegundosPorMinuto - 0.01f);
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

        // ---------- Rodada 2: tempo das corridas, GPS, calçada, chuva, trânsito ----------

        [UnityTest]
        public IEnumerator PlannedRunTimesLandBetween90And260SecondsForTypicalOrders()
        {
            var random = new SeededRandom(4);
            var starts = new System.Collections.Generic.List<Vector3> { Flow.Map.BikeSpawn };

            // Endereços de cliente estão em ordem de distância do largo: os 6 primeiros são o anel próximo.
            for (int k = 0; k < 6; k += 2)
            {
                starts.Add(Flow.Map.Customers[k].Position);
            }

            float min = float.MaxValue;
            float max = 0f;
            var table = new System.Text.StringBuilder();

            foreach (Vector3 start in starts)
            {
                for (double km = 2.0; km <= 5.01; km += 0.5)
                {
                    var order = new Order(1, km, Money.Reais(20), false);
                    RunPlan plan = RunPlanner.Plan(Flow.Map, Flow.Tuning, order, start, random, null);
                    float expected = plan.TotalMeters / Flow.Tuning.ReferenceSpeed * Flow.Tuning.ExpectedTimeFactor;
                    min = Mathf.Min(min, expected);
                    max = Mathf.Max(max, expected);

                    Assert.AreEqual(km, plan.DeliveryMeters * Flow.Tuning.WorldToGameKm, 0.6, $"Distância da rota para {km} km");
                    table.AppendLine($"{km:0.0} km: previsto {expected:0} s (rota {plan.DeliveryMeters:0} m + busca {plan.PickupMeters:0} m)");
                }
            }

            // Todas as distâncias possíveis de pedido (1,5 a 5,5 km) têm rota dentro da tolerância, de qualquer ponto de partida;
            // os tempos de quem parte de um endereço distante (busca longa) só são registrados no log.
            float farMax = 0f;

            for (int k = 0; k < Flow.Map.Customers.Length; k++)
            {
                for (double km = 1.5; km <= 5.51; km += 0.25)
                {
                    RunPlan plan = RunPlanner.Plan(Flow.Map, Flow.Tuning, new Order(1, km, Money.Reais(20), false), Flow.Map.Customers[k].Position, random, null);
                    Assert.AreEqual(km, plan.DeliveryMeters * Flow.Tuning.WorldToGameKm, 0.6, $"Distância da rota para {km} km (partida {k})");
                    farMax = Mathf.Max(farMax, plan.TotalMeters / Flow.Tuning.ReferenceSpeed);
                }
            }

            Debug.Log($"[Tempo previsto] pior caso (partida distante, 5,5 km): {farMax:0} s");
            Debug.Log("[Tempo previsto] tabela:\n" + table);
            Assert.GreaterOrEqual(min, 90f, "Tempo previsto mínimo curto demais.\n" + table);
            Assert.LessOrEqual(max, 260f, "Tempo previsto máximo longo demais.\n" + table);
            Debug.Log($"[Tempo previsto] mínimo {min:0} s, máximo {max:0} s (WorldToGameKm={Flow.Tuning.WorldToGameKm}, referência={Flow.Tuning.ReferenceSpeed} m/s)");
            yield return null;
        }

        [UnityTest]
        public IEnumerator GpsLineIsANarrowRibbonThatStartsAheadOfTheBike()
        {
            Flow.NewWeek(123);
            Flow.BeginShift();
            Flow.AcceptOffer();
            yield return null;
            yield return null;

            var line = Flow.Route.GetComponent<LineRenderer>();
            Assert.LessOrEqual(line.widthMultiplier, 1.2f, "A fita do GPS deve ter cerca de 1 m.");
            Assert.GreaterOrEqual(line.positionCount, 2);

            Vector3 first = line.GetPosition(0);
            Vector3 bike = Flow.Bike.Position;
            float flat = Mathf.Sqrt((first.x - bike.x) * (first.x - bike.x) + (first.z - bike.z) * (first.z - bike.z));
            Assert.GreaterOrEqual(flat, 5.5f, "Os primeiros metros da linha não devem ser desenhados.");
            Assert.Less(first.y, 0.5f, "A linha deve ficar rente ao asfalto.");
        }

        /// <summary>Procura, ao longo da rua do ponto de vista, um lugar de onde 8 m à direita só há calçada e parede de prédio (sem poste, árvore nem carro).</summary>
        private bool FindClearCurbCrossing(ViewPoint street, out Vector3 start, out Vector3 right)
        {
            Vector3 flow = Heading(street.Yaw);
            right = Vector3.Cross(Vector3.up, flow);

            for (float along = -12f; along <= 40f; along += 3f)
            {
                start = street.Position + flow * along;
                bool clear = true;

                foreach (RaycastHit hit in Physics.RaycastAll(start + Vector3.up * 0.5f, right, 8f))
                {
                    clear &= hit.collider.name.StartsWith("Calcada_") || hit.collider.name.StartsWith("Paredes_");
                }

                if (clear && !Physics.CheckSphere(start + Vector3.up * 1.6f, 1.0f))
                {
                    return true;
                }
            }

            start = street.Position;

            return false;
        }

        [UnityTest]
        public IEnumerator BikeRidesUpTheSidewalkCurbInsteadOfHittingAWall()
        {
            ViewPoint street = View("residencial");
            Assert.IsTrue(FindClearCurbCrossing(street, out Vector3 start, out Vector3 right), "Nenhum trecho de rua livre para cruzar a guia.");
            float yaw = Mathf.Atan2(right.x, right.z) * Mathf.Rad2Deg;
            Flow.Bike.Teleport(new Vector3(start.x, 0.1f, start.z), yaw);
            Flow.Bike.SetLocked(false);
            var input = Flow.Bike.GetComponent<BikeInput>();
            yield return new WaitForFixedUpdate();

            float maxY = float.MinValue;
            input.SetVirtual(0f, 1f, 0f);

            for (int i = 0; i < 40; i++)
            {
                yield return new WaitForSeconds(0.05f);
                maxY = Mathf.Max(maxY, Flow.Bike.Position.y);
            }

            input.SetVirtual(0f, 0f, 0f);
            float crossed = Vector3.Dot(Flow.Bike.Position - start, right);
            Assert.Greater(crossed, 3.9f, "A moto deveria passar do meio-fio (rua de 6 a 7,5 m) e andar um pouco na calçada.");
            Assert.Greater(maxY, 0.03f, "A moto deveria subir na calçada (cerca de 15 cm de altura).");
        }

        [UnityTest]
        public IEnumerator GpsRoutesNeverDriveAlongTheBusCorridor()
        {
            BusLane[] lanes = Flow.Map.BusLanes;
            Assert.Greater(lanes.Length, 20, "O mapa do OSM deveria ter corredores de ônibus.");
            var starts = new System.Collections.Generic.List<Vector3> { Flow.Map.BikeSpawn };

            foreach (Place restaurant in Flow.Map.Restaurants)
            {
                starts.Add(restaurant.Position);
            }

            var route = new System.Collections.Generic.List<Vector3>();
            int sampled = 0;

            foreach (Vector3 from in starts)
            {
                foreach (Place customer in Flow.Map.Customers)
                {
                    Flow.Map.Graph.FindRoute(from, customer.Position, route);

                    // Pula o primeiro e o último ponto (são a moto e o endereço, colados na rua): confere o caminho em si.
                    for (int i = 1; i + 2 < route.Count; i++)
                    {
                        float length = Vector3.Distance(route[i], route[i + 1]);

                        for (float d = 0f; d < length; d += 2f)
                        {
                            Vector3 p = Vector3.Lerp(route[i], route[i + 1], d / Mathf.Max(0.01f, length));
                            sampled++;

                            foreach (BusLane lane in lanes)
                            {
                                // Atravessar o corredor num cruzamento é legal; andar ao longo dele não.
                                Vector3 heading = (route[i + 1] - route[i]).normalized;
                                Vector3 axis = (lane.B - lane.A).normalized;
                                bool inside = lane.DistanceTo(p) < lane.HalfWidth;
                                Assert.IsFalse(inside && Mathf.Abs(Vector3.Dot(heading, axis)) > 0.8f, $"Rota de {from} até {customer.Name} anda ao longo do corredor de ônibus em {p}.");
                            }
                        }
                    }
                }
            }

            Assert.Greater(sampled, 1000);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RainEffectFollowsTheWeather()
        {
            var rain = Object.FindFirstObjectByType<RainEffect>();
            Assert.IsNotNull(rain, "RainEffect não encontrado na cena.");

            bool sawRain = false;
            bool sawDry = false;
            Flow.NewWeek(123);

            for (int day = 1; day <= 7 && Flow.State != FlowState.WeekReport; day++)
            {
                if (Flow.Session.IsRainingToday)
                {
                    sawRain = true;
                    Assert.IsTrue(rain.Raining, "Dia de chuva: a chuva deveria estar ligada.");
                }
                else
                {
                    sawDry = true;
                    Assert.IsFalse(rain.Raining, "Dia seco: a chuva deveria estar desligada.");
                }

                Flow.Ui.ClickModalButton("Folgar");
            }

            Assert.IsTrue(sawRain && sawDry);
            yield return null;
        }

        [UnityTest]
        public IEnumerator EightTrafficCarsDriveTheirLoops()
        {
            var cars = Object.FindObjectsByType<TrafficLoop>(FindObjectsSortMode.None);
            Assert.AreEqual(8, cars.Length);

            var before = new Vector3[cars.Length];

            for (int i = 0; i < cars.Length; i++)
            {
                before[i] = cars[i].transform.position;
                Assert.GreaterOrEqual(cars[i].CruiseSpeed, 7f);
                Assert.LessOrEqual(cars[i].CruiseSpeed, 9f);
            }

            yield return new WaitForSeconds(1.5f);

            for (int i = 0; i < cars.Length; i++)
            {
                float moved = Vector3.Distance(before[i], cars[i].transform.position);
                Assert.Greater(moved, 6f, "Carro " + cars[i].name + " deveria andar ~" + cars[i].CruiseSpeed * 1.5f + " m.");
            }
        }

        [UnityTest]
        public IEnumerator TrafficCarsNeverInterpenetrate()
        {
            var cars = Object.FindObjectsByType<TrafficLoop>(FindObjectsSortMode.None);
            float closest = float.MaxValue;
            string who = string.Empty;

            for (int step = 0; step < 100; step++)
            {
                yield return new WaitForSeconds(0.25f);

                for (int i = 0; i < cars.Length; i++)
                {
                    for (int j = i + 1; j < cars.Length; j++)
                    {
                        float d = Vector3.Distance(cars[i].transform.position, cars[j].transform.position);

                        if (d < closest)
                        {
                            closest = d;
                            who = cars[i].name + " e " + cars[j].name;
                        }
                    }
                }
            }

            Assert.Greater(closest, 2.6f, "Dois carros de trânsito se atravessaram (" + who + ").");
        }

        [UnityTest]
        public IEnumerator RestaurantsAndCustomersSitOnTheRoadNetwork()
        {
            Assert.AreEqual(4, Flow.Map.Restaurants.Length);
            Assert.AreEqual(20, Flow.Map.Customers.Length);
            CollectionAssert.AreEqual(new[] { "Pastelaria do Largo", "Pizzaria Bella Amaro", "Marmitaria da Dona Cida", "Hamburgueria Zona Sul" }, System.Array.ConvertAll(Flow.Map.Restaurants, p => p.Name));

            var all = new System.Collections.Generic.List<Place>(Flow.Map.Restaurants);
            all.AddRange(Flow.Map.Customers);

            foreach (Place place in all)
            {
                Flow.Map.Graph.NearestEdge(place.Position, out Vector3 snapped);
                Assert.Less(Vector3.Distance(snapped, place.Position), 12f, place.Name + " deveria estar junto de uma rua do grafo.");
                Assert.IsFalse(Physics.CheckSphere(place.Position + Vector3.up * 1.2f, 1.0f), place.Name + " não pode ficar dentro de um prédio ou carro.");
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator TrafficCarStopsForTheBikeAndCollisionCountsAsAHit()
        {
            Flow.NewWeek(321);
            Flow.BeginShift();
            Flow.AcceptOffer();

            TrafficLoop car = null;
            Vector3 spot = Vector3.zero;

            // Procura um carro com 9 m de rua livre à frente (longe de esquinas e de prédios).
            for (int attempt = 0; attempt < 40 && car == null; attempt++)
            {
                foreach (TrafficLoop candidate in Object.FindObjectsByType<TrafficLoop>(FindObjectsSortMode.None))
                {
                    Vector3 ahead = candidate.transform.position + candidate.transform.forward * 9f;

                    Vector3 toWaypoint = candidate.Waypoints[candidate.NextIndex] - candidate.transform.position;
                    toWaypoint.y = 0f;
                    bool straight = Vector3.Angle(candidate.transform.forward, toWaypoint) < 3f;

                    if (straight && candidate.DistanceToNextWaypoint > 28f && !Physics.CheckSphere(ahead + Vector3.up * 1.3f, 0.8f) && !Physics.CheckSphere(candidate.transform.position + candidate.transform.forward * 4.5f + Vector3.up * 1.3f, 0.8f))
                    {
                        car = candidate;
                        spot = ahead;

                        break;
                    }
                }

                if (car == null)
                {
                    yield return new WaitForSeconds(0.5f);
                }
            }

            Assert.IsNotNull(car, "Nenhum carro com a frente livre.");
            yield return new WaitForFixedUpdate();

            // Moto 9 m à frente do carro, virada para ele.
            Vector3 forward = car.transform.forward;
            spot = car.transform.position + forward * 9f;
            Flow.Bike.Teleport(new Vector3(spot.x, 0.1f, spot.z), car.transform.eulerAngles.y + 180f);
            Flow.Bike.SetLocked(false);

            yield return new WaitForSeconds(0.8f);
            Assert.IsTrue(car.Stopped, $"O carro deveria parar com a moto logo à frente. carro {car.transform.position} v={car.CurrentSpeed:0.0}, moto {Flow.Bike.Position}, distância {Vector3.Distance(car.transform.position, Flow.Bike.Position):0.0}");
            Assert.Less(car.CurrentSpeed, 4f);

            var input = Flow.Bike.GetComponent<BikeInput>();
            float food = Flow.Tracker.FoodCondition;
            input.SetVirtual(0f, 1f, 0f);
            yield return new WaitForSeconds(2f);
            input.SetVirtual(0f, 0f, 0f);

            Assert.IsTrue(Flow.Tracker.FoodCondition < food || Flow.Tracker.DamageAdded > 0f, "Bater no carro deveria derrubar comida e/ou causar dano.");
        }

        // ---------- Rodada 2: fluxos que faltavam testar ----------

        private IEnumerator DeliverWithClaim(ScriptedRandom random, bool photo)
        {
            Flow.NewWeek(1, random);
            Assert.IsTrue(Flow.Ui.ClickModalButton("Começar turno"));
            Assert.IsTrue(Flow.Ui.ClickModalButton("Aceitar"));
            yield return PickUpOrder(Flow);
            yield return ArriveAtCustomer(Flow);

            if (photo)
            {
                Flow.TakePhoto();
            }

            random.Value = 0.0;
            Flow.Deliver();
            random.Value = 0.5;
            Assert.AreEqual(FlowState.Result, Flow.State);
            Assert.IsNotNull(Flow.LastResult.AfterDeliveryIncident, "Com sorte 0,0 o cliente deveria reclamar.");
            Assert.IsTrue(Flow.Ui.ClickModalButton("Continuar"));
            Assert.AreEqual(FlowState.Claim, Flow.State);
            StringAssert.Contains("Reclamação", Flow.Ui.ModalTitle);
            StringAssert.Contains("não recebeu", Flow.Ui.ModalBody);
        }

        [UnityTest]
        public IEnumerator ClaimShowPhotoKeepsThePayment()
        {
            var random = new ScriptedRandom();
            yield return DeliverWithClaim(random, true);
            Assert.AreEqual(3, Flow.Ui.ModalButtonLabels.Count);

            Money before = Flow.Session.Wallet.Balance;
            Assert.IsTrue(Flow.Ui.ClickModalButton("foto"));
            Assert.AreEqual(before, Flow.Session.Wallet.Balance);
            StringAssert.Contains("ficou do seu lado", Flow.Ui.ModalBody);
            Assert.IsTrue(Flow.Ui.ClickModalButton("Continuar"));
            Assert.AreEqual(FlowState.Offer, Flow.State);
        }

        [UnityTest]
        public IEnumerator ClaimContestWinsOrLosesByLuck()
        {
            var random = new ScriptedRandom();
            yield return DeliverWithClaim(random, false);
            Assert.AreEqual(2, Flow.Ui.ModalButtonLabels.Count, "Sem foto, não existe a opção de mostrar a foto.");

            Money before = Flow.Session.Wallet.Balance;
            random.Value = 0.1;
            Assert.IsTrue(Flow.Ui.ClickModalButton("Contestar"));
            Assert.AreEqual(before, Flow.Session.Wallet.Balance, "Contestar com sorte 0,1 ganha.");
            StringAssert.Contains("ficou do seu lado", Flow.Ui.ModalBody);

            random.Value = 0.0;
            yield return DeliverWithClaim(random, false);
            before = Flow.Session.Wallet.Balance;
            random.Value = 0.9;
            Assert.IsTrue(Flow.Ui.ClickModalButton("Contestar"));
            Assert.Less(Flow.Session.Wallet.Balance.Cents, before.Cents, "Contestar com sorte 0,9 perde: estorno do pagamento.");
            StringAssert.Contains("estornou", Flow.Ui.ModalBody);
        }

        [UnityTest]
        public IEnumerator ClaimAcceptTheLossRefundsAndGivesOneStar()
        {
            var random = new ScriptedRandom();
            yield return DeliverWithClaim(random, true);

            Money before = Flow.Session.Wallet.Balance;
            double ratingBefore = Flow.Session.App.Rating;
            Assert.IsTrue(Flow.Ui.ClickModalButton("prejuízo"));
            Assert.Less(Flow.Session.Wallet.Balance.Cents, before.Cents);
            Assert.Less(Flow.Session.App.Rating, ratingBefore, "Aceitar o prejuízo derruba a nota (1 estrela).");
            StringAssert.Contains("nota 1", Flow.Ui.ModalBody);
        }

        [UnityTest]
        public IEnumerator BlockedAccountShowsTheMessageAndPassesTheDay()
        {
            Flow.NewWeek(5);

            // Quatro notas 1 derrubam a média abaixo de 4,3 e bloqueiam o dia seguinte.
            for (int i = 0; i < 4; i++)
            {
                Flow.Session.App.Rate(1, Flow.Session.Day);
            }

            Assert.AreEqual(2, Flow.Session.App.BlockedDay);
            Assert.IsTrue(Flow.Ui.ClickModalButton("Folgar"));
            Assert.AreEqual(2, Flow.Session.Day);

            Assert.IsTrue(Flow.Ui.ClickModalButton("Começar turno"));
            Assert.AreEqual(FlowState.Blocked, Flow.State);
            Assert.AreEqual("Conta bloqueada", Flow.Ui.ModalTitle);
            StringAssert.Contains("bloqueou sua conta", Flow.Ui.ModalBody);
            Assert.IsFalse(Flow.Ui.HudVisible);

            Assert.IsTrue(Flow.Ui.ClickModalButton("Ok"));
            Assert.AreEqual(FlowState.DayStart, Flow.State);
            Assert.AreEqual(3, Flow.Session.Day, "O dia bloqueado passa como folga.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator OfferTimesOutAndIsRejectedAfterTheOfferSeconds()
        {
            Assert.AreEqual(15f, Flow.Tuning.OfferSeconds, "O padrão do tempo da oferta é 15 s.");
            Flow.Tuning.OfferSeconds = 1f;

            Flow.NewWeek(8);
            Flow.BeginShift();
            Assert.AreEqual(FlowState.Offer, Flow.State);
            int firstId = Flow.CurrentOrder.Id;
            Assert.AreEqual(1, Flow.Session.App.Offered);

            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(firstId, Flow.CurrentOrder.Id, "Ainda dentro do tempo.");
            StringAssert.Contains("Responda em", Flow.Ui.ModalBody);

            yield return new WaitForSeconds(1.0f);
            Assert.AreEqual(FlowState.Offer, Flow.State);
            Assert.AreNotEqual(firstId, Flow.CurrentOrder.Id, "O pedido sem resposta deveria ser recusado e outro oferecido.");
            Assert.AreEqual(0, Flow.Session.App.Accepted);
            Assert.GreaterOrEqual(Flow.Session.App.Offered, 2);
            Flow.Tuning.OfferSeconds = 15f;
        }
    }
}
