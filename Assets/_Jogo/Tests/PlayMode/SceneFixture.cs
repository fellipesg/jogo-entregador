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

        protected static void TeleportNear(GameFlow flow, Place place)
        {
            flow.Bike.Teleport(new Vector3(place.Position.x, 0.1f, place.Position.z), 0f);
        }
    }
}
