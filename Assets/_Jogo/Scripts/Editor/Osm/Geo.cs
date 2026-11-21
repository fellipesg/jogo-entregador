using System.Collections.Generic;
using UnityEngine;

namespace Entregador.Editor
{
    /// <summary>
    /// Geometria 2D no plano do chão: um Vector2 guarda x = X (leste) e y = Z (norte) do mundo. Polígonos "positivos" são
    /// anti-horários nesse plano (área com sinal > 0), o que põe o interior à ESQUERDA de cada aresta e a rua à direita.
    /// </summary>
    public static class Geo
    {
        public static Vector3 V3(Vector2 p, float y = 0f) => new Vector3(p.x, y, p.y);

        public static Vector2 V2(Vector3 p) => new Vector2(p.x, p.z);

        /// <summary>Esquerda de uma direção (no plano do chão visto de cima).</summary>
        public static Vector2 Left(Vector2 d) => new Vector2(-d.y, d.x);

        /// <summary>Direita de uma direção.</summary>
        public static Vector2 Right(Vector2 d) => new Vector2(d.y, -d.x);

        public static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        /// <summary>Área com sinal (positiva se o contorno é anti-horário).</summary>
        public static float Area(IList<Vector2> p)
        {
            float a = 0f;

            for (int i = 0; i < p.Count; i++)
            {
                Vector2 u = p[i];
                Vector2 v = p[(i + 1) % p.Count];
                a += u.x * v.y - v.x * u.y;
            }

            return a * 0.5f;
        }

        public static Vector2 Centroid(IList<Vector2> p)
        {
            float area = 0f;
            Vector2 sum = Vector2.zero;

            for (int i = 0; i < p.Count; i++)
            {
                Vector2 u = p[i];
                Vector2 v = p[(i + 1) % p.Count];
                float c = u.x * v.y - v.x * u.y;
                area += c;
                sum += (u + v) * c;
            }

            if (Mathf.Abs(area) < 1e-4f)
            {
                Vector2 mean = Vector2.zero;

                foreach (Vector2 q in p)
                {
                    mean += q;
                }

                return mean / Mathf.Max(1, p.Count);
            }

            return sum / (3f * area);
        }

        /// <summary>Ponto dentro do polígono (par-ímpar).</summary>
        public static bool Inside(Vector2 pt, IList<Vector2> poly)
        {
            bool inside = false;

            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                Vector2 a = poly[i];
                Vector2 b = poly[j];

                if ((a.y > pt.y) != (b.y > pt.y) && pt.x < (b.x - a.x) * (pt.y - a.y) / (b.y - a.y) + a.x)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        /// <summary>Distância do ponto ao segmento; closest = ponto mais próximo e t = posição (0..1) nele.</summary>
        public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b, out Vector2 closest, out float t)
        {
            Vector2 ab = b - a;
            float lengthSqr = ab.sqrMagnitude;
            t = lengthSqr < 1e-8f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSqr);
            closest = a + ab * t;

            return Vector2.Distance(p, closest);
        }

        /// <summary>Douglas-Peucker num contorno fechado (parte de dois pontos distantes entre si).</summary>
        public static List<Vector2> SimplifyClosed(IList<Vector2> loop, float epsilon)
        {
            int n = loop.Count;

            if (n <= 4)
            {
                return new List<Vector2>(loop);
            }

            int far = 0;
            float best = -1f;

            for (int i = 1; i < n; i++)
            {
                float d = (loop[i] - loop[0]).sqrMagnitude;

                if (d > best)
                {
                    best = d;
                    far = i;
                }
            }

            var keep = new bool[n];
            keep[0] = true;
            keep[far] = true;
            Dp(loop, 0, far, epsilon, keep);
            Dp(loop, far, n, epsilon, keep);
            var result = new List<Vector2>();

            for (int i = 0; i < n; i++)
            {
                if (keep[i])
                {
                    result.Add(loop[i]);
                }
            }

            return result;
        }

        // Trecho de a até b (b pode ser n, que volta ao ponto 0).
        private static void Dp(IList<Vector2> p, int a, int b, float epsilon, bool[] keep)
        {
            if (b - a < 2)
            {
                return;
            }

            Vector2 pa = p[a];
            Vector2 pb = p[b % p.Count];
            int index = -1;
            float worst = epsilon;

            for (int i = a + 1; i < b; i++)
            {
                float d = DistanceToSegment(p[i], pa, pb, out _, out _);

                if (d > worst)
                {
                    worst = d;
                    index = i;
                }
            }

            if (index >= 0)
            {
                keep[index] = true;
                Dp(p, a, index, epsilon, keep);
                Dp(p, index, b, epsilon, keep);
            }
        }

