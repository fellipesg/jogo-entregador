using System.Collections.Generic;
using UnityEngine;

namespace Entregador.Game
{
    /// <summary>
    /// Grafo das ruas: cada nó é um cruzamento (ou um vértice de curva), cada aresta é um trecho de rua, com sentido único opcional
    /// (a aresta de from para to é a mão certa). Calcula o caminho mais curto (Dijkstra com heap) entre dois pontos quaisquer, que
    /// são "colados" no trecho de rua mais próximo. O GPS respeita a mão: andar contra ela custa WrongWayPenalty vezes a distância,
    /// então só vale a pena quando o desvio legal é bem maior (nos becos de mão única que a moto não consegue contornar).
    /// O comprimento devolvido é sempre em metros reais.
    /// </summary>
    public sealed class RoadGraph : MonoBehaviour
    {
        [SerializeField] private Vector3[] _nodes = new Vector3[0];
        [SerializeField] private int[] _edgeFrom = new int[0];
        [SerializeField] private int[] _edgeTo = new int[0];
        [SerializeField] private bool[] _edgeOneway = new bool[0];
        [SerializeField] private int[] _edgeLimit = new int[0];

        /// <summary>Custo (multiplicador da distância) de andar contra o sentido único.</summary>
        public const float WrongWayPenalty = 2.2f;

        private struct Arc
        {
            public int To;
            public float Cost;
        }

        private List<Arc>[] _arcs;
        private readonly Dictionary<(Vector3, Vector3), float> _lengthCache = new Dictionary<(Vector3, Vector3), float>();

        public int NodeCount => _nodes.Length;

        public int EdgeCount => _edgeFrom.Length;

        public Vector3 NodePosition(int index) => _nodes[index];

        public bool IsOneway(int edge) => edge >= 0 && edge < _edgeOneway.Length && _edgeOneway[edge];

        /// <summary>Limite de velocidade (km/h) do trecho. 50 se o gerador não informou.</summary>
        public int SpeedLimitKmh(int edge) => edge >= 0 && edge < _edgeLimit.Length && _edgeLimit[edge] > 0 ? _edgeLimit[edge] : DefaultLimitKmh;

        public const int DefaultLimitKmh = 50;

        /// <summary>Direção (plano, unitária) da mão certa do trecho: de from para to.</summary>
        public Vector3 EdgeDirection(int edge)
        {
            Vector3 d = _nodes[_edgeTo[edge]] - _nodes[_edgeFrom[edge]];
            d.y = 0f;

            return d.normalized;
        }

        public Vector3 EdgeStart(int edge) => _nodes[_edgeFrom[edge]];

        public Vector3 EdgeEnd(int edge) => _nodes[_edgeTo[edge]];

        /// <summary>
        /// Informações da rua sob a moto: trecho mais próximo, limite de velocidade e se ela anda contra a mão num trecho de sentido único.
        /// Só considera contramão quando a moto está rente ao trecho (menos de maxDistance) e anda alinhada com ele (cruzar a rua não conta).
        /// </summary>
        public RoadSample Sample(Vector3 position, Vector3 heading, float maxDistance = 9f)
        {
            var sample = new RoadSample { Edge = -1, LimitKmh = DefaultLimitKmh };

            if (_edgeFrom.Length == 0)
            {
                return sample;
            }

            int edge = NearestEdge(position, out Vector3 snapped);
            float distance = Mathf.Sqrt(FlatSqr(snapped - position));

            if (edge < 0 || distance > maxDistance)
            {
                return sample;
            }

            sample.Edge = edge;
            sample.Distance = distance;
            sample.LimitKmh = SpeedLimitKmh(edge);
            sample.Oneway = IsOneway(edge);
            heading.y = 0f;

            if (sample.Oneway && heading.sqrMagnitude > 0.01f)
            {
                float alignment = Vector3.Dot(heading.normalized, EdgeDirection(edge));
                sample.WrongWay = alignment < -0.8f;
            }

            return sample;
        }

