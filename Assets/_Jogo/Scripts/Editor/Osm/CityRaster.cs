using System;
using System.Collections.Generic;
using UnityEngine;

namespace Entregador.Editor
{
    /// <summary>Classe de uma rua na grade (a maior prioridade vence onde elas se cruzam).</summary>
    public enum RoadClass : byte
    {
        None = 0,
        Service = 1,
        Pedestrian = 2,
        Residential = 3,
        Tertiary = 4,
        Primary = 5,
    }

    /// <summary>
    /// Grade de 0,5 m sobre o mapa (com uma margem de 8 m além da caixa do OSM). As ruas são pintadas nela; o que sobra entre
    /// as ruas são os quarteirões (componentes conexos), de onde saem os polígonos de calçada, a distância ao meio-fio
    /// (transformada de distância exata), as zonas (largo, parques) e a ocupação dos lotes.
    /// </summary>
    public sealed class CityRaster
    {
        public const float Cell = 0.5f;
        public const float Margin = 8f;

        public readonly int W;
        public readonly int H;
        public readonly float X0;
        public readonly float Z0;

        /// <summary>Classe da rua em cada célula (0 = não é rua).</summary>
        public readonly byte[] Road;

        /// <summary>Quarteirão de cada célula (0 = rua ou fora). Os números são os rótulos usados em Trace.</summary>
        public readonly ushort[] Block;

        /// <summary>Distância (m) de cada célula de quarteirão até a célula de rua mais próxima.</summary>
        public float[] Dist;

        /// <summary>Zona de cada célula: 0 nada, 1 largo, 2 parque, 3 pátio/estacionamento, 4 cemitério.</summary>
        public readonly byte[] Zone;

        /// <summary>Ocupação: 0 livre, 1 lote ou fundos, 2 prédio do OSM.</summary>
        public readonly byte[] Occ;

        public CityRaster(float halfX, float halfZ)
        {
            X0 = -halfX - Margin;
            Z0 = -halfZ - Margin;
            W = Mathf.CeilToInt((halfX + Margin) * 2f / Cell);
            H = Mathf.CeilToInt((halfZ + Margin) * 2f / Cell);
            int n = W * H;
            Road = new byte[n];
            Block = new ushort[n];
            Dist = new float[n];
            Zone = new byte[n];
            Occ = new byte[n];
        }

        public int Ix(float x) => Mathf.FloorToInt((x - X0) / Cell);

        public int Iz(float z) => Mathf.FloorToInt((z - Z0) / Cell);

        public bool In(int ix, int iz) => ix >= 0 && iz >= 0 && ix < W && iz < H;

        public int Index(int ix, int iz) => iz * W + ix;

        /// <summary>Índice da célula que contém o ponto, ou -1 se for fora da grade.</summary>
        public int At(Vector2 p)
        {
            int ix = Ix(p.x);
            int iz = Iz(p.y);

            return In(ix, iz) ? Index(ix, iz) : -1;
        }

        public Vector2 Center(int index) => new Vector2(X0 + (index % W + 0.5f) * Cell, Z0 + (index / W + 0.5f) * Cell);

        public ushort BlockAt(Vector2 p)
        {
            int i = At(p);

            return i < 0 ? (ushort)0 : Block[i];
        }

        public float DistAt(Vector2 p)
        {
            int i = At(p);

            return i < 0 || Block[i] == 0 ? 0f : Dist[i];
        }

        public byte ZoneAt(Vector2 p)
        {
            int i = At(p);

            return i < 0 ? (byte)0 : Zone[i];
        }

        public bool IsRoad(Vector2 p)
        {
            int i = At(p);

            return i >= 0 && Block[i] == 0;
        }

        private readonly List<float> _crossings = new List<float>();

        /// <summary>Chama set(índice) para toda célula cujo centro está dentro do polígono (par-ímpar).</summary>
        public void FillPolygon(IList<Vector2> poly, Action<int> set)
        {
            float minZ = float.MaxValue;
            float maxZ = float.MinValue;

            foreach (Vector2 p in poly)
            {
                minZ = Mathf.Min(minZ, p.y);
                maxZ = Mathf.Max(maxZ, p.y);
            }

            int row0 = Mathf.Max(0, Iz(minZ));
            int row1 = Mathf.Min(H - 1, Iz(maxZ));

            for (int iz = row0; iz <= row1; iz++)
            {
                float zc = Z0 + (iz + 0.5f) * Cell;
                _crossings.Clear();

                for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                {
                    Vector2 a = poly[i];
                    Vector2 b = poly[j];

                    if ((a.y <= zc) != (b.y <= zc))
                    {
                        _crossings.Add(a.x + (zc - a.y) / (b.y - a.y) * (b.x - a.x));
                    }
                }

                _crossings.Sort();

                for (int k = 0; k + 1 < _crossings.Count; k += 2)
                {
                    int ix0 = Mathf.Max(0, Mathf.CeilToInt((_crossings[k] - X0) / Cell - 0.5f));
                    int ix1 = Mathf.Min(W - 1, Mathf.FloorToInt((_crossings[k + 1] - X0) / Cell - 0.5f));

                    for (int ix = ix0; ix <= ix1; ix++)
                    {
                        set(iz * W + ix);
                    }
                }
            }
        }

