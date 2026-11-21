using System.Collections.Generic;
using UnityEngine;

namespace Entregador.Editor
{
    /// <summary>Malha em listas simples (posições, normais, UV0, pesos de osso e triângulos por submalha), a entrada e a saída do decimador.</summary>
    public sealed class MeshData
    {
        public List<Vector3> Pos = new List<Vector3>();
        public List<Vector3> Nrm = new List<Vector3>();
        public List<Vector2> Uv = new List<Vector2>();
        public List<BoneWeight> Weights;

        /// <summary>Triângulos (3 índices por triângulo) de cada submalha.</summary>
        public List<List<int>> Subs = new List<List<int>>();

        public int Triangles
        {
            get
            {
                int total = 0;

                foreach (List<int> sub in Subs)
                {
                    total += sub.Count / 3;
                }

                return total;
            }
        }

        /// <summary>Soma de duas malhas (índices de submalha mantidos; a segunda pode ter menos submalhas).</summary>
        public void Append(MeshData other, int[] subMap = null)
        {
            int offset = Pos.Count;
            Pos.AddRange(other.Pos);
            Nrm.AddRange(other.Nrm);
            Uv.AddRange(other.Uv);

            if (Weights != null && other.Weights != null)
            {
                Weights.AddRange(other.Weights);
            }

            for (int s = 0; s < other.Subs.Count; s++)
            {
                int target = subMap != null ? subMap[s] : s;

                while (Subs.Count <= target)
                {
                    Subs.Add(new List<int>());
                }

                foreach (int i in other.Subs[s])
                {
                    Subs[target].Add(i + offset);
                }
            }
        }

        /// <summary>Junta vértices iguais (posição, normal e UV; pesos de osso iguais) e tira submalhas vazias.</summary>
        public MeshData Weld()
        {
            var result = new MeshData { Weights = Weights != null ? new List<BoneWeight>() : null };
            var map = new Dictionary<(int, int, int, int, int, int, int, int), int>();

            foreach (List<int> sub in Subs)
            {
                if (sub.Count == 0)
                {
                    continue;
                }

                var list = new List<int>(sub.Count);

                foreach (int i in sub)
                {
                    Vector3 p = Pos[i];
                    Vector3 n = Nrm[i];
                    Vector2 uv = Uv[i];
                    var key = (Mathf.RoundToInt(p.x * 10000f), Mathf.RoundToInt(p.y * 10000f), Mathf.RoundToInt(p.z * 10000f), Mathf.RoundToInt(n.x * 100f), Mathf.RoundToInt(n.y * 100f) * 1000 + Mathf.RoundToInt(n.z * 100f), Mathf.RoundToInt(uv.x * 10000f), Mathf.RoundToInt(uv.y * 10000f), Weights != null ? Weights[i].boneIndex0 * 1000 + Weights[i].boneIndex1 : 0);

                    if (!map.TryGetValue(key, out int index))
                    {
                        index = result.Pos.Count;
                        map[key] = index;
                        result.Pos.Add(p);
                        result.Nrm.Add(n);
                        result.Uv.Add(uv);
                        result.Weights?.Add(Weights[i]);
                    }

                    list.Add(index);
                }

                result.Subs.Add(list);
            }

            return result;
        }

        /// <summary>Lê uma malha (a lista de pesos só vem se ela tiver esqueleto).</summary>
        public static MeshData FromMesh(Mesh mesh)
        {
            var data = new MeshData();
            data.Pos.AddRange(mesh.vertices);
            Vector3[] normals = mesh.normals;
            Vector2[] uvs = mesh.uv;

            for (int i = 0; i < data.Pos.Count; i++)
            {
                data.Nrm.Add(normals.Length > i ? normals[i] : Vector3.up);
                data.Uv.Add(uvs.Length > i ? uvs[i] : Vector2.zero);
            }

            if (mesh.boneWeights.Length == data.Pos.Count)
            {
                data.Weights = new List<BoneWeight>(mesh.boneWeights);
            }

            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                data.Subs.Add(new List<int>(mesh.GetTriangles(s)));
            }

