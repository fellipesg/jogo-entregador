using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.TestTools;

namespace Entregador.Game.Tests
{
    /// <summary>O áudio ligado à cena: motor segue o controlador, sirene na perseguição, rádio troca de estação, carros e botões com som.</summary>
    public sealed class AudioPlayTests : SceneFixture
    {
        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey(RadioPlayer.MusicaExternaKey);
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

        [UnityTest]
        public IEnumerator EngineIsPlayingAndFollowsTheController()
        {
            yield return WaitFor(() => AudioDirector.Instance != null && AudioDirector.Instance.Motor != null, 5f);
            AudioDirector director = AudioDirector.Instance;
            Assert.IsNotNull(director, "A diretoria de áudio nasce sozinha.");
            MotoEngineAudio motor = director.Motor;
            Assert.IsNotNull(motor, "O motor da moto ganha áudio.");
            Assert.IsTrue(motor.Tocando, "A fonte do motor está tocando.");

            BikeInput input = Object.FindFirstObjectByType<BikeInput>();
            Flow.Bike.SetLocked(false);
            input.SetVirtual(0f, 1f, 0f);
            yield return new WaitForSeconds(2.5f);

            Assert.IsTrue(motor.Ligado);
            Assert.Greater(Flow.Bike.Rpm, Flow.Bike.Perfil.RpmMarchaLenta + 800f, "A moto acelerou.");
            Assert.AreEqual(Flow.Bike.Rpm, motor.RpmAlvo, 1500f, "O sintetizador segue o giro do controlador.");
            Assert.Greater(motor.ThrottleAlvo, 0.5f);

            input.SetVirtual(0f, 0f, 0f);
            yield return new WaitForSeconds(1.5f);
            Assert.Less(motor.ThrottleAlvo, 0.1f, "Solto o acelerador, o som vai para o freio-motor.");
            input.ClearAll();
        }

