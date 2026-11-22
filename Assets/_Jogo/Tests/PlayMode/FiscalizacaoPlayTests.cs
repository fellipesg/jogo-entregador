using System.Collections;
using Entregador.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace Entregador.Game.Tests
{
    /// <summary>Os testes de PlayMode abrem o jogo direto no dia 1 (sem a tela de novo jogo) e cuidam de zerar o tempo no fim.</summary>
    public static class PlayModeSetup
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void DisableSetupScreen()
        {
            GameFlow.ShowSetupOnStart = false;
        }
    }

    /// <summary>Moto livre (física real, física de motor e câmbio), controles de toque, radar, blitz, perseguição, contramão e perfil de novo jogo.</summary>
    public sealed class FiscalizacaoPlayTests : SceneFixture
    {
        private GameObject _freeBike;

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;

            if (_freeBike != null)
            {
                Object.Destroy(_freeBike);
            }
        }

        // ---------- Utilidades ----------

        /// <summary>Uma moto sozinha no ar (sem gravidade nem cidade), para medir o motor sem esbarrar em nada.</summary>
        private ArcadeBikeController NewFreeBike(MotoModelo modelo, out BikeInput input)
        {
            _freeBike = new GameObject("MotoLivre");
            _freeBike.transform.position = new Vector3(0f, 5000f, 0f);
            Rigidbody body = _freeBike.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.mass = 180f;
            input = _freeBike.AddComponent<BikeInput>();
            var bike = _freeBike.AddComponent<ArcadeBikeController>();
            bike.SetModelo(modelo);
            bike.SetLocked(false);

            return bike;
        }

        private IEnumerator StartRun(ScriptedRandom random, bool blitzToday)
        {
            Flow.NewWeek(1, random);
            random.Value = blitzToday ? 0.1 : 0.5;
            Assert.IsTrue(Flow.Ui.ClickModalButton("Começar turno"));
            random.Value = 0.5;
            Assert.IsTrue(Flow.Ui.ClickModalButton("Aceitar"));
            Assert.IsTrue(Flow.IsDriving);
            yield return null;
        }

        private static float YawOf(Vector3 direction) => Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;

        /// <summary>Põe a moto dentro da zona de uma blitz aberta e espera a abordagem começar.</summary>
        private IEnumerator EnterBlitz(BlitzSite site)
        {
            Vector3 at = site.Position - site.Direction * 3f;
            Flow.Bike.Teleport(new Vector3(at.x, 0.1f, at.z), YawOf(site.Direction));
            Flow.Bike.SetLocked(false);

            for (int i = 0; i < 20 && Flow.State != FlowState.Abordagem; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.AreEqual(FlowState.Abordagem, Flow.State, "Entrar na zona da blitz deveria começar a abordagem.");
        }

        private IEnumerator WaitForStopPanel()
        {
            for (int i = 0; i < 400 && !Flow.AbordagemPanelShown; i++)
            {
                yield return null;
            }

            Assert.IsTrue(Flow.AbordagemPanelShown, "O painel Parar/Fugir deveria aparecer.");
        }

        private BlitzSite OpenSite()
        {
            foreach (BlitzSite site in Flow.BlitzSites)
            {
                if (site.IsOpen)
                {
                    return site;
                }
            }

            Assert.Fail("Nenhuma blitz aberta.");

            return null;
        }

        // ---------- Física da moto ----------

        [UnityTest]
        public IEnumerator TitanHasNoSpeedCapAndReachesItsRealTopSpeed()
        {
            ArcadeBikeController bike = NewFreeBike(MotoModelo.Titan, out BikeInput input);
            input.SetVirtual(0f, 1f, 0f);
            Time.timeScale = 8f;
            float elapsed = 0f;
            float zeroTo60 = -1f;
            int lastGear = 1;

            while (elapsed < 45f)
            {
                yield return new WaitForFixedUpdate();
                elapsed += Time.fixedDeltaTime;

                if (zeroTo60 < 0f && bike.SpeedKmh >= 60f)
                {
                    zeroTo60 = elapsed;
                }

                Assert.GreaterOrEqual(bike.Gear, lastGear, "Na aceleração o câmbio só sobe.");
                Assert.That(bike.Gear, Is.InRange(1, 5));
                Assert.That(bike.Rpm, Is.InRange(1250f, bike.RpmLimit + 1f), "Giro entre a marcha lenta e o corte.");
                lastGear = bike.Gear;
            }

            Assert.Greater(bike.SpeedKmh, 60f, "Sem teto artificial: passa dos 60 km/h.");
            Assert.That(bike.SpeedKmh, Is.InRange(105f, 120f), "Titã 160: velocidade final de uns 115 km/h.");
            Assert.That(zeroTo60, Is.InRange(5f, 7f), "Titã 160: 0 a 60 km/h em uns 6 s.");
            Assert.AreEqual(5, bike.Gear);
            Assert.AreEqual(9000f, bike.RpmLimit);
            Assert.AreEqual(1f, bike.Throttle);
        }

        [UnityTest]
        public IEnumerator TwisterIsFasterThanTheTitan()
        {
            ArcadeBikeController bike = NewFreeBike(MotoModelo.Twister, out BikeInput input);
            input.SetVirtual(0f, 1f, 0f);
            Time.timeScale = 8f;
            float elapsed = 0f;
            float zeroTo60 = -1f;

            while (elapsed < 45f)
            {
                yield return new WaitForFixedUpdate();
                elapsed += Time.fixedDeltaTime;

                if (zeroTo60 < 0f && bike.SpeedKmh >= 60f)
                {
                    zeroTo60 = elapsed;
                }
            }

            Assert.That(bike.SpeedKmh, Is.InRange(130f, 146f), "Tornado 250: velocidade final de uns 140 km/h.");
            Assert.That(zeroTo60, Is.InRange(3.7f, 5.3f), "Tornado 250: 0 a 60 km/h em uns 4,5 s.");
            Assert.AreEqual(10000f, bike.RpmLimit);
        }

        [UnityTest]
        public IEnumerator BrakingStopsFromHighSpeedQuicklyAndEngineBrakesWhenCoasting()
        {
            ArcadeBikeController bike = NewFreeBike(MotoModelo.Titan, out BikeInput input);
            Rigidbody body = bike.GetComponent<Rigidbody>();
            Time.timeScale = 4f;
            body.linearVelocity = Vector3.forward * 25f;
            yield return new WaitForFixedUpdate();

            // Solto o acelerador, a moto perde velocidade (freio-motor, arrasto), mas devagar.
            input.SetVirtual(0f, 0f, 0f);
            float t = 0f;

            while (t < 2f)
            {
                yield return new WaitForFixedUpdate();
                t += Time.fixedDeltaTime;
            }

            Assert.Less(bike.Speed, 24f, "Sem acelerar a moto desacelera.");
            Assert.Greater(bike.Speed, 15f, "Mas rola: não para de repente.");

            // Freio: de ~19 m/s a menos de 3 s (9 m/s²). Segurando o freio parado a moto engata a ré, então mede o tempo até parar.
            float v0 = bike.Speed;
            input.SetVirtual(0f, 0f, 1f);
            t = 0f;

            while (bike.ForwardSpeed > 0.5f && t < 6f)
            {
                yield return new WaitForFixedUpdate();
                t += Time.fixedDeltaTime;
            }

            Assert.Less(t, v0 / 9f + 0.5f, "O freio combinado deveria parar a moto em poucos segundos.");
        }

        [UnityTest]
        public IEnumerator HighSpeedSteersWiderThanLowSpeed()
        {
            ArcadeBikeController bike = NewFreeBike(MotoModelo.Titan, out BikeInput input);
            Rigidbody body = bike.GetComponent<Rigidbody>();
            float[] rates = new float[2];
            float[] speeds = { 8f, 30f };

            for (int i = 0; i < 2; i++)
            {
                bike.Teleport(new Vector3(0f, 5000f, 0f), 0f);
                body.linearVelocity = Vector3.forward * speeds[i];
                input.SetVirtual(1f, 0.6f, 0f);
                yield return new WaitForSeconds(0.3f);
                float yaw0 = bike.Yaw;
                yield return new WaitForSeconds(0.5f);
                rates[i] = Mathf.DeltaAngle(yaw0, bike.Yaw) / 0.5f;
                input.SetVirtual(0f, 0f, 0f);
            }

            Assert.Greater(rates[0], 20f, "Em baixa velocidade gira bem.");
            Assert.Greater(rates[1], 3f, "Em alta ainda gira.");
            Assert.Less(rates[1], rates[0] * 0.6f, "A 108 km/h a moto é bem mais firme (gira menos) que a 29 km/h.");
        }

        [UnityTest]
        public IEnumerator FoodSuffersMoreFromABumpAtSpeed()
        {
            Flow.NewWeek(9);
            Flow.BeginShift();
            Flow.AcceptOffer();
            yield return null;
            Flow.Bike.SetLocked(true);
            float loss0 = Flow.Tracker.FoodCondition;
            Flow.Bike.RegisterBump();
            loss0 -= Flow.Tracker.FoodCondition;

            yield return new WaitForSeconds(1.1f);
            Flow.Bike.GetComponent<Rigidbody>().linearVelocity = Heading(Flow.Bike.Yaw) * 28f;
            float before = Flow.Tracker.FoodCondition;
            Flow.Bike.RegisterBump();
            float loss1 = before - Flow.Tracker.FoodCondition;

            Assert.Greater(loss1, loss0 * 1.8f, "Buraco a 100 km/h deveria abalar a comida bem mais que parado.");
        }

        // ---------- Controles de toque ----------

        private PointerEventData Pointer(Vector2 position) => new PointerEventData(EventSystem.current) { position = position, pointerId = 7 };

        [UnityTest]
        public IEnumerator TouchZonesDriveTheBikeInputWithoutButtons()
        {
            var input = Flow.Bike.GetComponent<BikeInput>();
            TouchZone[] zones = Flow.Ui.GetComponentsInChildren<TouchZone>(true);
            Assert.AreEqual(2, zones.Length, "Duas zonas invisíveis: direção e pedal.");
            TouchZone steer = System.Array.Find(zones, z => z.name == "ZonaDirecao");
            TouchZone pedal = System.Array.Find(zones, z => z.name == "ZonaPedal");
            Assert.IsNotNull(steer);
            Assert.IsNotNull(pedal);

            foreach (Transform t in Flow.Ui.GetComponentsInChildren<Transform>(true))
            {
                Assert.AreNotEqual("Acelerar", t.name, "O botão ACELERAR saiu do HUD.");
                Assert.AreNotEqual("Frear", t.name, "O botão FREAR saiu do HUD.");
            }

            float radius = TouchZone.RadiusPixels;
            Vector2 start = new Vector2(100f, 100f);

            // Pedal: arrastar para cima acelera na proporção; para baixo freia; soltar solta tudo.
            pedal.OnPointerDown(Pointer(start));
            pedal.OnDrag(Pointer(start + new Vector2(0f, radius * 0.5f)));
            Assert.AreEqual(0.5f, input.Gas, 0.02f);
            Assert.AreEqual(0f, input.Brake, 0.001f);
            pedal.OnDrag(Pointer(start + new Vector2(0f, radius * 3f)));
            Assert.AreEqual(1f, input.Gas, 0.001f, "Passar do raio vale 100%.");
            pedal.OnDrag(Pointer(start + new Vector2(0f, -radius * 0.7f)));
            Assert.AreEqual(0f, input.Gas, 0.001f);
            Assert.AreEqual(0.7f, input.Brake, 0.02f);
            pedal.OnPointerUp(Pointer(start));
            Assert.AreEqual(0f, input.Gas, 0.001f);
            Assert.AreEqual(0f, input.Brake, 0.001f);

            // Direção: horizontal a partir de onde o dedo pousou.
            steer.OnPointerDown(Pointer(start));
            steer.OnDrag(Pointer(start + new Vector2(radius * 0.25f, 40f)));
            Assert.AreEqual(0.25f, input.Steer, 0.02f);
            steer.OnDrag(Pointer(start + new Vector2(-radius, 0f)));
            Assert.AreEqual(-1f, input.Steer, 0.001f);
            steer.OnPointerUp(Pointer(start));
            Assert.AreEqual(0f, input.Steer, 0.001f);
            yield return null;
        }

        // ---------- Radar ----------

        [UnityTest]
        public IEnumerator SpeedCameraCapturesAboveTheLimitAndIgnoresTheToleranceBand()
        {
            var random = new ScriptedRandom();
            yield return StartRun(random, false);

            // O fiscal de semáforos fica de fora: a moto teletransportada pode cruzar uma linha no vermelho de um cruzamento com fotossensor.
            Object.FindFirstObjectByType<SemaforoFiscal>().enabled = false;
            SpeedCamera[] radars = Object.FindObjectsByType<SpeedCamera>(FindObjectsSortMode.None);
            Assert.GreaterOrEqual(radars.Length, 6, "A cidade deveria ter de 6 a 8 radares.");
            Assert.LessOrEqual(radars.Length, 8);
            System.Array.Sort(radars, (a, b) => string.CompareOrdinal(a.transform.parent.name, b.transform.parent.name));
            SpeedCamera radar = System.Array.Find(radars, r => r.LimitKmh == 50);
            Assert.IsNotNull(radar, "Os números abaixo (54 e 65 km/h) valem para um radar de limite 50.");
            Vector3 forward = radar.transform.forward;
            Vector3 start = radar.transform.position - forward * 8f;
            start.y = 0.1f;
            var body = Flow.Bike.GetComponent<Rigidbody>();

            // Abaixo do limite: nada. Dentro da tolerância (54 km/h num limite de 50): nada.
            foreach (float mps in new[] { 9f, 15f })
            {
                Flow.Bike.Teleport(start, YawOf(forward));
                body.linearVelocity = forward * mps;
                yield return new WaitForSeconds(1.2f);
            }

            Assert.AreEqual(0, Flow.Tracker.RadarCaptures, "Abaixo do limite (com a tolerância) o radar não captura.");
            Assert.AreEqual(0, radar.Captures);
            Assert.Greater(radar.LastMeasuredKmh, 20f, "O radar mediu a moto mesmo assim.");

            // Acima: 18 m/s = 65 km/h num limite de 50.
            Flow.Bike.Teleport(start, YawOf(forward));
            body.linearVelocity = forward * 18f;
            yield return new WaitForSeconds(1.2f);
            Assert.AreEqual(1, Flow.Tracker.RadarCaptures, "Acima do limite o radar captura.");
            Assert.AreEqual(1, radar.Captures);
            Assert.IsEmpty(Flow.Ui.ToastMessage, "Nenhum aviso ao jogador na hora do flash.");

            // A infração vai no relatório da corrida; a multa chega dias depois (Core), sem aviso agora.
            RunReport report = Flow.Tracker.BuildReport(Flow.Tuning.WorldToGameKm);
            Assert.AreEqual(1, report.Capturas.Count);
            Assert.AreEqual(Infracao.ExcessoVelocidadeAte20, report.Capturas[0]);

            yield return PickUpOrder(Flow);
            yield return ArriveAtCustomer(Flow);
            Flow.Deliver();
            Assert.AreEqual(1, Flow.Session.MultasPendentes.Count, "A multa de radar fica na fila do Core.");
            Assert.Greater(Flow.Session.MultasPendentes[0].DiaChegada, Flow.Session.Day);
            Assert.AreEqual(0, Flow.Tracker.RadarCaptures);
        }

        [UnityTest]
        public IEnumerator EveryRadarHasItsSignAndTheStreetLimitIsKnown()
        {
            SpeedCamera[] radars = Object.FindObjectsByType<SpeedCamera>(FindObjectsSortMode.None);
            Assert.GreaterOrEqual(radars.Length, 6);

            foreach (SpeedCamera radar in radars)
            {
                Assert.That(radar.LimitKmh, Is.InRange(30, 80));
                bool hasSign = false;

                foreach (TMPro.TMP_Text text in radar.transform.parent.GetComponentsInChildren<TMPro.TMP_Text>())
                {
                    hasSign |= text.text.Contains("FISCALIZAÇÃO") || text.text.Contains(radar.LimitKmh + " km/h");
                }

                Assert.IsTrue(hasSign, "Cada radar tem a placa FISCALIZAÇÃO ELETRÔNICA com o limite.");
            }

            // O grafo conhece o limite de cada rua: avenidas 50, ruas residenciais 30.
            RoadGraph graph = Flow.Map.Graph;
            var seen = new System.Collections.Generic.HashSet<int>();

            for (int e = 0; e < graph.EdgeCount; e++)
            {
                seen.Add(graph.SpeedLimitKmh(e));
            }

            CollectionAssert.IsSubsetOf(new[] { 30, 40, 50 }, seen, "Deveria haver ruas de 30, 40 e 50 km/h.");
            yield return null;
        }

        // ---------- Contramão ----------

        private RoadGraph Synthetic(out GameObject holder)
        {
            holder = new GameObject("GrafoSintetico");
            var graph = holder.AddComponent<RoadGraph>();
            var nodes = new[] { new Vector3(0f, 0f, 0f), new Vector3(100f, 0f, 0f), new Vector3(100f, 0f, 60f) };
            graph.Configure(nodes, new[] { 0, 1 }, new[] { 1, 2 }, new[] { true, false }, new[] { 50, 30 });

            return graph;
        }

        [Test]
        public void OnewayDetectionFlagsOnlyTheWrongDirection()
        {
            RoadGraph graph = Synthetic(out GameObject holder);

            try
            {
                Vector3 position = new Vector3(40f, 0f, 1.5f);
                Assert.IsFalse(graph.Sample(position, Vector3.right).WrongWay, "Na mão certa não é contramão.");
                Assert.IsTrue(graph.Sample(position, Vector3.left).WrongWay, "Contra a mão é contramão.");
                Assert.IsFalse(graph.Sample(position, Vector3.forward).WrongWay, "Cruzar a rua não é contramão.");
                Assert.AreEqual(50, graph.Sample(position, Vector3.right).LimitKmh);

                // Trecho de mão dupla: nunca contramão (e limite 30).
                Vector3 twoWay = new Vector3(100.5f, 0f, 30f);
                Assert.IsFalse(graph.Sample(twoWay, Vector3.forward).WrongWay);
                Assert.IsFalse(graph.Sample(twoWay, Vector3.back).WrongWay);
                Assert.AreEqual(30, graph.Sample(twoWay, Vector3.back).LimitKmh);

                // Longe de qualquer rua: nada.
                Assert.AreEqual(-1, graph.Sample(new Vector3(40f, 0f, 80f), Vector3.left).Edge);
            }
            finally
            {
                Object.DestroyImmediate(holder);
            }
        }

        [UnityTest]
        public IEnumerator RidingAgainstARealOnewayStreetIsDetectedAndWithTheFlowIsNot()
        {
            Flow.NewWeek(3);
            RoadGraph graph = Flow.Map.Graph;
            int edge = -1;

            for (int e = 0; e < graph.EdgeCount; e++)
            {
                if (graph.IsOneway(e) && Vector3.Distance(graph.EdgeStart(e), graph.EdgeEnd(e)) > 40f)
                {
                    edge = e;

                    break;
                }
            }

            Assert.GreaterOrEqual(edge, 0, "O mapa tem ruas de sentido único.");
            Vector3 flow = graph.EdgeDirection(edge);
            Vector3 mid = (graph.EdgeStart(edge) + graph.EdgeEnd(edge)) * 0.5f;
            var body = Flow.Bike.GetComponent<Rigidbody>();
            Flow.Bike.SetLocked(false);

            // Com a mão: sem contramão, mesmo depois de vários segundos.
            Flow.Bike.Teleport(new Vector3(mid.x, 0.1f, mid.z), YawOf(flow));
            body.linearVelocity = flow * 4f;
            yield return new WaitForFixedUpdate();
            Flow.Monitor.ResetState();

            for (int i = 0; i < 15; i++)
            {
                Flow.Monitor.Sample(0.1f);
            }

            Assert.IsFalse(Flow.Monitor.WrongWay);
            Assert.IsNull(Flow.Monitor.CurrentInfraction);

            // Contra a mão: detectado e, passado 1 s, é infração visível à polícia.
            Flow.Bike.Teleport(new Vector3(mid.x, 0.1f, mid.z), YawOf(-flow));
            body.linearVelocity = -flow * 4f;
            yield return new WaitForFixedUpdate();
            Flow.Monitor.ResetState();

            for (int i = 0; i < 15; i++)
            {
                Flow.Monitor.Sample(0.1f);
            }

            Assert.IsTrue(Flow.Monitor.WrongWay, "Contra a mão da rua de sentido único.");
            Assert.AreEqual(Infracao.Contramao, Flow.Monitor.CurrentInfraction);
        }

        // ---------- Polícia ----------

        private static string GetPath(Transform t)
        {
            return t.parent == null ? t.name : GetPath(t.parent) + "/" + t.name;
        }

        [UnityTest]
        public IEnumerator PatrolOnlySeesWhatIsNearAndInTheOpen()
        {
            PoliceUnit patrol = Flow.Patrol;
            Assert.IsNotNull(patrol, "A cidade tem uma viatura de ronda.");
            Assert.IsTrue(patrol.HasPatrolRoute);

            // Os entregadores parados (PontosDirector) também bloqueiam a visão; este teste é do largo aberto.
            Object.FindFirstObjectByType<PontosDirector>().gameObject.SetActive(false);

            // O trânsito em circuito e a própria ronda se mexem: um carro cruzando a linha de visão (ou a viatura andando entre o
            // teleporte e a leitura) fazia o teste falhar de vez em quando. Congela os dois para a linha ser sempre a mesma.
            foreach (TrafficLoop car in Object.FindObjectsByType<TrafficLoop>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                car.gameObject.SetActive(false);
            }

            patrol.enabled = false;
            Vector3 spot = View("largo").Position;
            Flow.Bike.Teleport(new Vector3(spot.x, 0.1f, spot.z), 0f);

            // A viatura é movida pelo Rigidbody a cada passo da física: mexer só no transform deixava a ronda voltar ao trajeto dela
            // antes da leitura (a causa real da oscilação). Posiciona o corpo e deixa a ronda desligada.
            Vector3 patrolPos = spot + new Vector3(0f, 0f, 14f);
            var patrolBody = patrol.GetComponent<Rigidbody>();
            patrolBody.position = patrolPos;
            patrolBody.linearVelocity = Vector3.zero;
            patrolBody.angularVelocity = Vector3.zero;
            patrol.transform.position = patrolPos;
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            string quem = Physics.Linecast(patrol.transform.position + Vector3.up * 1.6f, Flow.Bike.Position + Vector3.up, out RaycastHit bloqueio, ~0, QueryTriggerInteraction.Ignore)
                ? GetPath(bloqueio.collider.transform) : "nada";
            Assert.IsTrue(patrol.CanSee(Flow.Bike.Position, 40f), "No largo aberto, a 14 m, a viatura vê. Bloqueio: " + quem + " bike=" + Flow.Bike.Position + " patrol=" + patrol.transform.position);
            Assert.IsFalse(patrol.CanSee(Flow.Bike.Position, 10f), "Mais longe que o alcance: não vê.");
        }

        [UnityTest]
        public IEnumerator FlagranteOpensAStopPanelAndBothChoicesResolve()
        {
            var random = new ScriptedRandom();
            yield return StartRun(random, false);
            Money before = Flow.Session.Wallet.Balance;

            Flow.ForceFlagrante(Infracao.Contramao);
            Assert.AreEqual(FlowState.Abordagem, Flow.State);
            Assert.AreEqual(AbordagemMotivo.Flagrante, Flow.AbordagemAtual.Motivo);
            Assert.IsTrue(Flow.Patrol.LightsOn, "A viatura acende a sirene.");
            yield return WaitForStopPanel();
            CollectionAssert.AreEqual(new[] { "Parar", "Fugir" }, new System.Collections.Generic.List<string>(Flow.Ui.ModalButtonLabels));

            // Parar: multa de contramão (alugada, CNH em ordem), atraso, volta à corrida.
            Assert.IsTrue(Flow.Ui.ClickModalButton("Parar"));
            Assert.AreEqual(FlowState.AbordagemResultado, Flow.State);
            StringAssert.Contains("Contramão", Flow.Ui.ModalBody);
            Assert.Less(Flow.Session.Wallet.Balance.Cents, before.Cents);
            Assert.Greater(Flow.Session.Cnh.Pontos, 0);
            Assert.IsTrue(Flow.Ui.ClickModalButton("Seguir viagem"));
            Assert.AreEqual(FlowState.DrivingToRestaurant, Flow.State);
            Assert.IsFalse(Flow.Bike.Locked);
            Assert.IsTrue(Flow.Tracker.Running);
            Assert.GreaterOrEqual(Flow.Tracker.ElapsedSeconds, 10f * Flow.SegundosPorMinuto - 0.1f, "O atraso da parada soma ao tempo da corrida.");
        }

        // ---------- Blitz ----------

        [UnityTest]
        public IEnumerator BlitzDayOpensCheckpointsAndNoBlitzDayDoesNot()
        {
            var random = new ScriptedRandom();
            yield return StartRun(random, false);
            Assert.AreEqual(0, Flow.OpenBlitzCount, "Sem sorteio, sem blitz.");

            yield return StartRun(random, true);
            Assert.GreaterOrEqual(Flow.OpenBlitzCount, 1);
            Assert.LessOrEqual(Flow.OpenBlitzCount, 2);
            BlitzSite site = OpenSite();
            Assert.IsTrue(site.Unit.LightsOn, "A viatura da blitz fica de luz piscando.");
            Assert.Greater(Vector3.Distance(site.Position, Flow.Bike.Position), 100f, "A blitz nasce longe da moto: dá tempo de desviar.");
        }

        [UnityTest]
        public IEnumerator BlitzEntryStartsAnAbordagemAndParkingResolvesWithDelayOnly()
        {
            var random = new ScriptedRandom();
            yield return StartRun(random, true);
            BlitzSite site = OpenSite();
            yield return EnterBlitz(site);
            Assert.IsNotNull(Flow.Session.AbordagemAtual, "O Core registrou a abordagem (IniciarBlitz).");
            Assert.AreEqual(AbordagemMotivo.Blitz, Flow.AbordagemAtual.Motivo);
            Assert.IsFalse(Flow.Tracker.Running);

            yield return WaitForStopPanel();
            StringAssert.Contains("Blitz", Flow.Ui.ModalTitle);
            Assert.IsTrue(Flow.Bike.Speed < 1.5f, "A moto parou.");

            Assert.IsTrue(Flow.Ui.ClickModalButton("Parar"));
            Assert.AreEqual(FlowState.AbordagemResultado, Flow.State);
            Assert.AreEqual(AbordagemDesfecho.Liberado, Flow.LastOutcome.Desfecho);
            StringAssert.Contains("Documentos em ordem", Flow.Ui.ModalBody);
            Assert.IsTrue(Flow.Ui.ClickModalButton("Seguir viagem"));
            Assert.AreEqual(FlowState.DrivingToRestaurant, Flow.State);
            Assert.IsTrue(site.Used, "A mesma blitz não aborda duas vezes.");
        }

        [UnityTest]
        public IEnumerator FleeingThatEscapesChasesThenLetsTheRunContinueWithoutAnyMessageAboutTheFine()
        {
            var random = new ScriptedRandom();
            yield return StartRun(random, true);
            Flow.Tuning.ChaseSeconds = 2.5f;
            BlitzSite site = OpenSite();
            yield return EnterBlitz(site);
            yield return WaitForStopPanel();

            random.Value = 0.1; // sorteio do Core: escapa (45%) e a placa da alugada é anotada (30%)
            Assert.IsTrue(Flow.Ui.ClickModalButton("Fugir"));
            random.Value = 0.5;
            Assert.AreEqual(FlowState.Perseguicao, Flow.State);
            Assert.IsTrue(Flow.LastOutcome == null || Flow.LastOutcome.Escapou);
            Assert.AreEqual(PoliceMode.Perseguicao, site.Unit.Mode);
            Assert.IsFalse(Flow.Bike.Locked, "Na fuga o jogador tem o controle.");

            Flow.Bike.GetComponent<BikeInput>().SetVirtual(0f, 1f, 0f);
            yield return new WaitForSeconds(3.2f);
            Flow.Bike.GetComponent<BikeInput>().SetVirtual(0f, 0f, 0f);

            Assert.AreEqual(FlowState.DrivingToRestaurant, Flow.State, "Escapou: a corrida continua.");
            Assert.AreNotEqual(PoliceMode.Perseguicao, site.Unit.Mode);
            Assert.GreaterOrEqual(Flow.Session.MultasPendentes.Count, 1, "Placa anotada: a multa chega depois (Core), sem aviso agora.");
            Assert.AreEqual(0, Flow.Session.MultasPagas.Count);
        }

        [UnityTest]
        public IEnumerator FleeingThatGetsCaughtSendsTheMotoToTheYardAndTheNextDayOffersToRetrieveIt()
        {
            var random = new ScriptedRandom();
            yield return StartRun(random, true);
            Flow.Session.Wallet.Credit(1, LedgerCategory.Bonus, Money.Reais(1000), "teste: dinheiro para o pátio");
            BlitzSite site = OpenSite();
            yield return EnterBlitz(site);
            yield return WaitForStopPanel();

            random.Value = 0.9; // não escapa
            Assert.IsTrue(Flow.Ui.ClickModalButton("Fugir"));
            random.Value = 0.5;
            Assert.AreEqual(FlowState.Perseguicao, Flow.State);

            for (int i = 0; i < 900 && Flow.State == FlowState.Perseguicao; i++)
            {
                yield return null;
            }

            Assert.AreEqual(FlowState.MotoRetida, Flow.State, "Pego: moto levada ao pátio e o dia acaba.");
            Assert.AreEqual(MotoStatus.NoPatio, Flow.Session.MotoStatus);
            StringAssert.Contains("pátio", Flow.Ui.ModalTitle);
            StringAssert.Contains("Desobediência", Flow.Ui.ModalBody);
            Assert.IsFalse(Flow.Tracker.Running);

            Assert.IsTrue(Flow.Ui.ClickModalButton("Continuar"));
            Assert.AreEqual(FlowState.DayStart, Flow.State);
            Assert.AreEqual(2, Flow.Session.Day);
            StringAssert.Contains("pátio", Flow.Ui.ModalBody);
            CollectionAssert.DoesNotContain(new System.Collections.Generic.List<string>(Flow.Ui.ModalButtonLabels), "Começar turno");
            Assert.IsTrue(Flow.Ui.ClickModalButton("Retirar do pátio"));
            Assert.AreEqual(MotoStatus.Ok, Flow.Session.MotoStatus);
            Assert.AreEqual(3, Flow.Session.Day, "Retirar a moto consome o dia.");
            Assert.AreEqual(FlowState.DayStart, Flow.State);
            Assert.IsTrue(Flow.Ui.ClickModalButton("Começar turno"));
        }

        [UnityTest]
        public IEnumerator BobMotoWithoutMoneyForAnotherEndsTheWeekAsNoMoto()
        {
            var random = new ScriptedRandom();
            Flow.SetProfile(new PerfilJogador { Origem = MotoOrigem.Bob, TemCnh = true, Personagem = Personagem.Marcao });
            yield return StartRun(random, true);
            Assert.AreEqual(MotoOrigem.Bob, Flow.Session.Origem);

            BlitzSite site = OpenSite();
            yield return EnterBlitz(site);
            yield return WaitForStopPanel();
            StringAssert.Contains("bob", Flow.Ui.ModalBody);

            Assert.IsTrue(Flow.Ui.ClickModalButton("Parar"));
            Assert.AreEqual(FlowState.MotoRetida, Flow.State, "Moto bob parada na blitz é apreendida.");
            Assert.AreEqual(MotoStatus.Apreendida, Flow.Session.MotoStatus);
            StringAssert.Contains("apreendida", Flow.Ui.ModalTitle);

            Assert.IsTrue(Flow.Ui.ClickModalButton("Continuar"));
            Assert.AreEqual(FlowState.DayStart, Flow.State);
            Assert.IsTrue(Flow.Ui.ClickModalButton("Encerrar a semana"), "Sem dinheiro para a entrada de outra moto.");
            Assert.AreEqual(FlowState.WeekReport, Flow.State);
            Assert.AreEqual(WeekResult.SemMoto, Flow.WeekReport.Result);
            StringAssert.Contains("Moto: apreendida", Flow.Ui.ModalBody);
            StringAssert.Contains("Blitz no dia 1", Flow.Ui.ModalBody);
            Flow.SetProfile(new PerfilJogador());
        }

        // ---------- Novo jogo ----------

        [UnityTest]
        public IEnumerator NewGameProfileReachesTheSessionAndTheMotoOnScreen()
        {
            Flow.ShowSetup();
            Assert.AreEqual(FlowState.Setup, Flow.State);
            Assert.AreEqual("Novo jogo", Flow.Ui.ModalTitle);

            // Padrão: alugada, com CNH, Titã 160, Jefinho.
            Assert.AreEqual(PersonagemName(Personagem.Jefinho), PersonagemNameInUi());
            Assert.IsTrue(Flow.Ui.ClickModalButton("Personagem"));
            Assert.AreEqual(Personagem.Robsao, Flow.Perfil.Personagem);
            Assert.IsTrue(Flow.Ui.ClickModalButton("Modelo"));
            Assert.AreEqual(MotoModelo.Twister, Flow.Perfil.Modelo);
            StringAssert.Contains("aluguel mais caro", string.Join("|", Flow.Ui.ModalButtonLabels));
            Assert.IsTrue(Flow.Ui.ClickModalButton("Começar"));

            Assert.AreEqual(FlowState.FreeRoam, Flow.State, "Depois do novo jogo o jogador explora livremente.");
            Flow.StartShiftFromMenu();
            Assert.AreEqual(FlowState.DayStart, Flow.State);
            Assert.AreEqual(MotoOrigem.Alugada, Flow.Session.Origem);
            Assert.IsTrue(Flow.Session.Cnh.TemCnh);
            Money extra = Money.Reais((decimal)Flow.Tuning.TornadoRentExtra);
            Assert.AreEqual(new EconomyConfig().WeeklyRent + extra, Flow.Session.Rental.WeeklyRent, "O Tornado 250 custa mais por semana.");
            Assert.AreEqual(MotoModelo.Twister, Flow.Bike.Modelo);
            Assert.AreEqual(Personagem.Robsao, Flow.Bike.GetComponentInChildren<MotoVisual>().Personagem);
            Assert.AreEqual(MotoModelo.Twister, Flow.Bike.GetComponentInChildren<MotoVisual>().Modelo);

            // Bob sem CNH: Titã (o modelo não se aplica), sem aluguel, sem carteira.
            Flow.ShowSetup();
            Assert.IsTrue(Flow.Ui.ClickModalButton("Moto:"));
            Assert.IsTrue(Flow.Ui.ClickModalButton("Tenho CNH"));
            Assert.IsTrue(Flow.Ui.ClickModalButton("Começar"));
            Flow.StartShiftFromMenu();
            Assert.AreEqual(MotoOrigem.Bob, Flow.Session.Origem);
            Assert.IsFalse(Flow.Session.Cnh.TemCnh);
            Assert.IsNull(Flow.Session.Rental);
            Assert.AreEqual(MotoModelo.Titan, Flow.Bike.Modelo, "A moto bob é sempre a Titã 160.");
            StringAssert.Contains("Parcela da moto", Flow.Ui.ModalBody);
            Flow.SetProfile(new PerfilJogador());
            yield return null;
        }

        private static string PersonagemName(Personagem personagem) => MotoCatalogo.Config(personagem).Nome;

        private string PersonagemNameInUi()
        {
            foreach (string label in Flow.Ui.ModalButtonLabels)
            {
                if (label.StartsWith("Personagem: "))
                {
                    return label.Substring("Personagem: ".Length).Replace("  >", string.Empty);
                }
            }

            return string.Empty;
        }
    }
}