        /// <summary>Resultado de RoadGraph.Sample.</summary>
        public struct RoadSample
        {
            public int Edge;
            public float Distance;
            public int LimitKmh;
            public bool Oneway;
            public bool WrongWay;
        }

        /// <summary>Usado pelo gerador da cidade. edgeOneway (opcional): true = só de from para to.</summary>
        public void Configure(Vector3[] nodes, int[] edgeFrom, int[] edgeTo, bool[] edgeOneway = null, int[] edgeLimitKmh = null)
        {
            _edgeLimit = edgeLimitKmh ?? new int[0];
            _nodes = nodes;
            _edgeFrom = edgeFrom;
            _edgeTo = edgeTo;
            _edgeOneway = edgeOneway ?? new bool[edgeFrom.Length];
            _arcs = null;
            _lengthCache.Clear();
        }

        private void EnsureAdjacency()
        {
            if (_arcs != null)
            {
                return;
            }

            _arcs = new List<Arc>[_nodes.Length];

            for (int i = 0; i < _nodes.Length; i++)
            {
                _arcs[i] = new List<Arc>(3);
            }

            for (int e = 0; e < _edgeFrom.Length; e++)
            {
                float length = FlatDistance(_nodes[_edgeFrom[e]], _nodes[_edgeTo[e]]);
                _arcs[_edgeFrom[e]].Add(new Arc { To = _edgeTo[e], Cost = length });
                _arcs[_edgeTo[e]].Add(new Arc { To = _edgeFrom[e], Cost = length * WrongWay(e) });
            }
        }

        /// <summary>Multiplicador de custo de andar contra a mão no trecho e (1 se o trecho tem mão dupla).</summary>
        private float WrongWay(int edge) => IsOneway(edge) ? WrongWayPenalty : 1f;

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
        /// Rota de from até to, passando pelas ruas, preferindo a mão certa. A lista devolvida começa em from e termina em to.
        /// Devolve o comprimento total em metros (a distância reta se o grafo estiver vazio ou desconexo).
        /// </summary>
        public float FindRoute(Vector3 from, Vector3 to, List<Vector3> route)
        {
            route.Clear();

            if (_nodes.Length == 0 || _edgeFrom.Length == 0)
            {
                route.Add(from);
                route.Add(to);

                return FlatDistance(from, to);
            }

            EnsureAdjacency();

            if (Solve(from, to, route, out float length))
            {
                return length;
            }

            route.Clear();
            route.Add(from);
            route.Add(to);

            return FlatDistance(from, to);
        }

        /// <summary>Só o comprimento da rota, em metros (com cache: restaurantes e endereços são pontos fixos).</summary>
        public float RouteLength(Vector3 from, Vector3 to)
        {
            if (_lengthCache.TryGetValue((from, to), out float cached))
            {
                return cached;
            }

            float length = FindRoute(from, to, new List<Vector3>());
            _lengthCache[(from, to)] = length;

            return length;
        }