        [UnityTest]
        public IEnumerator BrakePlusThrottleAtStandstillRevsToTheLimiter()
        {
            BikeInput input = Object.FindFirstObjectByType<BikeInput>();
            Flow.Bike.SetLocked(false);
            Flow.Bike.Teleport(new Vector3(Flow.Bike.Position.x, 0.1f, Flow.Bike.Position.z), Flow.Bike.Yaw);
            input.SetVirtual(0f, 1f, 1f);
            yield return new WaitForSeconds(1f);

            float limit = Flow.Bike.RpmLimit;
            float max = 0f;
            float min = float.MaxValue;
            float maxSpeed = 0f;
            int samples = 0;

            while (samples < 60)
            {
                yield return new WaitForFixedUpdate();
                max = Mathf.Max(max, Flow.Bike.Rpm);
                min = Mathf.Min(min, Flow.Bike.Rpm);
                maxSpeed = Mathf.Max(maxSpeed, Flow.Bike.SpeedKmh);
                samples++;
            }

            Assert.IsTrue(Flow.Bike.CortandoGiro, "Embreagem puxada.");
            Assert.Less(maxSpeed, 2f, "A moto fica parada.");
            Assert.Greater(max, limit - 60f, "O giro chega ao limitador.");
            Assert.Less(min, limit - 350f, "E quica no corte de ignição.");
            Assert.IsTrue(Flow.Bike.NoLimitador || min < limit - 350f);

            AudioDirector director = AudioDirector.Instance;
            yield return WaitFor(() => director.Motor != null && director.Motor.LimiterAlvo > 0.5f, 2f);
            Assert.AreEqual(1f, director.Motor.LimiterAlvo, "O som liga o corte do limitador.");

            input.SetVirtual(0f, 0f, 1f);
            yield return new WaitForSeconds(1.5f);
            Assert.IsFalse(Flow.Bike.CortandoGiro);
            Assert.Less(Flow.Bike.Rpm, Flow.Bike.Perfil.RpmMarchaLenta + 400f, "Soltou o acelerador: o giro cai.");

            // Soltar o freio girando alto: arrancada, a moto sai. Antes, deixa a moto parar de verdade: o freio sozinho a põe de ré
            // devagar, e a embreagem puxada só vale abaixo de ~3 km/h (era isto, e não o limitador, que fazia o teste oscilar).
            input.ClearAll();

            for (int i = 0; i < 150 && Mathf.Abs(Flow.Bike.SpeedKmh) > 0.5f; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            input.SetVirtual(0f, 1f, 1f);
            yield return new WaitForSeconds(0.4f);

            // A arrancada depende do giro no instante em que o freio solta (o limitador quica a ~13 Hz: no vale a moto sai fraca).
            // Solta sempre no pico do giro, não num instante de relógio que muda com o tempo do quadro.
            for (int i = 0; i < 100 && Flow.Bike.Rpm < limit - 60f; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.GreaterOrEqual(Flow.Bike.Rpm, limit - 60f, "O giro chegou ao pico antes de soltar o freio.");
            input.SetVirtual(0f, 1f, 0f);
            yield return new WaitForSeconds(1.2f);
            Assert.Greater(Flow.Bike.SpeedKmh, 6f, "A arrancada tira a moto do lugar.");
            input.ClearAll();
        }

        [UnityTest]
        public IEnumerator SirenPlaysDuringPursuitAndStopsAfter()
        {
            PoliceUnit patrol = Flow.Patrol;
            Assert.IsNotNull(patrol);
            yield return WaitFor(() => patrol.GetComponentInChildren<SirenAudio>(true) != null, 5f);
            SirenAudio siren = patrol.GetComponentInChildren<SirenAudio>(true);
            Assert.IsNotNull(siren, "A viatura ganha sirene.");
            Assert.IsFalse(siren.Tocando, "Em ronda, sem sirene.");

            patrol.StartChase(Flow.Bike, false);
            yield return WaitFor(() => siren.Tocando, 3f);
            Assert.IsTrue(siren.Tocando, "A sirene toca na perseguição.");
            Assert.IsTrue(siren.Perseguicao);

            patrol.Dismiss();
            yield return WaitFor(() => !siren.Tocando, 4f);
            Assert.IsFalse(siren.Tocando, "Terminada a perseguição, a sirene cala.");
        }

        [UnityTest]
        public IEnumerator CheckpointUnitWithLightsOnIsSilent()
        {
            PoliceUnit unit = Flow.Patrol;
            Assert.IsNotNull(unit);
            yield return WaitFor(() => unit.GetComponentInChildren<SirenAudio>(true) != null, 5f);
            SirenAudio siren = unit.GetComponentInChildren<SirenAudio>(true);
            Assert.IsNotNull(siren);

            unit.BeginCheckpoint();
            Assert.AreEqual(PoliceMode.Parada, unit.Mode);
            Assert.IsTrue(unit.LightsOn, "A blitz fica com a barra de luz ligada.");
            yield return new WaitForSeconds(2f);
            Assert.IsFalse(siren.Tocando, "Blitz tem luz piscando, mas não tem sirene.");
            Assert.IsFalse(unit.SirenActive);
        }

        [UnityTest]
        public IEnumerator RadioSwitchesStationAndFiresTheEvent()
        {
            yield return WaitFor(() => RadioPlayer.Instance != null, 5f);
            RadioPlayer radio = RadioPlayer.Instance;
            Assert.IsNotNull(radio);
            radio.Desligar();

            int events = 0;
            int lastIndex = -2;
            System.Action<int, RadioEstacao> handler = (i, e) =>
            {
                events++;
                lastIndex = i;
            };
            radio.EstacaoMudou += handler;

            radio.Proxima();
            Assert.AreEqual(0, radio.IndiceAtual);
            Assert.AreEqual("Quebrada FM", radio.EstacaoAtual.Nome);
            Assert.AreEqual(1, events);
            Assert.AreEqual(0, lastIndex);
            yield return null;
            yield return null;
            Assert.IsTrue(radio.Tocando, "A faixa provisória toca.");

            radio.Proxima();
            Assert.AreEqual(1, radio.IndiceAtual);
            radio.Anterior();
            Assert.AreEqual(0, radio.IndiceAtual);
            radio.Anterior();
            Assert.AreEqual(-1, radio.IndiceAtual, "Antes da primeira vem o desligado.");
            Assert.IsNull(radio.EstacaoAtual);
            Assert.AreEqual(4, events);

            RadioPlayer.MusicaExterna = true;
            radio.Proxima();
            Assert.AreEqual(-1, radio.IndiceAtual, "Com música externa, o rádio do jogo não liga.");
            RadioPlayer.MusicaExterna = false;
            radio.EstacaoMudou -= handler;
            radio.Desligar();
        }

        [UnityTest]
        public IEnumerator TrafficCarsHumAndButtonsClick()
        {
            yield return WaitFor(() => Object.FindObjectsByType<CarAudio>(FindObjectsSortMode.None).Length > 0, 6f);
            CarAudio[] cars = Object.FindObjectsByType<CarAudio>(FindObjectsSortMode.None);
            Assert.Greater(cars.Length, 0, "Os carros do trânsito ganham som.");
            Assert.IsTrue(cars[0].Zumbido.isPlaying);
            Assert.AreEqual(1f, cars[0].Zumbido.spatialBlend, "3D");

            yield return WaitFor(() => Object.FindObjectsByType<ClickSfx>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length > 0, 4f);
            Assert.Greater(Object.FindObjectsByType<ClickSfx>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length, 0, "Os botões ganham clique.");
            Assert.Greater(Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length, 0);
        }
    }
}
