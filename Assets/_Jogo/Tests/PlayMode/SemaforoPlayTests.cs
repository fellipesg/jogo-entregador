using System.Collections;
using System.Collections.Generic;
using Entregador.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Entregador.Game.Tests
{
    /// <summary>Semáforos: o ciclo acende as lentes, os carros param no vermelho e andam no verde, o jogador que avança é pego pelo fotossensor.</summary>
    public sealed class SemaforoPlayTests : SceneFixture
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;

            foreach (GameObject go in _spawned)
            {
                if (go != null)
                {
                    Object.Destroy(go);
                }
            }

            _spawned.Clear();
        }

        private static float YawOf(Vector3 direction) => Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;

        private static SemaforoControlador Find(bool camera)
        {
            foreach (SemaforoControlador c in SemaforoControlador.Lista)
            {
                if (c.Fotossensor == camera && c.Aproximacoes.Length > 0 && c.Aproximacoes[0].Grupo >= 0)
                {
                    return c;
                }
            }

            return null;
        }

        /// <summary>Ponto na pista de chegada do braço, "antes" metros antes da linha.</summary>
        private static Vector3 LaneBefore(SemaforoAproximacao a, float before)
        {
            float lateral = a.LarguraEsquerda > 1f ? 0f : a.LarguraDireita * 0.5f;

            return a.Linha - a.Direcao * before + a.Direita * lateral;
        }

        private IEnumerator StartRun()
        {
            var random = new ScriptedRandom();
            Flow.NewWeek(1, random);
            random.Value = 0.5;
            Assert.IsTrue(Flow.Ui.ClickModalButton("Começar turno"));
            Assert.IsTrue(Flow.Ui.ClickModalButton("Aceitar"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator CityHasWorkingSignalisedIntersectionsWithSomeCameras()
        {
            yield return null;
            Assert.GreaterOrEqual(SemaforoControlador.Lista.Count, 15, "Os nós traffic_signals do OSM viram semáforos.");
            int cameras = 0;

            foreach (SemaforoControlador c in SemaforoControlador.Lista)
            {
                cameras += c.Fotossensor ? 1 : 0;
                Assert.Greater(c.Aproximacoes.Length, 0);

                foreach (SemaforoAproximacao a in c.Aproximacoes)
                {
                    Assert.AreEqual(2, a.Vermelhos.Length, "Foco na coluna e no braço.");
                    Assert.AreEqual(1, a.PedestreVerde.Length);
                }
            }

            Assert.GreaterOrEqual(cameras, 1);
            Assert.LessOrEqual(cameras, SemaforoControlador.Lista.Count / 2);
            Debug.Log($"[Semaforos] {SemaforoControlador.Lista.Count} cruzamentos, {cameras} com fotossensor");
        }

        [UnityTest]
        public IEnumerator LensesLightUpWithTheState()
        {
            SemaforoControlador c = Find(false);
            SemaforoAproximacao a = c.Aproximacoes[0];
            c.Fixar(a.Grupo, SemaforoEstado.Vermelho);
            yield return null;
            Assert.AreEqual(SemaforoEstado.Vermelho, c.Estado(a));
            Assert.AreEqual("SemaforoLente_Vermelho_Acesa", a.Vermelhos[0].sharedMaterial.name);
            Assert.AreEqual("SemaforoLente_Verde_Apagada", a.Verdes[0].sharedMaterial.name);

            c.Fixar(a.Grupo, SemaforoEstado.Verde);
            yield return null;
            Assert.AreEqual("SemaforoLente_Verde_Acesa", a.Verdes[0].sharedMaterial.name);
            Assert.AreEqual("SemaforoLente_Vermelho_Apagada", a.Vermelhos[0].sharedMaterial.name);

            c.Fixar(a.Grupo, SemaforoEstado.Amarelo);
            yield return null;
            Assert.AreEqual("SemaforoLente_Amarelo_Acesa", a.Amarelos[0].sharedMaterial.name);
        }

        [UnityTest]
        public IEnumerator PublicApiReportsStateAndDistanceAhead()
        {
            SemaforoControlador c = Find(false);
            SemaforoAproximacao a = c.Aproximacoes[0];
            c.Fixar(a.Grupo, SemaforoEstado.Vermelho);
            yield return null;

            // Com 29 cruzamentos, outro semáforo pode estar mais perto: só confere o braço escolhido quando é ele que responde.
            Vector3 at = LaneBefore(a, 30f);
            SemaforoLeitura leitura = Semaforo.EstadoPara(at, a.Direcao);
            Assert.IsTrue(leitura.Existe);
            Assert.LessOrEqual(leitura.Distancia, 30.5f, "O mais próximo à frente nunca está mais longe que o braço escolhido.");

            if (leitura.Linha == a.Linha)
            {
                Assert.AreEqual(SemaforoEstado.Vermelho, leitura.Estado);
                Assert.AreEqual(30f, leitura.Distancia, 0.5f);
                Assert.IsTrue(Semaforo.VermelhoAFrente(at, a.Direcao));
            }

            // Sentido contrário: o braço escolhido não pode responder (outro semáforo alinhado nesse sentido pode).
            SemaforoLeitura contrario = Semaforo.EstadoPara(at, -a.Direcao, 20f);
            Assert.IsFalse(contrario.Existe && contrario.Linha == a.Linha, "Indo no sentido contrário o sinal não é para você.");
            Assert.IsTrue(!contrario.Existe || contrario.Distancia <= 20.5f, "Respeita o alcance.");

            // Fora do mapa não há semáforo nenhum, em qualquer direção.
            Vector3 nowhere = new Vector3(9000f, 0f, 9000f);
            Assert.IsFalse(Semaforo.EstadoPara(nowhere, a.Direcao, 80f).Existe, "Fora do alcance.");
            Assert.IsFalse(Semaforo.EstadoPara(nowhere, -a.Direcao, 500f).Existe);
        }

        private TrafficLoop SpawnCar(SemaforoAproximacao a, float before)
        {
            Vector3 start = LaneBefore(a, before);
            start.y = 0.05f;
            var go = new GameObject("CarroDeTeste");
            go.transform.SetPositionAndRotation(start, Quaternion.LookRotation(a.Direcao, Vector3.up));
            var car = go.AddComponent<TrafficLoop>();
            Vector3 end = LaneBefore(a, -40f);
            end.y = 0.05f;
            car.Configure(new[] { start, end, end + a.Direcao * 40f }, 8f, null, 0);
            _spawned.Add(go);

            return car;
        }

        [UnityTest]
        public IEnumerator CarStopsAtTheRedLineAndGoesOnGreen()
        {
            SemaforoControlador c = Find(false);
            SemaforoAproximacao a = c.Aproximacoes[0];
            c.Fixar(a.Grupo, SemaforoEstado.Vermelho);
            TrafficLoop car = SpawnCar(a, 22f);
            yield return new WaitForSeconds(6f);

            float toLine = a.DistanciaAte(car.transform.position);
            Assert.Less(car.CurrentSpeed, 0.2f, "O carro parou no vermelho.");
            Assert.IsTrue(car.WaitingAtSignal);
            Assert.Greater(toLine, 1.5f, "Parou antes da linha (o nariz não passou dela).");
            Assert.Less(toLine, 4.5f);

            c.Fixar(a.Grupo, SemaforoEstado.Verde);
            yield return new WaitForSeconds(3.5f);
            Assert.Less(a.DistanciaAte(car.transform.position), -3f, "No verde o carro andou e cruzou a linha.");
        }

        [UnityTest]
        public IEnumerator CarsQueueBehindTheFirstOneAtRed()
        {
            SemaforoControlador c = Find(false);
            SemaforoAproximacao a = c.Aproximacoes[0];
            c.Fixar(a.Grupo, SemaforoEstado.Vermelho);
            TrafficLoop first = SpawnCar(a, 30f);
            TrafficLoop second = SpawnCar(a, 42f);
            yield return new WaitForSeconds(9f);

            Assert.Less(first.CurrentSpeed, 0.2f);
            Assert.Less(second.CurrentSpeed, 0.2f);
            float gap = a.DistanciaAte(second.transform.position) - a.DistanciaAte(first.transform.position);
            Assert.Greater(gap, 3.5f, "Sem encostar um no outro.");
            Assert.Less(a.DistanciaAte(first.transform.position), a.DistanciaAte(second.transform.position));
            Assert.Greater(a.DistanciaAte(second.transform.position), 0f, "O segundo também ficou antes da linha.");
        }

        [UnityTest]
        public IEnumerator CarThatCannotStopOnAmberKeepsGoing()
        {
            SemaforoControlador c = Find(false);
            SemaforoAproximacao a = c.Aproximacoes[0];
            c.Fixar(a.Grupo, SemaforoEstado.Amarelo);
            TrafficLoop car = SpawnCar(a, 6f);
            yield return new WaitForSeconds(2.5f);
            Assert.Less(a.DistanciaAte(car.transform.position), -2f, "Perto demais para frear no amarelo: passa.");
        }

        [UnityTest]
        public IEnumerator PlayerRunningRedAtAPhotoSensorIsCaptured()
        {
            yield return StartRun();
            SemaforoControlador c = Find(true);
            Assert.IsNotNull(c, "Há ao menos um cruzamento com fotossensor.");
            SemaforoAproximacao a = c.Aproximacoes[0];
            c.Fixar(a.Grupo, SemaforoEstado.Vermelho, 3.0);
            Vector3 start = LaneBefore(a, 7f);
            start.y = 0.1f;
            var body = Flow.Bike.GetComponent<Rigidbody>();
            Flow.Bike.Teleport(start, YawOf(a.Direcao));
            body.linearVelocity = a.Direcao * 9f;
            yield return new WaitForSeconds(1.6f);

            Assert.AreEqual(1, c.Capturas, "O fotossensor capturou.");
            Assert.AreEqual(1, Flow.Tracker.RadarCaptures);
            RunReport report = Flow.Tracker.BuildReport(Flow.Tuning.WorldToGameKm);
            Assert.AreEqual(Infracao.AvancarSinalVermelho, report.Capturas[0]);
            StringAssert.DoesNotContain("sinal", Flow.Ui.ToastMessage.ToLowerInvariant(), "Nenhum aviso na hora: a multa chega dias depois.");
            StringAssert.DoesNotContain("multa", Flow.Ui.ToastMessage.ToLowerInvariant());
        }

        [UnityTest]
        public IEnumerator CrossingOnGreenOrAmberOrWithinGraceIsNotCaptured()
        {
            yield return StartRun();
            SemaforoControlador c = Find(true);
            SemaforoAproximacao a = c.Aproximacoes[0];
            var body = Flow.Bike.GetComponent<Rigidbody>();
            SemaforoEstado[] states = { SemaforoEstado.Verde, SemaforoEstado.Amarelo, SemaforoEstado.Vermelho };
            double[] inside = { 5.0, 1.0, 0.1 };

            for (int i = 0; i < states.Length; i++)
            {
                c.Fixar(a.Grupo, states[i], inside[i]);
                Vector3 start = LaneBefore(a, 7f);
                start.y = 0.1f;
                Flow.Bike.Teleport(start, YawOf(a.Direcao));
                body.linearVelocity = a.Direcao * 9f;
                yield return new WaitForSeconds(1.4f);
                Assert.Less(a.DistanciaAte(Flow.Bike.Position), 0f, "A moto cruzou a linha em " + states[i]);
            }

            Assert.AreEqual(0, c.Capturas);
            Assert.AreEqual(0, Flow.Tracker.RadarCaptures);
        }

        [UnityTest]
        public IEnumerator RedLightWithoutCameraOrPoliceGoesUnnoticed()
        {
            yield return StartRun();
            SemaforoControlador c = Find(false);
            SemaforoAproximacao a = c.Aproximacoes[0];
            c.Fixar(a.Grupo, SemaforoEstado.Vermelho, 3.0);
            var fiscal = Object.FindFirstObjectByType<SemaforoFiscal>();
            Flow.Bike.Teleport(new Vector3(-400f, 0.1f, 400f), 0f);
            Assert.AreEqual(Infracao.AvancarSinalVermelho, fiscal.Cruzou(c, a));
            Assert.AreEqual(0, Flow.Tracker.RadarCaptures);
            Assert.AreEqual(0, fiscal.FlagrantesPolicia);
            Assert.AreEqual(FlowState.DrivingToRestaurant, Flow.State);
        }

        [UnityTest]
        public IEnumerator PoliceNearbyFlagsTheRedLightRunner()
        {
            yield return StartRun();
            SemaforoControlador c = Find(false);
            SemaforoAproximacao a = c.Aproximacoes[0];
            c.Fixar(a.Grupo, SemaforoEstado.Vermelho, 3.0);
            var fiscal = Object.FindFirstObjectByType<SemaforoFiscal>();

            Vector3 spot = View("largo").Position;
            Flow.Bike.Teleport(new Vector3(spot.x, 0.1f, spot.z), 0f);
            Flow.Patrol.BeginPatrol();
            Vector3[] offsets = { new Vector3(0f, 0f, 12f), new Vector3(12f, 0f, 0f), new Vector3(-12f, 0f, 0f), new Vector3(0f, 0f, -12f) };

            foreach (Vector3 offset in offsets)
            {
                Flow.Patrol.transform.position = spot + offset;
                yield return new WaitForFixedUpdate();

                if (Flow.Patrol.CanSee(Flow.Bike.Position, 40f))
                {
                    break;
                }
            }

            Assert.IsTrue(Flow.Patrol.CanSee(Flow.Bike.Position, 40f), "A viatura vê a moto no largo aberto.");
            fiscal.Cruzou(c, a);

            Assert.AreEqual(1, fiscal.FlagrantesPolicia);
            Assert.AreEqual(FlowState.Abordagem, Flow.State);
            Assert.AreEqual(Infracao.AvancarSinalVermelho, Flow.AbordagemAtual.Flagrante);
        }
    }
}
