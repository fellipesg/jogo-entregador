using System.Collections.Generic;
using UnityEngine;

namespace Entregador.Game
{
    /// <summary>
    /// Grafo das ruas: cada nó é um cruzamento, cada aresta é um trecho de rua. Calcula o caminho mais curto (Dijkstra)
    /// entre dois pontos quaisquer, que são "colados" no trecho de rua mais próximo.
    /// </summary>
    public sealed class RoadGraph : MonoBehaviour
    {
        [SerializeField] private Vector3[] _nodes = new Vector3[0];
        [SerializeField] private int[] _edgeFrom = new int[0];
        [SerializeField] private int[] _edgeTo = new int[0];

        private List<int>[] _adjacency;

        public int NodeCount => _nodes.Length;

        public int EdgeCount => _edgeFrom.Length;

        public Vector3 NodePosition(int index) => _nodes[index];

        /// <summary>Usado pelo gerador da cidade.</summary>
        public void Configure(Vector3[] nodes, int[] edgeFrom, int[] edgeTo)
        {
            _nodes = nodes;
            _edgeFrom = edgeFrom;
            _edgeTo = edgeTo;
            _adjacency = null;
        }

        private void EnsureAdjacency()
        {
            if (_adjacency != null)
            {
                return;
            }

            _adjacency = new List<int>[_nodes.Length];

            for (int i = 0; i < _adjacency.Length; i++)
            {
                _adjacency[i] = new List<int>(4);
            }

            for (int e = 0; e < _edgeFrom.Length; e++)
            {
                _adjacency[_edgeFrom[e]].Add(e);
                _adjacency[_edgeTo[e]].Add(e);
            }
        }

        /// <summary>Trecho de rua mais próximo do ponto, e o ponto projetado nele.</summary>
        public int NearestEdge(Vector3 point, out Vector3 snapped)
        {
            int best = -1;
            float bestSqr = float.MaxValue;
            snapped = point;

            for (int e = 0; e < _edgeFrom.Length; e++)
            {
                Vector3 a = _nodes[_edgeFrom[e]];
                Vector3 b = _nodes[_edgeTo[e]];
                Vector3 projected = ClosestOnSegment(a, b, point);
                float sqr = FlatSqr(projected - point);

                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = e;
                    snapped = projected;
                }
            }

            return best;
        }

        public int NearestNode(Vector3 point)
        {
            int best = -1;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < _nodes.Length; i++)
            {
                float sqr = FlatSqr(_nodes[i] - point);

                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>
        /// Rota de from até to, passando pelas ruas. A lista devolvida começa em from e termina em to.
        /// Devolve o comprimento total em metros (0 se o grafo estiver vazio).
        /// </summary>
        public float FindRoute(Vector3 from, Vector3 to, List<Vector3> route)
        {
            route.Clear();
            EnsureAdjacency();

            if (_nodes.Length == 0)
            {
                route.Add(from);
                route.Add(to);

                return FlatDistance(from, to);
            }

            int fromEdge = NearestEdge(from, out Vector3 fromSnap);
            int toEdge = NearestEdge(to, out Vector3 toSnap);

            // Nós virtuais: N = origem colada na rua, N+1 = destino colado na rua.
            int source = _nodes.Length;
            int target = _nodes.Length + 1;
            int total = _nodes.Length + 2;

            var distance = new float[total];
            var previous = new int[total];
            var done = new bool[total];

            for (int i = 0; i < total; i++)
            {
                distance[i] = float.PositiveInfinity;
                previous[i] = -1;
            }

            distance[source] = 0f;

            for (int step = 0; step < total; step++)
            {
                int current = -1;
                float best = float.PositiveInfinity;

                for (int i = 0; i < total; i++)
                {
                    if (!done[i] && distance[i] < best)
                    {
                        best = distance[i];
                        current = i;
                    }
                }

                if (current < 0 || current == target)
                {
                    break;
                }

                done[current] = true;
                Relax(current, source, target, fromEdge, toEdge, fromSnap, toSnap, distance, previous);
            }

            var path = new List<int>();

            for (int node = target; node >= 0; node = previous[node])
            {
                path.Add(node);
            }

            path.Reverse();
            route.Add(from);

            foreach (int node in path)
            {
                route.Add(node == source ? fromSnap : node == target ? toSnap : _nodes[node]);
            }

            route.Add(to);

            float length = 0f;

            for (int i = 1; i < route.Count; i++)
            {
                length += FlatDistance(route[i - 1], route[i]);
            }

            return length;
        }

        /// <summary>Só o comprimento da rota, em metros.</summary>
        public float RouteLength(Vector3 from, Vector3 to)
        {
            return FindRoute(from, to, new List<Vector3>());
        }

        private void Relax(int current, int source, int target, int fromEdge, int toEdge, Vector3 fromSnap, Vector3 toSnap, float[] distance, int[] previous)
        {
            if (current == source)
            {
                Link(current, _edgeFrom[fromEdge], FlatDistance(fromSnap, _nodes[_edgeFrom[fromEdge]]), distance, previous);
                Link(current, _edgeTo[fromEdge], FlatDistance(fromSnap, _nodes[_edgeTo[fromEdge]]), distance, previous);

                if (fromEdge == toEdge)
                {
                    Link(current, target, FlatDistance(fromSnap, toSnap), distance, previous);
                }

                return;
            }

            foreach (int edge in _adjacency[current])
            {
                int other = _edgeFrom[edge] == current ? _edgeTo[edge] : _edgeFrom[edge];
                Link(current, other, FlatDistance(_nodes[current], _nodes[other]), distance, previous);
            }

            if (current == _edgeFrom[toEdge] || current == _edgeTo[toEdge])
            {
                Link(current, target, FlatDistance(_nodes[current], toSnap), distance, previous);
            }
        }

        private static void Link(int from, int to, float cost, float[] distance, int[] previous)
        {
            float candidate = distance[from] + cost;

            if (candidate < distance[to])
            {
                distance[to] = candidate;
                previous[to] = from;
            }
        }

        private static Vector3 ClosestOnSegment(Vector3 a, Vector3 b, Vector3 p)
        {
            Vector3 ab = b - a;
            float lengthSqr = FlatSqr(ab);

            if (lengthSqr < 0.0001f)
            {
                return a;
            }

            float t = Mathf.Clamp01(((p.x - a.x) * ab.x + (p.z - a.z) * ab.z) / lengthSqr);

            return a + ab * t;
        }

        private static float FlatSqr(Vector3 v) => v.x * v.x + v.z * v.z;

        private static float FlatDistance(Vector3 a, Vector3 b) => Mathf.Sqrt(FlatSqr(a - b));
    }
}
