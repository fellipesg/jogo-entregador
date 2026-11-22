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

        /// <summary>Duas ruas desligadas: a principal (0,0)-(100,0)-(100,100) e uma ilha (500,0)-(600,0).</summary>
        private RoadGraph BuildWithIsland()
        {
            _holder = new GameObject("Grafo");
            var graph = _holder.AddComponent<RoadGraph>();
            var nodes = new[] { new Vector3(0f, 0f, 0f), new Vector3(100f, 0f, 0f), new Vector3(100f, 0f, 100f), new Vector3(500f, 0f, 0f), new Vector3(600f, 0f, 0f) };
            graph.Configure(nodes, new[] { 0, 1, 3 }, new[] { 1, 2, 4 }, null, null, new[] { "Rua A", "Rua B", "Rua Ilha" }, null);

            return graph;
        }

        [Test]
        public void TargetOnADisconnectedIslandStillGetsARouteToTheNearestReachableStreet()
        {
            RoadGraph graph = BuildWithIsland();
            Assert.AreEqual(2, graph.ComponentCount);
            Assert.AreEqual(0, graph.ComponentOf(0), "O pedaço maior é o 0.");

            var route = new List<Vector3>();
            bool real = graph.TryFindRoute(new Vector3(10f, 0f, 2f), new Vector3(550f, 0f, 3f), route, out float length);

            Assert.IsTrue(real, "Antes caía numa reta que atravessava prédios.");
            Assert.Greater(route.Count, 2);
            Assert.Less(length, 700f);
            Assert.Less(Vector3.Distance(route[route.Count - 2], new Vector3(100f, 0f, 3f)), 6f, "A rota termina na rua alcançável mais próxima do destino.");
        }

        [Test]
        public void RouteDetailCarriesStreetNamesAndCrossings()
        {
            RoadGraph graph = BuildWithIsland();
            var route = new List<Vector3>();
            var detail = new RoadGraph.RotaDetalhada();

            Assert.IsTrue(graph.TryFindRoute(new Vector3(10f, 0f, 2f), new Vector3(105f, 0f, 90f), route, out _, detail));
            Assert.AreEqual(route.Count, detail.Count);
            Assert.AreEqual("Rua A", detail.RuaSaida[1], "Saindo da rua colada: Rua A.");
            Assert.IsTrue(detail.RuaSaida.Contains("Rua B"), "A rota passa para a Rua B.");
            Assert.IsFalse(detail.Cruzamento.Contains(true), "Num grafo em linha não há cruzamentos.");
        }

        /// <summary>Quadrado de 200 m com mão dupla: A(0,0) B(200,0) C(200,200) D(0,200).</summary>
        private RoadGraph BuildSquare()
        {
            _holder = new GameObject("Grafo");
            var graph = _holder.AddComponent<RoadGraph>();
            var nodes = new[] { new Vector3(0f, 0f, 0f), new Vector3(200f, 0f, 0f), new Vector3(200f, 0f, 200f), new Vector3(0f, 0f, 200f) };
            graph.Configure(nodes, new[] { 0, 1, 2, 3 }, new[] { 1, 2, 3, 0 });

            return graph;
        }

        [Test]
        public void KnowingTheHeadingTheRoutePrefersGoingForwardOverAUTurn()
        {
            RoadGraph graph = BuildSquare();
            var route = new List<Vector3>();
            Vector3 from = new Vector3(100f, 0f, 0f);
            Vector3 to = new Vector3(95f, 0f, 200f);

            graph.TryFindRoute(from, to, route, out _);
            Assert.AreEqual(new Vector3(0f, 0f, 0f), route[2], "Sem rumo, o caminho curto é para oeste (retorno).");

            graph.TryFindRoute(from, to, route, out _, null, Vector3.right);
            Assert.AreEqual(new Vector3(200f, 0f, 0f), route[2], "Indo para leste, segue em frente e dá a volta no quarteirão.");

            graph.TryFindRoute(from, new Vector3(30f, 0f, 0f), route, out float length, null, Vector3.right);
            Assert.Less(length, 100f, "Alvo logo atrás: o retorno é necessário.");
        }

        [Test]
        public void BlockedEdgesAreNeverUsedAndTheTargetSnapsToAnOpenStreet()
        {
            RoadGraph graph = BuildSquare();
            graph.BlockEdge(1);
            Assert.IsTrue(graph.IsBlocked(1));
            Assert.AreEqual(1, graph.BlockedCount);

            var route = new List<Vector3>();
            Assert.IsTrue(graph.TryFindRoute(new Vector3(180f, 0f, 0f), new Vector3(100f, 0f, 200f), route, out float length));
            Assert.Greater(length, 400f, "Dá a volta em vez de passar pela obra.");

            for (int i = 1; i < route.Count; i++)
            {
                Vector3 mid = (route[i - 1] + route[i]) * 0.5f;
                bool onBlocked = Mathf.Abs(mid.x - 200f) < 0.1f && mid.z > 5f && mid.z < 195f;
                Assert.IsFalse(onBlocked, "A rota passou pelo trecho bloqueado.");
            }
        }

        [Test]
        public void BlockNearAndBlockOutsideClosePartsOfTheCity()
        {
            RoadGraph graph = BuildSquare();
            Assert.AreEqual(1, graph.BlockNear(new Vector3(200f, 0f, 100f), 3f), "Barreira no meio da B-C.");
            Assert.AreEqual(3, graph.BlockOutside(150f), "Os três trechos que ainda estavam livres têm alguma ponta além de 150 m.");
            Assert.AreEqual(4, graph.BlockedCount);
        }

        [Test]
        public void ARouteNeverGoesThroughADeadEndNode()
        {
            _holder = new GameObject("Grafo");
            var graph = _holder.AddComponent<RoadGraph>();
            var nodes = new[] { new Vector3(0f, 0f, 0f), new Vector3(100f, 0f, 0f), new Vector3(200f, 0f, 0f), new Vector3(100f, 0f, 80f) };
            graph.Configure(nodes, new[] { 0, 1, 1 }, new[] { 1, 2, 3 });
            Assert.AreEqual(1, graph.NodeDegree(3), "O nó 3 é beco sem saída.");

            var route = new List<Vector3>();
            graph.TryFindRoute(new Vector3(10f, 0f, 1f), new Vector3(190f, 0f, 1f), route, out _);

            foreach (Vector3 p in route)
            {
                Assert.AreNotEqual(nodes[3], p, "Passou pelo beco.");
            }

            graph.TryFindRoute(new Vector3(10f, 0f, 1f), new Vector3(101f, 0f, 70f), route, out _);
            Assert.IsTrue(route.Contains(nodes[1]));
        }
    }
}
