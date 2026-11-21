using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Entregador.Game.Tests
{
    /// <summary>Rotas no grafo de ruas com sentido único, sem precisar da cena.</summary>
    public sealed class RoadGraphTests
    {
        private GameObject _holder;

        [TearDown]
        public void TearDown()
        {
            if (_holder != null)
            {
                Object.DestroyImmediate(_holder);
            }
        }

        /// <summary>
        /// A (0,0) -> B (100,0) em mão única; a volta legal é B -> C -> D -> A. Os nós C e D ficam a detour metros de altura.
        /// </summary>
        private RoadGraph Build(float detour)
        {
            _holder = new GameObject("Grafo");
            var graph = _holder.AddComponent<RoadGraph>();
            var nodes = new[] { new Vector3(0f, 0f, 0f), new Vector3(100f, 0f, 0f), new Vector3(100f, 0f, detour), new Vector3(0f, 0f, detour) };
            graph.Configure(nodes, new[] { 0, 1, 2, 3 }, new[] { 1, 2, 3, 0 }, new[] { true, false, false, false });

            return graph;
        }

        [Test]
        public void OnewayStreetIsDrivenTheRightWayWhenTheLegalDetourIsShort()
        {
            RoadGraph graph = Build(30f);
            Assert.AreEqual(100f, graph.RouteLength(new Vector3(0f, 0f, 0f), new Vector3(100f, 0f, 0f)), 0.5f, "Na mão certa a rota é direta.");

            // B até A: contra a mão são 100 m (custo 220); a volta legal tem 160 m. O GPS prefere a volta legal.
            float back = graph.RouteLength(new Vector3(100f, 0f, 0f), new Vector3(0f, 0f, 0f));
            Assert.AreEqual(160f, back, 0.5f, "O GPS deveria dar a volta pela mão certa.");
        }

        [Test]
        public void WrongWayIsUsedOnlyWhenTheLegalDetourIsMuchLonger()
        {
            RoadGraph graph = Build(400f);
            var route = new List<Vector3>();
            float back = graph.FindRoute(new Vector3(100f, 0f, 0f), new Vector3(0f, 0f, 0f), route);

            Assert.AreEqual(100f, back, 0.5f, "A volta legal de 900 m custa mais que andar contra a mão (2,2 x 100 m).");
            Assert.AreEqual(new Vector3(0f, 0f, 0f), route[route.Count - 1]);
        }

        [Test]
        public void RouteStartsAtTheOriginAndEndsAtTheDestination()
        {
            RoadGraph graph = Build(60f);
            var route = new List<Vector3>();
            Vector3 from = new Vector3(20f, 0f, 3f);
            Vector3 to = new Vector3(95f, 0f, 40f);
            float length = graph.FindRoute(from, to, route);

            Assert.AreEqual(from, route[0]);
            Assert.AreEqual(to, route[route.Count - 1]);
            Assert.Greater(length, 0f);
        }
    }
}
