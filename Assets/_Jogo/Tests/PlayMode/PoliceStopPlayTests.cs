using System.Collections;
using Entregador.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Entregador.Game.Tests
{
    /// <summary>A viatura para a moto como numa abordagem de verdade: encosta atrás, freia e fica parada, sem empurrar nem atropelar.</summary>
    public sealed class PoliceStopPlayTests : SceneFixture
    {
        private static float Flat(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;

            return Vector3.Distance(a, b);
        }

        private static void PlaceUnit(PoliceUnit unit, Vector3 position, float yaw)
        {
            var body = unit.GetComponent<Rigidbody>();
            body.position = position;
            body.rotation = Quaternion.Euler(0f, yaw, 0f);
            unit.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        }

        [UnityTest]
        public IEnumerator PursuitWithThePlayerStoppedNeverComesCloserThanThreeMetersNorMovesTheMoto()
        {
            var random = new ScriptedRandom();
            Flow.NewWeek(1, random);
            Assert.IsTrue(Flow.Ui.ClickModalButton("Começar turno"));
            Assert.IsTrue(Flow.Ui.ClickModalButton("Aceitar"));

            PoliceUnit unit = Flow.Patrol;
            Assert.IsNotNull(unit);
            ViewPoint street = View("avenida");
            Vector3 heading = Heading(street.Yaw);
            Vector3 bikeAt = new Vector3(street.Position.x, 0.1f, street.Position.z);

            Flow.Bike.SetLocked(true);
            Flow.Bike.Teleport(bikeAt, street.Yaw);
            var bikeBody = Flow.Bike.GetComponent<Rigidbody>();
            bikeBody.linearVelocity = Vector3.zero;
            yield return new WaitForFixedUpdate();
            Vector3 stopped = Flow.Bike.Position;

            // A viatura vem de 70 m atrás, em linha com a rua.
            Vector3 start = bikeAt - heading * 70f;
            PlaceUnit(unit, new Vector3(start.x, 0.02f, start.z), street.Yaw);
            unit.StartChase(Flow.Bike, true);

            float minGap = float.MaxValue;
            float maxDisplacement = 0f;
            float topSpeed = 0f;

            for (int i = 0; i < 1000; i++)
            {
                yield return new WaitForFixedUpdate();
                minGap = Mathf.Min(minGap, Flat(unit.transform.position, Flow.Bike.Position));
                maxDisplacement = Mathf.Max(maxDisplacement, Flat(Flow.Bike.Position, stopped));
                topSpeed = Mathf.Max(topSpeed, unit.Speed);
            }

            Assert.Greater(topSpeed, 8f, "A viatura de fato veio atrás da moto.");
            Assert.GreaterOrEqual(minGap, 3f, "A viatura nunca chega a menos de 3 m da moto.");
            Assert.Less(maxDisplacement, 0.3f, "A viatura não empurra a moto.");
            float finalGap = Flat(unit.transform.position, Flow.Bike.Position);
            Assert.That(finalGap, Is.InRange(PoliceUnit.MinBikeDistance - 0.2f, 10.5f), "Para uns 6 a 10 m atrás, a salvo.");
            Assert.Less(unit.Speed, 0.5f, "E fica parada.");
            Assert.IsTrue(unit.LightsOn, "Com a sirene ligada.");
        }

        [UnityTest]
        public IEnumerator FlagranteStopsThePatrolBehindTheMotoAndItLeavesWithoutHittingIt()
        {
            var random = new ScriptedRandom();
            Flow.NewWeek(1, random);
            Assert.IsTrue(Flow.Ui.ClickModalButton("Começar turno"));
            Assert.IsTrue(Flow.Ui.ClickModalButton("Aceitar"));

            PoliceUnit unit = Flow.Patrol;
            ViewPoint street = View("avenida");
            Vector3 heading = Heading(street.Yaw);
            Vector3 bikeAt = new Vector3(street.Position.x, 0.1f, street.Position.z);
            Flow.Bike.SetLocked(false);
            Flow.Bike.Teleport(bikeAt, street.Yaw);
            Flow.Bike.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
            Vector3 start = bikeAt - heading * 55f;
            PlaceUnit(unit, new Vector3(start.x, 0.02f, start.z), street.Yaw);
            yield return new WaitForFixedUpdate();

            Flow.ForceFlagrante(Infracao.Contramao, unit);
            Assert.AreEqual(FlowState.Abordagem, Flow.State);
            Vector3 stopped = Flow.Bike.Position;
            float minGap = float.MaxValue;
            float maxDisplacement = 0f;

            for (int i = 0; i < 700; i++)
            {
                yield return new WaitForFixedUpdate();
                minGap = Mathf.Min(minGap, Flat(unit.transform.position, Flow.Bike.Position));
                maxDisplacement = Mathf.Max(maxDisplacement, Flat(Flow.Bike.Position, stopped));
            }

            Assert.GreaterOrEqual(minGap, 3f);
            Assert.Less(maxDisplacement, 0.3f);
            Assert.AreEqual(PoliceMode.Parada, unit.Mode, "A viatura parou atrás da moto até a abordagem acabar.");
            Assert.Less(unit.Speed, 0.2f);

            // Depois de resolver, a viatura segue seu caminho e continua a pelo menos 3 m da moto.
            Assert.IsTrue(Flow.AbordagemPanelShown || Flow.Ui.ModalVisible);
            Assert.IsTrue(Flow.Ui.ClickModalButton("Parar"));
            Assert.IsTrue(Flow.Ui.ClickModalButton("Seguir viagem"));
            minGap = float.MaxValue;

            for (int i = 0; i < 300; i++)
            {
                yield return new WaitForFixedUpdate();
                minGap = Mathf.Min(minGap, Flat(unit.transform.position, Flow.Bike.Position));
            }

            Assert.GreaterOrEqual(minGap, 3f, "Ao ir embora a viatura não bate na moto.");
        }
    }
}