        /// <summary>
        /// Triangulação por "orelhas" de um polígono anti-horário (sem buracos). Devolve os triângulos como índices anti-horários.
        /// Falso se o contorno degenerou (autointerseção); os triângulos já achados continuam na lista.
        /// </summary>
        public static bool Triangulate(IList<Vector2> poly, List<int> triangles)
        {
            int n = poly.Count;

            if (n < 3)
            {
                return false;
            }

            var index = new List<int>(n);

            for (int i = 0; i < n; i++)
            {
                index.Add(i);
            }

            int guard = 0;
            bool clean = true;

            while (index.Count > 3 && guard++ < n * n)
            {
                bool clipped = false;

                for (int k = 0; k < index.Count; k++)
                {
                    int ia = index[(k + index.Count - 1) % index.Count];
                    int ib = index[k];
                    int ic = index[(k + 1) % index.Count];
                    Vector2 a = poly[ia];
                    Vector2 b = poly[ib];
                    Vector2 c = poly[ic];

                    if (Cross(b - a, c - b) <= 1e-6f)
                    {
                        continue;
                    }

                    bool ear = true;

                    for (int m = 0; m < index.Count && ear; m++)
                    {
                        int im = index[m];

                        if (im == ia || im == ib || im == ic)
                        {
                            continue;
                        }

                        Vector2 p = poly[im];

                        if (p == a || p == b || p == c)
                        {
                            continue;
                        }

                        ear = !InTriangle(p, a, b, c);
                    }

                    if (ear)
                    {
                        triangles.Add(ia);
                        triangles.Add(ib);
                        triangles.Add(ic);
                        index.RemoveAt(k);
                        clipped = true;

                        break;
                    }
                }

                if (!clipped)
                {
                    // Contorno degenerado: descarta o vértice menos saliente para continuar.
                    clean = false;
                    int drop = 0;
                    float smallest = float.MaxValue;

                    for (int k = 0; k < index.Count; k++)
                    {
                        Vector2 a = poly[index[(k + index.Count - 1) % index.Count]];
                        Vector2 b = poly[index[k]];
                        Vector2 c = poly[index[(k + 1) % index.Count]];
                        float area = Mathf.Abs(Cross(b - a, c - b));

                        if (area < smallest)
                        {
                            smallest = area;
                            drop = k;
                        }
                    }

                    index.RemoveAt(drop);
                }
            }

            if (index.Count == 3)
            {
                Vector2 a = poly[index[0]];
                Vector2 b = poly[index[1]];
                Vector2 c = poly[index[2]];

                if (Cross(b - a, c - b) > 1e-6f)
                {
                    triangles.Add(index[0]);
                    triangles.Add(index[1]);
                    triangles.Add(index[2]);
                }
            }

            return clean;
        }

        private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(b - a, p - a);
            float d2 = Cross(c - b, p - b);
            float d3 = Cross(a - c, p - c);
            bool negative = d1 < -1e-6f || d2 < -1e-6f || d3 < -1e-6f;
            bool positive = d1 > 1e-6f || d2 > 1e-6f || d3 > 1e-6f;

            return !(negative && positive);
        }

        /// <summary>Recorta o segmento ab pelo retângulo [-hx,hx] x [-hz,hz] (Liang-Barsky). Falso se fica todo de fora.</summary>
        public static bool ClipSegment(ref Vector2 a, ref Vector2 b, float hx, float hz)
        {
            float t0 = 0f;
            float t1 = 1f;
            Vector2 d = b - a;

            bool Test(float p, float q)
            {
                if (Mathf.Abs(p) < 1e-9f)
                {
                    return q >= 0f;
                }

                float r = q / p;

                if (p < 0f)
                {
                    if (r > t1)
                    {
                        return false;
                    }

                    t0 = Mathf.Max(t0, r);
                }
                else
                {
                    if (r < t0)
                    {
                        return false;
                    }

                    t1 = Mathf.Min(t1, r);
                }

                return true;
            }

            if (!Test(-d.x, a.x + hx) || !Test(d.x, hx - a.x) || !Test(-d.y, a.y + hz) || !Test(d.y, hz - a.y))
            {
                return false;
            }

            Vector2 start = a;
            a = start + d * t0;
            b = start + d * t1;

            return true;
        }

        /// <summary>Recorta um polígono pelo retângulo (Sutherland-Hodgman).</summary>
        public static List<Vector2> ClipPolygon(IList<Vector2> poly, float hx, float hz)
        {
            List<Vector2> output = new List<Vector2>(poly);

            for (int side = 0; side < 4 && output.Count > 0; side++)
            {
                var input = output;
                output = new List<Vector2>();

                for (int i = 0; i < input.Count; i++)
                {
                    Vector2 cur = input[i];
                    Vector2 prev = input[(i + input.Count - 1) % input.Count];
                    bool curIn = SideIn(cur, side, hx, hz);
                    bool prevIn = SideIn(prev, side, hx, hz);

                    if (curIn != prevIn)
                    {
                        output.Add(SideCross(prev, cur, side, hx, hz));
                    }

                    if (curIn)
                    {
                        output.Add(cur);
                    }
                }
            }

            return output;
        }

        private static bool SideIn(Vector2 p, int side, float hx, float hz)
        {
            switch (side)
            {
                case 0: return p.x >= -hx;
                case 1: return p.x <= hx;
                case 2: return p.y >= -hz;
                default: return p.y <= hz;
            }
        }

        private static Vector2 SideCross(Vector2 a, Vector2 b, int side, float hx, float hz)
        {
            float limit = side == 0 ? -hx : side == 1 ? hx : side == 2 ? -hz : hz;

            if (side < 2)
            {
                float t = (limit - a.x) / (b.x - a.x);

                return new Vector2(limit, a.y + (b.y - a.y) * t);
            }

            float u = (limit - a.y) / (b.y - a.y);

            return new Vector2(a.x + (b.x - a.x) * u, limit);
        }
    }
}
