using System.Collections.Generic;
using UnityEngine;

namespace Entregador.Game
{
    /// <summary>
    /// Paleta de cores da moto e do piloto: uma textura minúscula (64x1, filtro de ponto) em que cada célula é uma cor
    /// (RGB) com a suavidade no alfa. Todas as peças "pintadas" usam um único material; a UV de cada vértice aponta para a célula.
    /// </summary>
    public sealed class MotoPalette
    {
        public const int Size = 64;

        private readonly List<Color32> _cells = new List<Color32>();

        public int Count => _cells.Count;

        /// <summary>Devolve o índice da célula dessa cor (cria se for nova). A suavidade vai no alfa.</summary>
        public int Get(Color color, float smoothness = 0.4f)
        {
            var c32 = new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt(color.r * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(color.g * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(color.b * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(smoothness * 255f), 0, 255));

            for (int i = 0; i < _cells.Count; i++)
            {
                Color32 e = _cells[i];

                if (e.r == c32.r && e.g == c32.g && e.b == c32.b && e.a == c32.a)
                {
                    return i;
                }
            }

            if (_cells.Count >= Size)
            {
                return Size - 1;
            }

            _cells.Add(c32);

            return _cells.Count - 1;
        }

        public Texture2D BuildTexture()
        {
            var texture = new Texture2D(Size, 1, TextureFormat.RGBA32, false, false)
            {
                name = "Moto_Paleta",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };

            var pixels = new Color32[Size];

            for (int i = 0; i < Size; i++)
            {
                pixels[i] = i < _cells.Count ? _cells[i] : new Color32(255, 0, 255, 255);
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);

            return texture;
        }

        public static Vector2 Uv(int cell) => new Vector2((cell + 0.5f) / Size, 0.5f);
    }

    /// <summary>Seção de um loft: centro, eixos da seção, meia-largura (W, no eixo R), meia-altura (H, no eixo U) e expoente da superelipse (2 = elipse, mais alto = caixa arredondada).</summary>
    public struct Sec
    {
        public Vector3 C;
        public Vector3 R;
        public Vector3 U;
        public float W;
        public float H;
        public float E;

        public Sec(Vector3 c, Vector3 r, Vector3 u, float w, float h, float e = 2f)
        {
            C = c;
            R = r;
            U = u;
            W = w;
            H = h;
            E = e;
        }
    }

    /// <summary>
    /// Gerador de malhas lisas: loft de seções (superelipses) ao longo de um caminho, tubos com pontas arredondadas e
    /// revolução de perfis (rodas, faróis). Acumula dois submeshes: 0 = tinta/plástico/borracha, 1 = metal.
    /// Cada triângulo é orientado para fora na hora de emitir, então a ordem dos pontos não importa.
    /// </summary>
    public sealed class MotoMeshKit
    {
        private readonly List<Vector3> _v = new List<Vector3>();
        private readonly List<Vector2> _uv = new List<Vector2>();
        private readonly List<int> _paint = new List<int>();
        private readonly List<int> _metal = new List<int>();
        private readonly MotoPalette _palette;

        public MotoMeshKit(MotoPalette palette)
        {
            _palette = palette;
        }

        /// <summary>Subtraído de todo ponto emitido (permite escrever em coordenadas da moto e gerar no espaço local de uma peça).</summary>
        public Vector3 Origin;

        public int Triangles => (_paint.Count + _metal.Count) / 3;

        public int Color(Color color, float smoothness = 0.4f) => _palette.Get(color, smoothness);

        private int Add(Vector3 p, int cell)
        {
            _v.Add(p - Origin);
            _uv.Add(MotoPalette.Uv(cell));

            return _v.Count - 1;
        }

        private void Tri(bool metal, int a, int b, int c, Vector3 outward)
        {
            Vector3 n = Vector3.Cross(_v[b] - _v[a], _v[c] - _v[a]);
            List<int> list = metal ? _metal : _paint;

            if (Vector3.Dot(n, outward) < 0f)
            {
                list.Add(a);
                list.Add(c);
                list.Add(b);
            }
            else
            {
                list.Add(a);
                list.Add(b);
                list.Add(c);
            }
        }

        private static float SgnPow(float v, float p) => Mathf.Sign(v) * Mathf.Pow(Mathf.Abs(v), p);