        private bool Solve(Vector3 from, Vector3 to, List<Vector3> route, out float length)
        {
            length = 0f;
            int fromEdge = NearestEdge(from, out Vector3 fromSnap);
            int toEdge = NearestEdge(to, out Vector3 toSnap);

            // Nós virtuais: N = origem colada na rua, N+1 = destino colado na rua.
            int source = _nodes.Length;
            int target = _nodes.Length + 1;
            int total = _nodes.Length + 2;
            var distance = new float[total];
            var previous = new int[total];

            for (int i = 0; i < total; i++)
            {
                distance[i] = float.PositiveInfinity;
                previous[i] = -1;
            }

            var heap = new MinHeap();
            distance[source] = 0f;
            heap.Push(0f, source);

            while (heap.Count > 0)
            {
                heap.Pop(out float d, out int current);

                if (d > distance[current])
                {
                    continue;
                }

                if (current == target)
                {
                    break;
                }

                if (current == source)
                {
                    int a = _edgeFrom[fromEdge];
                    int b = _edgeTo[fromEdge];
                    Relax(current, b, FlatDistance(fromSnap, _nodes[b]), distance, previous, heap);
                    Relax(current, a, FlatDistance(fromSnap, _nodes[a]) * WrongWay(fromEdge), distance, previous, heap);

                    if (fromEdge == toEdge)
                    {
                        Vector3 along = _nodes[b] - _nodes[a];
                        bool forward = along.x * (toSnap.x - fromSnap.x) + along.z * (toSnap.z - fromSnap.z) >= 0f;
                        Relax(current, target, FlatDistance(fromSnap, toSnap) * (forward ? 1f : WrongWay(fromEdge)), distance, previous, heap);
                    }

                    continue;
                }

                foreach (Arc arc in _arcs[current])
                {
                    Relax(current, arc.To, arc.Cost, distance, previous, heap);
                }

                // Chegando ao trecho do destino: de from para to é a mão certa; de to para from é contra a mão.
                if (current == _edgeFrom[toEdge])
                {
                    Relax(current, target, FlatDistance(_nodes[current], toSnap), distance, previous, heap);
                }

                if (current == _edgeTo[toEdge])
                {
                    Relax(current, target, FlatDistance(_nodes[current], toSnap) * WrongWay(toEdge), distance, previous, heap);
                }
            }

            if (float.IsPositiveInfinity(distance[target]))
            {
                return false;
            }

            var path = new List<int>();

            for (int node = target; node >= 0; node = previous[node])
            {
                path.Add(node);
            }

            path.Reverse();
            route.Clear();
            route.Add(from);

            foreach (int node in path)
            {
                route.Add(node == source ? fromSnap : node == target ? toSnap : _nodes[node]);
            }

            route.Add(to);

            for (int i = 1; i < route.Count; i++)
            {
                length += FlatDistance(route[i - 1], route[i]);
            }

            return true;
        }

        private static void Relax(int from, int to, float cost, float[] distance, int[] previous, MinHeap heap)
        {
            float candidate = distance[from] + cost;

            if (candidate < distance[to])
            {
                distance[to] = candidate;
                previous[to] = from;
                heap.Push(candidate, to);
            }
        }

        /// <summary>Heap binário mínimo de (chave, nó); entradas repetidas são descartadas ao sair (Dijkstra preguiçoso).</summary>
        private sealed class MinHeap
        {
            private readonly List<float> _keys = new List<float>();
            private readonly List<int> _values = new List<int>();

            public int Count => _keys.Count;

            public void Push(float key, int value)
            {
                _keys.Add(key);
                _values.Add(value);
                int i = _keys.Count - 1;

                while (i > 0)
                {
                    int parent = (i - 1) / 2;

                    if (_keys[parent] <= _keys[i])
                    {
                        break;
                    }

                    Swap(parent, i);
                    i = parent;
                }
            }

            public void Pop(out float key, out int value)
            {
                key = _keys[0];
                value = _values[0];
                int last = _keys.Count - 1;
                _keys[0] = _keys[last];
                _values[0] = _values[last];
                _keys.RemoveAt(last);
                _values.RemoveAt(last);
                int i = 0;

                while (true)
                {
                    int left = i * 2 + 1;
                    int right = left + 1;
                    int smallest = i;

                    if (left < _keys.Count && _keys[left] < _keys[smallest])
                    {
                        smallest = left;
                    }

                    if (right < _keys.Count && _keys[right] < _keys[smallest])
                    {
                        smallest = right;
                    }

                    if (smallest == i)
                    {
                        break;
                    }

                    Swap(smallest, i);
                    i = smallest;
                }
            }

            private void Swap(int a, int b)
            {
                (_keys[a], _keys[b]) = (_keys[b], _keys[a]);
                (_values[a], _values[b]) = (_values[b], _values[a]);
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
