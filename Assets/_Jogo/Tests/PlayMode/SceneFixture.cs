using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Entregador.Game.Tests
{
    /// <summary>Carrega a cena SantoAmaro e entrega o GameFlow pronto. Base dos testes de PlayMode.</summary>
    public abstract class SceneFixture
    {
        protected GameFlow Flow;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            SceneManager.LoadScene("SantoAmaro");
            yield return null;
            yield return null;

            Flow = Object.FindFirstObjectByType<GameFlow>();
            Assert.IsNotNull(Flow, "GameFlow não encontrado na cena SantoAmaro.");

            // Espera o Start do GameFlow montar a interface e abrir a semana.
            int guard = 0;

            while (Flow.Session == null && guard++ < 60)
            {
                yield return null;
            }

            Assert.IsNotNull(Flow.Session, "A sessão não foi criada.");
        }

        protected static Place Restaurant(GameFlow flow) => flow.CurrentPlan.Restaurant;

        /// <summary>Ponto de vista nomeado gerado pelo CityBuilder (residencial, avenida, largo, cruzamento, curva, restauranteN).</summary>
        protected ViewPoint View(string name)
        {
            ViewPoint point = Flow.Map.FindViewPoint(name);
            Assert.IsNotNull(point, "Ponto de vista não gerado: " + name);

            return point;
        }

        /// <summary>Direção de avanço (plano) de um yaw em graus.</summary>
        protected static Vector3 Heading(float yaw) => Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;

        protected static void TeleportNear(GameFlow flow, Place place)
        {
            flow.Bike.Teleport(new Vector3(place.Position.x, 0.1f, place.Position.z), 0f);
        }
    }
}