        /// <summary>Loft: liga as seções em sequência com anéis de "segs" pontos; as pontas podem ser tampadas.</summary>
        public void Loft(IList<Sec> secs, int segs, int cell, bool metal = false, bool capA = true, bool capB = true)
        {
            int n = secs.Count;

            if (n < 2)
            {
                return;
            }

            var idx = new int[n, segs];

            for (int i = 0; i < n; i++)
            {
                Sec s = secs[i];
                float p = 2f / Mathf.Max(0.5f, s.E);

                for (int j = 0; j < segs; j++)
                {
                    float a = Mathf.PI * 2f * j / segs;
                    float x = SgnPow(Mathf.Cos(a), p);
                    float y = SgnPow(Mathf.Sin(a), p);
                    idx[i, j] = Add(s.C + s.R * (s.W * x) + s.U * (s.H * y), cell);
                }
            }

            for (int i = 0; i < n - 1; i++)
            {
                Vector3 mid = (secs[i].C + secs[i + 1].C) * 0.5f;

                for (int j = 0; j < segs; j++)
                {
                    int j2 = (j + 1) % segs;
                    int a = idx[i, j];
                    int b = idx[i, j2];
                    int c = idx[i + 1, j];
                    int d = idx[i + 1, j2];
                    Vector3 center = (_v[a] + _v[b] + _v[c] + _v[d]) * 0.25f + Origin;
                    Vector3 outward = center - mid;
                    Tri(metal, a, b, c, outward);
                    Tri(metal, b, d, c, outward);
                }
            }

            if (capA)
            {
                Cap(secs[0], secs[0].C - secs[1].C, segs, cell, metal);
            }

            if (capB)
            {
                Cap(secs[n - 1], secs[n - 1].C - secs[n - 2].C, segs, cell, metal);
            }
        }

        private void Cap(Sec s, Vector3 outward, int segs, int cell, bool metal)
        {
            if (s.W < 0.0004f || s.H < 0.0004f)
            {
                return;
            }

            float p = 2f / Mathf.Max(0.5f, s.E);
            int center = Add(s.C, cell);
            var ring = new int[segs];

            for (int j = 0; j < segs; j++)
            {
                float a = Mathf.PI * 2f * j / segs;
                ring[j] = Add(s.C + s.R * (s.W * SgnPow(Mathf.Cos(a), p)) + s.U * (s.H * SgnPow(Mathf.Sin(a), p)), cell);
            }

            for (int j = 0; j < segs; j++)
            {
                Tri(metal, center, ring[j], ring[(j + 1) % segs], outward);
            }
        }

        /// <summary>Seções ao longo de uma poligonal, com quadros transportados (sem torção). Raios por ponto; "round" fecha as pontas como meia esfera.</summary>
        public static List<Sec> Path(IList<Vector3> pts, IList<float> w, IList<float> h, Vector3 upHint, float e = 2f, bool round = false)
        {
            int n = pts.Count;
            var list = new List<Sec>();
            Vector3 u = upHint;

            for (int i = 0; i < n; i++)
            {
                Vector3 t = (pts[Mathf.Min(i + 1, n - 1)] - pts[Mathf.Max(i - 1, 0)]).normalized;

                if (i == 0)
                {
                    u = u - t * Vector3.Dot(u, t);

                    if (u.sqrMagnitude < 1e-4f)
                    {
                        u = Mathf.Abs(t.y) < 0.9f ? Vector3.up : Vector3.right;
                        u = u - t * Vector3.Dot(u, t);
                    }
                }
                else
                {
                    u = u - t * Vector3.Dot(u, t);
                }

                u.Normalize();
                Vector3 r = Vector3.Cross(u, t).normalized;
                list.Add(new Sec(pts[i], r, u, w[i], h[i], e));
            }

            if (round)
            {
                Sec a = list[0];
                Sec b = list[n - 1];
                Vector3 ta = (pts[1] - pts[0]).normalized;
                Vector3 tb = (pts[n - 1] - pts[n - 2]).normalized;
                float ra = (a.W + a.H) * 0.5f;
                float rb = (b.W + b.H) * 0.5f;
                float[] d = { 0.38f, 0.74f, 0.95f };
                float[] s = { 0.92f, 0.70f, 0.36f };

                for (int k = 0; k < d.Length; k++)
                {
                    list.Insert(0, new Sec(a.C - ta * (ra * d[k]), a.R, a.U, a.W * s[k], a.H * s[k], e));
                }

                for (int k = 0; k < d.Length; k++)
                {
                    list.Add(new Sec(b.C + tb * (rb * d[k]), b.R, b.U, b.W * s[k], b.H * s[k], e));
                }
            }

            return list;
        }