        /// <summary>Distância euclidiana exata (m) de cada célula até a célula-fonte mais próxima (Felzenszwalb).</summary>
        public float[] DistanceTo(Func<int, bool> isSource)
        {
            const float far = 1e9f;
            var f = new float[W * H];

            for (int i = 0; i < f.Length; i++)
            {
                f[i] = isSource(i) ? 0f : far;
            }

            int max = Mathf.Max(W, H);
            var line = new float[max];
            var result = new float[max];
            var v = new int[max];
            var z = new float[max + 1];

            for (int x = 0; x < W; x++)
            {
                for (int y = 0; y < H; y++)
                {
                    line[y] = f[y * W + x];
                }

                Edt1D(line, H, result, v, z);

                for (int y = 0; y < H; y++)
                {
                    f[y * W + x] = result[y];
                }
            }

            for (int y = 0; y < H; y++)
            {
                int row = y * W;

                for (int x = 0; x < W; x++)
                {
                    line[x] = f[row + x];
                }

                Edt1D(line, W, result, v, z);

                for (int x = 0; x < W; x++)
                {
                    f[row + x] = Mathf.Sqrt(result[x]) * Cell;
                }
            }

            return f;
        }

        private static void Edt1D(float[] f, int n, float[] d, int[] v, float[] z)
        {
            int k = 0;
            v[0] = 0;
            z[0] = float.NegativeInfinity;
            z[1] = float.PositiveInfinity;

            for (int q = 1; q < n; q++)
            {
                float s = ((f[q] + (float)q * q) - (f[v[k]] + (float)v[k] * v[k])) / (2f * q - 2f * v[k]);

                while (s <= z[k])
                {
                    k--;
                    s = ((f[q] + (float)q * q) - (f[v[k]] + (float)v[k] * v[k])) / (2f * q - 2f * v[k]);
                }

                k++;
                v[k] = q;
                z[k] = s;
                z[k + 1] = float.PositiveInfinity;
            }

            k = 0;

            for (int q = 0; q < n; q++)
            {
                while (z[k + 1] < q)
                {
                    k++;
                }

                d[q] = (q - v[k]) * (float)(q - v[k]) + f[v[k]];
            }
        }

        /// <summary>
        /// Contornos das regiões de mesmo rótulo (0 = vazio). Cada região de 4 vizinhos vira um contorno externo anti-horário
        /// (área > 0) e, se tiver buracos, contornos horários (área &lt; 0). Coordenadas em metros, sem simplificar.
        /// </summary>
        public Dictionary<int, List<List<Vector2>>> Trace(ushort[] labels)
        {
            var edges = new Dictionary<int, List<(int a, int b)>>();
            int stride = W + 1;

            void Add(int label, int ax, int az, int bx, int bz)
            {
                if (!edges.TryGetValue(label, out List<(int a, int b)> list))
                {
                    list = new List<(int a, int b)>();
                    edges[label] = list;
                }

                list.Add((az * stride + ax, bz * stride + bx));
            }

            for (int iz = 0; iz < H; iz++)
            {
                for (int ix = 0; ix < W; ix++)
                {
                    int label = labels[iz * W + ix];

                    if (label == 0)
                    {
                        continue;
                    }

                    if (iz == 0 || labels[(iz - 1) * W + ix] != label)
                    {
                        Add(label, ix, iz, ix + 1, iz);
                    }

                    if (ix == W - 1 || labels[iz * W + ix + 1] != label)
                    {
                        Add(label, ix + 1, iz, ix + 1, iz + 1);
                    }

                    if (iz == H - 1 || labels[(iz + 1) * W + ix] != label)
                    {
                        Add(label, ix + 1, iz + 1, ix, iz + 1);
                    }

                    if (ix == 0 || labels[iz * W + ix - 1] != label)
                    {
                        Add(label, ix, iz + 1, ix, iz);
                    }
                }
            }

            var result = new Dictionary<int, List<List<Vector2>>>();

            foreach (KeyValuePair<int, List<(int a, int b)>> pair in edges)
            {
                List<(int a, int b)> list = pair.Value;
                var outgoing = new Dictionary<int, List<int>>();

                for (int e = 0; e < list.Count; e++)
                {
                    if (!outgoing.TryGetValue(list[e].a, out List<int> starts))
                    {
                        starts = new List<int>(1);
                        outgoing[list[e].a] = starts;
                    }

                    starts.Add(e);
                }

                var used = new bool[list.Count];
                var loops = new List<List<Vector2>>();

                for (int e0 = 0; e0 < list.Count; e0++)
                {
                    if (used[e0])
                    {
                        continue;
                    }

                    var loop = new List<Vector2>();
                    int e = e0;

                    while (e >= 0 && !used[e])
                    {
                        used[e] = true;
                        int a = list[e].a;
                        int b = list[e].b;
                        loop.Add(new Vector2(X0 + a % stride * Cell, Z0 + a / stride * Cell));
                        int next = -1;
                        float bestTurn = float.NegativeInfinity;
                        Vector2 d0 = new Vector2(b % stride - a % stride, b / stride - a / stride);

                        foreach (int candidate in outgoing[b])
                        {
                            if (used[candidate] && candidate != e0)
                            {
                                continue;
                            }

                            int c = list[candidate].b;
                            Vector2 d1 = new Vector2(c % stride - b % stride, c / stride - b / stride);
                            float turn = Geo.Cross(d0, d1);

                            if (turn > bestTurn)
                            {
                                bestTurn = turn;
                                next = candidate;
                            }
                        }

                        e = next == e0 ? -1 : next;
                    }

                    if (loop.Count >= 4)
                    {
                        loops.Add(loop);
                    }
                }

                result[pair.Key] = loops;
            }

            return result;
        }
    }
}
