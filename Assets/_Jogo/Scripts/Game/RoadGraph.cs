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
        [SerializeField] private string[] _edgeName = new string[0];
        [SerializeField] private bool[] _edgeRoundabout = new bool[0];
        [SerializeField] private bool[] _edgeBlocked = new bool[0];

        /// <summary>Custo (multiplicador da distância) de andar contra o sentido único.</summary>
        public const float WrongWayPenalty = 2.2f;

        /// <summary>Metros de custo a mais para dar meia-volta quando se conhece o rumo da moto (evita retorno desnecessário).</summary>
        public const float UTurnPenalty = 80f;

        private struct Arc
        {
            public int To;
            public float Cost;
        }

        private List<Arc>[] _arcs;
        private readonly Dictionary<long, int> _edgeBetween = new Dictionary<long, int>();
        private int[] _edgeComponent;
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

        /// <summary>Nome da rua do trecho (vazio se o mapa não tem nome).</summary>
        public string EdgeName(int edge) => edge >= 0 && edge < _edgeName.Length ? _edgeName[edge] ?? string.Empty : string.Empty;

        /// <summary>O trecho faz parte de uma rotatória.</summary>
        public bool IsRoundabout(int edge) => edge >= 0 && edge < _edgeRoundabout.Length && _edgeRoundabout[edge];

        /// <summary>O trecho está bloqueado (obra, barreira, fora da área jogável): o GPS nunca passa por ele.</summary>
        public bool IsBlocked(int edge) => edge >= 0 && edge < _edgeBlocked.Length && _edgeBlocked[edge];

        public int BlockedCount
        {
            get
            {
                int count = 0;

                foreach (bool b in _edgeBlocked)
                {
                    count += b ? 1 : 0;
                }

                return count;
            }
        }

        /// <summary>Bloqueia um trecho (some do cálculo de rotas).</summary>
        public void BlockEdge(int edge)
        {
            if (_edgeBlocked.Length != _edgeFrom.Length)
            {
                System.Array.Resize(ref _edgeBlocked, _edgeFrom.Length);
            }

            if (edge >= 0 && edge < _edgeBlocked.Length)
            {
                _edgeBlocked[edge] = true;
                _arcs = null;
                _edgeComponent = null;
                _lengthCache.Clear();
            }
        }

        /// <summary>Bloqueia todo trecho que passa a menos de radius metros do ponto (uma barreira de obra, por exemplo). Devolve quantos bloqueou.</summary>
        public int BlockNear(Vector3 point, float radius)
        {
            int count = 0;

            for (int e = 0; e < _edgeFrom.Length; e++)
            {
                Vector3 projected = ClosestOnSegment(_nodes[_edgeFrom[e]], _nodes[_edgeTo[e]], point);

                if (!IsBlocked(e) && FlatSqr(projected - point) <= radius * radius)
                {
                    BlockEdge(e);
                    count++;
                }
            }

            return count;
        }

        /// <summary>Bloqueia os trechos com alguma ponta além de half metros do centro (a cidade além das paredes e das barreiras de obra).</summary>
        public int BlockOutside(float half)
        {
            int count = 0;

            for (int e = 0; e < _edgeFrom.Length; e++)
            {
                Vector3 a = _nodes[_edgeFrom[e]];
                Vector3 b = _nodes[_edgeTo[e]];
                bool outside = Mathf.Max(Mathf.Abs(a.x), Mathf.Abs(a.z), Mathf.Abs(b.x), Mathf.Abs(b.z)) > half;

                if (outside && !IsBlocked(e))
                {
                    BlockEdge(e);
                    count++;
                }
            }

            return count;
        }

        /// <summary>Quantos trechos se encontram no nó (mais de 2 é um cruzamento).</summary>
        public int NodeDegree(int node)
        {
            EnsureAdjacency();

            return _arcs[node].Count;
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

            int edge = NearestRoad(position, out Vector3 snapped);
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
        public void Configure(Vector3[] nodes, int[] edgeFrom, int[] edgeTo, bool[] edgeOneway = null, int[] edgeLimitKmh = null,
            string[] edgeName = null, bool[] edgeRoundabout = null)
        {
            _edgeLimit = edgeLimitKmh ?? new int[0];
            _edgeName = edgeName ?? new string[0];
            _edgeRoundabout = edgeRoundabout ?? new bool[0];
            _nodes = nodes;
            _edgeFrom = edgeFrom;
            _edgeTo = edgeTo;
            _edgeOneway = edgeOneway ?? new bool[edgeFrom.Length];
            _arcs = null;
            _edgeComponent = null;
            _edgeBlocked = new bool[edgeFrom.Length];
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

            _edgeBetween.Clear();

            for (int e = 0; e < _edgeFrom.Length; e++)
            {
                if (IsBlocked(e))
                {
                    continue;
                }

                float length = FlatDistance(_nodes[_edgeFrom[e]], _nodes[_edgeTo[e]]);
                _edgeBetween[PairKey(_edgeFrom[e], _edgeTo[e])] = e;
                _arcs[_edgeFrom[e]].Add(new Arc { To = _edgeTo[e], Cost = length });
                _arcs[_edgeTo[e]].Add(new Arc { To = _edgeFrom[e], Cost = length * WrongWay(e) });
            }

            ComputeComponents();
        }

        /// <summary>Quantos pedaços de ruas desligados entre si o grafo tem (1 = tudo conectado). Todas as arestas têm mão dupla (a contramão só custa mais).</summary>
        public int ComponentCount
        {
            get
            {
                EnsureAdjacency();
                int max = -1;

                foreach (int c in _edgeComponent)
                {
                    max = Mathf.Max(max, c);
                }

                return max + 1;
            }
        }

        /// <summary>Pedaço conexo (0, 1, 2...) a que o trecho pertence; -1 se o trecho não existe.</summary>
        public int ComponentOf(int edge)
        {
            EnsureAdjacency();

            return edge >= 0 && edge < _edgeComponent.Length ? _edgeComponent[edge] : -1;
        }

        private void ComputeComponents()
        {
            var parent = new int[_nodes.Length];

            for (int i = 0; i < parent.Length; i++)
            {
                parent[i] = i;
            }

            for (int e = 0; e < _edgeFrom.Length; e++)
            {
                if (IsBlocked(e))
                {
                    continue;
                }

                int a = Find(parent, _edgeFrom[e]);
                int b = Find(parent, _edgeTo[e]);

                if (a != b)
                {
                    parent[a] = b;
                }
            }

            // Numera os pedaços pelo tamanho (o 0 é o maior): é onde o jogador está quase sempre.
            var size = new Dictionary<int, int>();

            for (int e = 0; e < _edgeFrom.Length; e++)
            {
                if (IsBlocked(e))
                {
                    continue;
                }

                int root = Find(parent, _edgeFrom[e]);
                size[root] = size.TryGetValue(root, out int n) ? n + 1 : 1;
            }

            var roots = new List<int>(size.Keys);
            roots.Sort((x, y) => size[y] != size[x] ? size[y].CompareTo(size[x]) : x.CompareTo(y));
            _edgeComponent = new int[_edgeFrom.Length];

            for (int e = 0; e < _edgeFrom.Length; e++)
            {
                _edgeComponent[e] = IsBlocked(e) ? -1 : roots.IndexOf(Find(parent, _edgeFrom[e]));
            }
        }

        private static int Find(int[] parent, int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }

            return i;
        }

        private static long PairKey(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        /// <summary>Multiplicador de custo de andar contra a mão no trecho e (1 se o trecho tem mão dupla).</summary>
        private float WrongWay(int edge) => IsOneway(edge) ? WrongWayPenalty : 1f;

        /// <summary>Trecho de rua mais próximo do ponto, e o ponto projetado nele.</summary>
        public int NearestEdge(Vector3 point, out Vector3 snapped) => NearestEdge(point, -1, out snapped);

        /// <summary>Trecho físico mais próximo, bloqueado ou não (limite de velocidade e contramão valem em qualquer rua).</summary>
        public int NearestRoad(Vector3 point, out Vector3 snapped) => NearestEdge(point, -1, out snapped, true);

        /// <summary>Trecho mais próximo do ponto, só entre os do pedaço conexo indicado (-1 = qualquer um).</summary>
        public int NearestEdge(Vector3 point, int component, out Vector3 snapped, bool includeBlocked = false)
        {
            int best = -1;
            float bestSqr = float.MaxValue;
            snapped = point;

            if (component >= 0)
            {
                EnsureAdjacency();
            }

            for (int e = 0; e < _edgeFrom.Length; e++)
            {
                if ((!includeBlocked && IsBlocked(e)) || (component >= 0 && _edgeComponent[e] != component))
                {
                    continue;
                }

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
            TryFindRoute(from, to, route, out float length);

            return length;
        }

        /// <summary>
        /// Como FindRoute, mas diz se achou caminho pelas ruas (false = a rota devolvida é só a reta de from a to).
        /// Se os dois pontos caem em pedaços desligados do grafo, o destino é colado no trecho mais próximo do pedaço de onde se parte.
        /// </summary>
        public bool TryFindRoute(Vector3 from, Vector3 to, List<Vector3> route, out float length, RotaDetalhada detail = null, Vector3? heading = null)
        {
            detail?.Clear();

            route.Clear();

            if (_nodes.Length == 0 || _edgeFrom.Length == 0)
            {
                route.Add(from);
                route.Add(to);
                length = FlatDistance(from, to);

                return false;
            }

            EnsureAdjacency();

            if (Solve(from, to, route, out length, detail, heading))
            {
                return true;
            }

            route.Clear();
            route.Add(from);
            route.Add(to);
            length = FlatDistance(from, to);
            detail?.Clear();

            return false;
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

        private bool Solve(Vector3 from, Vector3 to, List<Vector3> route, out float length, RotaDetalhada detail, Vector3? heading)
        {
            length = 0f;
            int fromEdge = NearestEdge(from, out Vector3 fromSnap);
            int toEdge = NearestEdge(to, out Vector3 toSnap);

            // Destino num pedaço de ruas desligado do de origem: cola no trecho mais próximo do pedaço da origem (senão não há rota).
            if (_edgeComponent[fromEdge] != _edgeComponent[toEdge])
            {
                toEdge = NearestEdge(to, _edgeComponent[fromEdge], out toSnap);
            }

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
                    // Com o rumo da moto, dar meia-volta custa UTurnPenalty metros a mais: o GPS prefere seguir em frente.
                    float penaltyB = 0f;
                    float penaltyA = 0f;

                    if (heading.HasValue)
                    {
                        Vector3 h = heading.Value;
                        h.y = 0f;
                        Vector3 ab = _nodes[b] - _nodes[a];
                        ab.y = 0f;

                        if (h.sqrMagnitude > 0.01f && ab.sqrMagnitude > 0.01f)
                        {
                            float alignment = Vector3.Dot(h.normalized, ab.normalized);
                            penaltyA = alignment > 0.3f ? UTurnPenalty : 0f;
                            penaltyB = alignment < -0.3f ? UTurnPenalty : 0f;
                        }
                    }

                    Relax(current, b, FlatDistance(fromSnap, _nodes[b]) + penaltyB, distance, previous, heap);
                    Relax(current, a, FlatDistance(fromSnap, _nodes[a]) * WrongWay(fromEdge) + penaltyA, distance, previous, heap);

                    if (fromEdge == toEdge)
                    {
                        Vector3 along = _nodes[b] - _nodes[a];
                        bool forward = along.x * (toSnap.x - fromSnap.x) + along.z * (toSnap.z - fromSnap.z) >= 0f;
                        float turnBack = 0f;

                        if (heading.HasValue)
                        {
                            Vector3 h = heading.Value;
                            Vector3 go = toSnap - fromSnap;
                            turnBack = h.x * go.x + h.z * go.z < -0.3f * Mathf.Sqrt(h.x * h.x + h.z * h.z) * Mathf.Sqrt(FlatSqr(go)) ? UTurnPenalty : 0f;
                        }

                        Relax(current, target, FlatDistance(fromSnap, toSnap) * (forward ? 1f : WrongWay(fromEdge)) + turnBack, distance, previous, heap);
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

            if (detail != null)
            {
                FillDetail(detail, path, source, target, fromEdge, toEdge);
            }

            return true;
        }

        /// <summary>Para cada ponto da rota: a rua do trecho que sai dele, se é cruzamento e se é rotatória (listas paralelas à rota).</summary>
        private void FillDetail(RotaDetalhada detail, List<int> path, int source, int target, int fromEdge, int toEdge)
        {
            detail.Add(EdgeName(fromEdge), false, false);

            for (int j = 0; j < path.Count; j++)
            {
                int node = path[j];
                bool isNode = node != source && node != target;
                int edge;

                if (j + 1 >= path.Count)
                {
                    edge = toEdge;
                }
                else if (node == source)
                {
                    edge = fromEdge;
                }
                else if (path[j + 1] == target)
                {
                    edge = toEdge;
                }
                else
                {
                    edge = _edgeBetween.TryGetValue(PairKey(node, path[j + 1]), out int found) ? found : toEdge;
                }

                detail.Add(j + 1 >= path.Count ? EdgeName(toEdge) : EdgeName(edge), isNode && _arcs[node].Count > 2, IsRoundabout(edge));
            }

            detail.Add(string.Empty, false, false);
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

        /// <summary>Informações por ponto da rota para a navegação: nome da rua que sai do ponto, cruzamento, rotatória.</summary>
        public sealed class RotaDetalhada
        {
            public readonly List<string> RuaSaida = new List<string>();
            public readonly List<bool> Cruzamento = new List<bool>();
            public readonly List<bool> Rotatoria = new List<bool>();

            public int Count => RuaSaida.Count;

            public void Clear()
            {
                RuaSaida.Clear();
                Cruzamento.Clear();
                Rotatoria.Clear();
            }

            public void Add(string rua, bool cruzamento, bool rotatoria)
            {
                RuaSaida.Add(rua);
                Cruzamento.Add(cruzamento);
                Rotatoria.Add(rotatoria);
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