            return data;
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = Pos.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(Pos);
            mesh.SetNormals(Nrm);
            mesh.SetUVs(0, Uv);

            if (Weights != null && Weights.Count == Pos.Count)
            {
                mesh.boneWeights = Weights.ToArray();
            }

            mesh.subMeshCount = Subs.Count;

            for (int s = 0; s < Subs.Count; s++)
            {
                mesh.SetTriangles(Subs[s], s, false);
            }

            mesh.RecalculateBounds();

            return mesh;
        }
    }

    /// <summary>
    /// Decimador de malhas por colapso de arestas com métrica de erro quádrica (Garland-Heckbert). As posições são soldadas
    /// por vizinhança (costuras de UV e de normais não impedem o colapso) mas cada canto de triângulo guarda a própria normal,
    /// UV e pesos de osso, então o sombreamento e a textura continuam os da malha original. Bordas abertas e fronteiras entre
    /// submalhas (materiais) recebem um plano de proteção, para a silhueta e as janelas não derreterem. Funciona em malhas
    /// com esqueleto (os pesos seguem junto).
    /// </summary>
    public static class MeshDecimator
    {
        private struct Quadric
        {
            public double A2, AB, AC, AD, B2, BC, BD, C2, CD, D2;

            public void AddPlane(double a, double b, double c, double d, double w)
            {
                A2 += w * a * a;
                AB += w * a * b;
                AC += w * a * c;
                AD += w * a * d;
                B2 += w * b * b;
                BC += w * b * c;
                BD += w * b * d;
                C2 += w * c * c;
                CD += w * c * d;
                D2 += w * d * d;
            }

            public static Quadric operator +(Quadric p, Quadric q)
            {
                return new Quadric
                {
                    A2 = p.A2 + q.A2, AB = p.AB + q.AB, AC = p.AC + q.AC, AD = p.AD + q.AD, B2 = p.B2 + q.B2,
                    BC = p.BC + q.BC, BD = p.BD + q.BD, C2 = p.C2 + q.C2, CD = p.CD + q.CD, D2 = p.D2 + q.D2,
                };
            }

            public double Error(Vector3 v)
            {
                double x = v.x, y = v.y, z = v.z;

                return A2 * x * x + 2 * AB * x * y + 2 * AC * x * z + 2 * AD * x + B2 * y * y + 2 * BC * y * z + 2 * BD * y + C2 * z * z + 2 * CD * z + D2;
            }

            public bool Optimal(out Vector3 v)
            {
                // Resolve [A2 AB AC; AB B2 BC; AC BC C2] v = -[AD BD CD].
                double det = A2 * (B2 * C2 - BC * BC) - AB * (AB * C2 - BC * AC) + AC * (AB * BC - B2 * AC);
                v = Vector3.zero;

                if (System.Math.Abs(det) < 1e-12)
                {
                    return false;
                }

                double rx = -AD, ry = -BD, rz = -CD;
                double x = (rx * (B2 * C2 - BC * BC) - AB * (ry * C2 - BC * rz) + AC * (ry * BC - B2 * rz)) / det;
                double y = (A2 * (ry * C2 - BC * rz) - rx * (AB * C2 - BC * AC) + AC * (AB * rz - ry * AC)) / det;
                double z = (A2 * (B2 * rz - ry * BC) - AB * (AB * rz - ry * AC) + rx * (AB * BC - B2 * AC)) / det;
                v = new Vector3((float)x, (float)y, (float)z);

                return !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z);
            }
        }

        private struct Candidate
        {
            public double Cost;
            public int A, B, VerA, VerB;
            public Vector3 Target;
        }

        /// <summary>Binário (mínimo no topo).</summary>
        private sealed class Heap
        {
            private readonly List<Candidate> _items = new List<Candidate>();

            public int Count => _items.Count;

            public void Push(Candidate c)
            {
                _items.Add(c);
                int i = _items.Count - 1;

                while (i > 0)
                {
                    int p = (i - 1) / 2;

                    if (_items[p].Cost <= _items[i].Cost)
                    {
                        break;
                    }

                    (_items[p], _items[i]) = (_items[i], _items[p]);
                    i = p;
                }
            }

            public Candidate Pop()
            {
                Candidate top = _items[0];
                Candidate last = _items[_items.Count - 1];
                _items.RemoveAt(_items.Count - 1);

                if (_items.Count > 0)
                {
                    _items[0] = last;
                    int i = 0;

                    while (true)
                    {
                        int l = 2 * i + 1;
                        int r = l + 1;
                        int m = i;

                        if (l < _items.Count && _items[l].Cost < _items[m].Cost)
                        {
                            m = l;
                        }

                        if (r < _items.Count && _items[r].Cost < _items[m].Cost)
                        {
                            m = r;
                        }

                        if (m == i)
                        {
                            break;
                        }

                        (_items[m], _items[i]) = (_items[i], _items[m]);
                        i = m;
                    }
                }

                return top;
            }
        }

        /// <summary>Reduz a malha até no máximo targetTriangles triângulos (ou até não haver mais colapso seguro).</summary>
        public static MeshData Decimate(MeshData source, int targetTriangles, float boundaryWeight = 40f)
        {
            // Triângulos achatados em listas únicas.
            var corner = new List<int>();
            var triSub = new List<int>();

            for (int s = 0; s < source.Subs.Count; s++)
            {
                List<int> sub = source.Subs[s];

                for (int i = 0; i + 2 < sub.Count; i += 3)
                {
                    corner.Add(sub[i]);
                    corner.Add(sub[i + 1]);
                    corner.Add(sub[i + 2]);
                    triSub.Add(s);
                }
            }

            // Solda as posições (grade de 0,1 mm).
            var weld = new Dictionary<(int, int, int), int>();
            var posOf = new int[source.Pos.Count];
            var pos = new List<Vector3>();

            for (int v = 0; v < source.Pos.Count; v++)
            {
                Vector3 p = source.Pos[v];
                var key = (Mathf.RoundToInt(p.x * 10000f), Mathf.RoundToInt(p.y * 10000f), Mathf.RoundToInt(p.z * 10000f));

                if (!weld.TryGetValue(key, out int id))
                {
                    id = pos.Count;
                    weld[key] = id;
                    pos.Add(p);
                }

                posOf[v] = id;
            }

            int triCount = triSub.Count;
            var tri = new int[triCount * 3];
            var alive = new bool[triCount];
            int aliveCount = 0;
            var adj = new List<int>[pos.Count];

            for (int p = 0; p < pos.Count; p++)
            {
                adj[p] = new List<int>(6);
            }

            for (int t = 0; t < triCount; t++)
            {
                int a = posOf[corner[t * 3]];
                int b = posOf[corner[t * 3 + 1]];
                int c = posOf[corner[t * 3 + 2]];
                tri[t * 3] = a;
                tri[t * 3 + 1] = b;
                tri[t * 3 + 2] = c;

                if (a == b || b == c || a == c)
                {
                    continue;
                }

                alive[t] = true;
                aliveCount++;
                adj[a].Add(t);
                adj[b].Add(t);
                adj[c].Add(t);
            }

            // Quádricas: planos dos triângulos (ponderados pela área) e planos de fronteira.
            var q = new Quadric[pos.Count];
            var edgeInfo = new Dictionary<long, (int count, int sub, bool mixed, int tri)>();

            for (int t = 0; t < triCount; t++)
            {
                if (!alive[t])
                {
                    continue;
                }

                Vector3 n = TriNormal(pos, tri, t, out float area);

                if (area < 1e-12f)
                {
                    continue;
                }

                double d = -Vector3.Dot(n, pos[tri[t * 3]]);

                for (int k = 0; k < 3; k++)
                {
                    q[tri[t * 3 + k]].AddPlane(n.x, n.y, n.z, d, area);
                    long key = EdgeKey(tri[t * 3 + k], tri[t * 3 + (k + 1) % 3]);

                    if (edgeInfo.TryGetValue(key, out var info))
                    {
                        edgeInfo[key] = (info.count + 1, info.sub, info.mixed || info.sub != triSub[t], info.tri);
                    }
                    else
                    {
                        edgeInfo[key] = (1, triSub[t], false, t);
                    }
                }
            }

            foreach (var pair in edgeInfo)
            {
                if (pair.Value.count == 2 && !pair.Value.mixed)
                {
                    continue;
                }

                int a = (int)(pair.Key >> 32);
                int b = (int)(pair.Key & 0xffffffff);
                Vector3 edge = pos[b] - pos[a];
                float len = edge.magnitude;

                // Todos os triângulos nessa aresta ganham um plano perpendicular a eles passando por ela.
                foreach (int t in adj[a])
                {
                    if (!Contains(tri, t, b))
                    {
                        continue;
                    }

                    Vector3 n = TriNormal(pos, tri, t, out float area);
                    Vector3 pn = Vector3.Cross(edge, n);

                    if (pn.sqrMagnitude < 1e-14f)
                    {
                        continue;
                    }

                    pn.Normalize();
                    double d = -Vector3.Dot(pn, pos[a]);
                    double w = boundaryWeight * len;
                    q[a].AddPlane(pn.x, pn.y, pn.z, d, w);
                    q[b].AddPlane(pn.x, pn.y, pn.z, d, w);
                }
            }

            var version = new int[pos.Count];
            var dead = new bool[pos.Count];
            var parent = new int[pos.Count];

            for (int p = 0; p < pos.Count; p++)
            {
                parent[p] = p;
            }

            var heap = new Heap();
            var seen = new HashSet<long>();

            for (int t = 0; t < triCount; t++)
            {
                if (!alive[t])
                {
                    continue;
                }

                for (int k = 0; k < 3; k++)
                {
                    int a = tri[t * 3 + k];
                    int b = tri[t * 3 + (k + 1) % 3];

                    if (seen.Add(EdgeKey(a, b)))
                    {
                        heap.Push(MakeCandidate(pos, q, version, a, b));
                    }
                }
            }

            var neighbours = new HashSet<int>();

            while (aliveCount > targetTriangles && heap.Count > 0)
            {
                Candidate c = heap.Pop();

                if (dead[c.A] || dead[c.B] || version[c.A] != c.VerA || version[c.B] != c.VerB)
                {
                    continue;
                }

                if (!SafeCollapse(pos, tri, adj, c.A, c.B, c.Target) || !SafeCollapse(pos, tri, adj, c.B, c.A, c.Target))
                {
                    continue;
                }

                // Colapsa B em A.
                int a2 = c.A;
                int b2 = c.B;
                pos[a2] = c.Target;
                q[a2] = q[a2] + q[b2];
                dead[b2] = true;
                parent[b2] = a2;

                foreach (int t in adj[b2].ToArray())
                {
                    if (Contains(tri, t, a2))
                    {
                        // Triângulo sobre a aresta colapsada: some.
                        alive[t] = false;
                        aliveCount--;

                        for (int k = 0; k < 3; k++)
                        {
                            int v = tri[t * 3 + k];

                            if (v != b2)
                            {
                                adj[v].Remove(t);
                            }
                        }
                    }
                    else
                    {
                        for (int k = 0; k < 3; k++)
                        {
                            if (tri[t * 3 + k] == b2)
                            {
                                tri[t * 3 + k] = a2;
                            }
                        }

                        adj[a2].Add(t);
                    }
                }

                adj[b2].Clear();
                version[a2]++;
                neighbours.Clear();

                foreach (int t in adj[a2])
                {
                    for (int k = 0; k < 3; k++)
                    {
                        int v = tri[t * 3 + k];

                        if (v != a2)
                        {
                            neighbours.Add(v);
                        }
                    }
                }

                foreach (int n in neighbours)
                {
                    heap.Push(MakeCandidate(pos, q, version, a2, n));
                }
            }

            // Saída: cantos originais com a posição colapsada.
            var result = new MeshData { Weights = source.Weights != null ? new List<BoneWeight>() : null };
            var map = new Dictionary<int, int>();

            for (int s = 0; s < source.Subs.Count; s++)
            {
                result.Subs.Add(new List<int>());
            }

            for (int t = 0; t < triCount; t++)
            {
                if (!alive[t])
                {
                    continue;
                }

                int[] ids = new int[3];

                for (int k = 0; k < 3; k++)
                {
                    int v = corner[t * 3 + k];

                    if (!map.TryGetValue(v, out int index))
                    {
                        index = result.Pos.Count;
                        map[v] = index;
                        result.Pos.Add(pos[Root(parent, posOf[v])]);
                        result.Nrm.Add(source.Nrm[v]);
                        result.Uv.Add(source.Uv[v]);
                        result.Weights?.Add(source.Weights[v]);
                    }

                    ids[k] = index;
                }

                // Descarta triângulos que viraram degenerados na saída (cantos distintos na mesma posição).
                if (result.Pos[ids[0]] == result.Pos[ids[1]] || result.Pos[ids[1]] == result.Pos[ids[2]] || result.Pos[ids[0]] == result.Pos[ids[2]])
                {
                    continue;
                }

                result.Subs[triSub[t]].AddRange(ids);
            }

            return result;
        }

        private static int Root(int[] parent, int p)
        {
            while (parent[p] != p)
            {
                parent[p] = parent[parent[p]];
                p = parent[p];
            }

            return p;
        }

        private static long EdgeKey(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        private static bool Contains(int[] tri, int t, int v) => tri[t * 3] == v || tri[t * 3 + 1] == v || tri[t * 3 + 2] == v;

        private static Vector3 TriNormal(List<Vector3> pos, int[] tri, int t, out float area)
        {
            Vector3 p0 = pos[tri[t * 3]];
            Vector3 cross = Vector3.Cross(pos[tri[t * 3 + 1]] - p0, pos[tri[t * 3 + 2]] - p0);
            float len = cross.magnitude;
            area = len * 0.5f;

            return len < 1e-12f ? Vector3.up : cross / len;
        }

        private static Candidate MakeCandidate(List<Vector3> pos, Quadric[] q, int[] version, int a, int b)
        {
            Quadric sum = q[a] + q[b];
            Vector3 pa = pos[a];
            Vector3 pb = pos[b];
            Vector3 best = (pa + pb) * 0.5f;
            double bestCost = sum.Error(best);

            if (sum.Optimal(out Vector3 opt) && (opt - best).magnitude < (pa - pb).magnitude * 3f)
            {
                double e = sum.Error(opt);

                if (e < bestCost)
                {
                    bestCost = e;
                    best = opt;
                }
            }

            double ea = sum.Error(pa);
            double eb = sum.Error(pb);

            if (ea < bestCost)
            {
                bestCost = ea;
                best = pa;
            }

            if (eb < bestCost)
            {
                bestCost = eb;
                best = pb;
            }

            return new Candidate { Cost = System.Math.Max(0.0, bestCost), A = a, B = b, VerA = version[a], VerB = version[b], Target = best };
        }

        /// <summary>Verifica se mover o vértice v para target (como se b fosse colapsado em a) não inverte nenhum triângulo vizinho.</summary>
        private static bool SafeCollapse(List<Vector3> pos, int[] tri, List<int>[] adj, int v, int other, Vector3 target)
        {
            foreach (int t in adj[v])
            {
                if (Contains(tri, t, other))
                {
                    continue;
                }

                Vector3 oldN = TriNormal(pos, tri, t, out float oldArea);
                Vector3 p0 = pos[tri[t * 3]];
                Vector3 p1 = pos[tri[t * 3 + 1]];
                Vector3 p2 = pos[tri[t * 3 + 2]];

                if (tri[t * 3] == v)
                {
                    p0 = target;
                }
                else if (tri[t * 3 + 1] == v)
                {
                    p1 = target;
                }
                else
                {
                    p2 = target;
                }

                Vector3 cross = Vector3.Cross(p1 - p0, p2 - p0);
                float len = cross.magnitude;

                if (len < 1e-10f || Vector3.Dot(oldN, cross / len) < 0.15f)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