        /// <summary>Tubo de raio constante (ou do raio inicial ao final) por uma poligonal.</summary>
        public void Tube(IList<Vector3> pts, float r0, float r1, int segs, int cell, bool metal = false, bool round = false, Vector3? up = null)
        {
            int n = pts.Count;
            var w = new float[n];

            for (int i = 0; i < n; i++)
            {
                w[i] = Mathf.Lerp(r0, r1, n == 1 ? 0f : (float)i / (n - 1));
            }

            Loft(Path(pts, w, w, up ?? Vector3.up, 2f, round), segs, cell, metal);
        }

        public void Tube(Vector3 a, Vector3 b, float r, int segs, int cell, bool metal = false, bool round = false)
        {
            Tube(new[] { a, b }, r, r, segs, cell, metal, round);
        }

        /// <summary>
        /// Revolução de um perfil (raio, posição no eixo) em volta de "axis" por "origin", de a0 a a1 graus (360 = anel completo).
        /// dir0 é a direção radial do ângulo 0. O perfil é um laço fechado.
        /// </summary>
        public void Revolve(IList<Vector2> prof, Vector3 origin, Vector3 axis, Vector3 dir0, float a0, float a1, int segs, int cell, bool metal = false)
        {
            int np = prof.Count;
            Vector3 ax = axis.normalized;
            Vector3 d0 = (dir0 - ax * Vector3.Dot(dir0, ax)).normalized;
            Vector3 d90 = Vector3.Cross(ax, d0);
            bool full = a1 - a0 >= 359.9f;
            int cols = full ? segs : segs + 1;
            var idx = new int[cols, np];
            Vector2 centroid = Vector2.zero;

            for (int i = 0; i < np; i++)
            {
                centroid += prof[i];
            }

            centroid /= np;

            for (int k = 0; k < cols; k++)
            {
                float a = Mathf.Deg2Rad * Mathf.Lerp(a0, a1, (float)k / segs);
                Vector3 rad = d0 * Mathf.Cos(a) + d90 * Mathf.Sin(a);

                for (int i = 0; i < np; i++)
                {
                    idx[k, i] = Add(origin + ax * prof[i].y + rad * prof[i].x, cell);
                }
            }

            for (int k = 0; k < segs; k++)
            {
                int k2 = full ? (k + 1) % segs : k + 1;
                float am = Mathf.Deg2Rad * Mathf.Lerp(a0, a1, (k + 0.5f) / segs);
                Vector3 radm = d0 * Mathf.Cos(am) + d90 * Mathf.Sin(am);
                Vector3 inner = origin + ax * centroid.y + radm * centroid.x;

                for (int i = 0; i < np; i++)
                {
                    int i2 = (i + 1) % np;
                    int a = idx[k, i];
                    int b = idx[k2, i];
                    int c = idx[k, i2];
                    int d = idx[k2, i2];
                    Vector3 center = (_v[a] + _v[b] + _v[c] + _v[d]) * 0.25f + Origin;
                    Vector3 outward = center - inner;
                    Tri(metal, a, b, c, outward);
                    Tri(metal, b, d, c, outward);
                }
            }
        }

        /// <summary>Perfil fechado arredondado (superelipse) para pneus, aros e para-lamas: centro (rc, xc), meias medidas (hr, hx).</summary>
        public static Vector2[] RoundProfile(float rc, float xc, float hr, float hx, int n, float e = 2.6f)
        {
            var pts = new Vector2[n];
            float p = 2f / e;

            for (int i = 0; i < n; i++)
            {
                float a = Mathf.PI * 2f * i / n;
                pts[i] = new Vector2(rc + hr * SgnPow(Mathf.Cos(a), p), xc + hx * SgnPow(Mathf.Sin(a), p));
            }

            return pts;
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(_v);
            mesh.SetUVs(0, _uv);
            mesh.subMeshCount = _metal.Count > 0 ? 2 : 1;
            mesh.SetTriangles(_paint, 0, false);

            if (_metal.Count > 0)
            {
                mesh.SetTriangles(_metal, 1, false);
            }

            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }
    }
}
