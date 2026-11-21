using System.Collections.Generic;
using UnityEngine;

namespace Entregador.Game
{
    /// <summary>Malhas do piloto. O tronco tem a origem no quadril; braços e pernas são malhas ao longo de +Z (articuladas por IK em MotoVisual).</summary>
    public sealed class RiderParts
    {
        public Mesh Tronco;
        public Mesh BracoSuperior;
        public Mesh Antebraco;
        public Mesh Coxa;
        public Mesh Canela;
        public Mesh Tenis;

        public float CompBracoSuperior;
        public float CompAntebraco;
        public float CompLuva;
        public float CompCoxa;
        public float CompCanela;

        /// <summary>Distância do tornozelo à sola.</summary>
        public float AlturaTornozelo = 0.085f;

        /// <summary>Do calcanhar (z negativo) à bola do pé, em relação ao tornozelo.</summary>
        public float BolaDoPe = 0.1f;

        public Vector3 OmbroEsq;
        public Vector3 OmbroDir;
        public Vector3 QuadrilEsq;
        public Vector3 QuadrilDir;

        public int Triangulos;
    }

    /// <summary>Gera o entregador sentado: capacete integral, jaqueta com faixa refletiva, jeans, tênis e luvas. Tudo em loft, sem esferas soltas.</summary>
    public static class RiderModel
    {
        private static readonly Vector3 X = Vector3.right;
        private static readonly Vector3 Y = Vector3.up;
        private static readonly Vector3 Z = Vector3.forward;

        // Tabela do tronco: t (ao longo da coluna), meia-largura, meia-profundidade.
        private static readonly float[][] Table =
        {
            new[] { -0.09f, 0.15f, 0.115f }, new[] { 0.0f, 0.162f, 0.122f }, new[] { 0.1f, 0.152f, 0.112f }, new[] { 0.2f, 0.158f, 0.115f },
            new[] { 0.3f, 0.17f, 0.122f }, new[] { 0.4f, 0.185f, 0.118f }, new[] { 0.48f, 0.197f, 0.108f }, new[] { 0.53f, 0.195f, 0.097f },
            new[] { 0.575f, 0.1f, 0.072f }, new[] { 0.61f, 0.07f, 0.065f },
        };

        private static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        public static RiderParts Build(PersonagemConfig c, MotoPalette pal)
        {
            var parts = new RiderParts();
            float s = c.Escala;
            float lean = c.Inclinacao * Mathf.Deg2Rad;
            Vector3 u = V(0f, Mathf.Cos(lean), Mathf.Sin(lean));
            Vector3 f = V(0f, -Mathf.Sin(lean), Mathf.Cos(lean));

            // Seção do tronco em t (já com escala, largura de ombros, profundidade e barriga).
            Sec TorsoSec(float t, float grow)
            {
                float[] a = Table[0];
                float[] b = Table[Table.Length - 1];

                for (int i = 0; i < Table.Length - 1; i++)
                {
                    if (t >= Table[i][0] && t <= Table[i + 1][0])
                    {
                        a = Table[i];
                        b = Table[i + 1];

                        break;
                    }
                }

                float k = Mathf.InverseLerp(a[0], b[0], t);
                float hw = Mathf.Lerp(a[1], b[1], k);
                float hd = Mathf.Lerp(a[2], b[2], k);
                float shoulderK = Mathf.Clamp01(Mathf.InverseLerp(0.1f, 0.4f, t)) * 0.6f + 0.4f;
                hw *= Mathf.Lerp(1f, c.Ombros, shoulderK) * s * 1.12f;
                float belly = c.Barriga * Mathf.Clamp01(1f - Mathf.Abs(t - 0.12f) / 0.22f);
                hd = (hd * c.Peito + belly) * s;
                Vector3 center = u * (t * s) + f * (belly * 0.55f);

                return new Sec(center, X, f, hw * grow, hd * grow, 2.3f);
            }

            var kit = new MotoMeshKit(pal);
            int cJacket = kit.Color(c.Jaqueta, 0.45f);
            int cJeans = kit.Color(c.Jeans, 0.2f);
            int cSkin = kit.Color(c.Pele, 0.35f);
            int cHelmet = kit.Color(c.Capacete, 0.85f);
            int cHelmetAcc = kit.Color(c.CapaceteDetalhe, 0.8f);
            int cVisor = kit.Color(new Color(0.03f, 0.04f, 0.06f), 0.95f);
            int cGlove = kit.Color(c.Luva, 0.35f);
            int cReflect = kit.Color(new Color(0.78f, 0.8f, 0.78f), 0.9f);
            int cShoe = kit.Color(c.Tenis, 0.35f);
            int cSole = kit.Color(new Color(0.82f, 0.82f, 0.8f), 0.3f);
            int cHair = kit.Color(c.Cabelo, 0.4f);

            // Pélvis (jeans) e jaqueta.
            kit.Loft(new List<Sec> { TorsoSec(-0.09f, 1f), TorsoSec(-0.02f, 1.02f), TorsoSec(0.05f, 1.01f) }, 14, cJeans);
            var jacket = new List<Sec>();

            foreach (float t in new[] { 0.02f, 0.09f, 0.16f, 0.24f, 0.32f, 0.4f, 0.47f, 0.52f, 0.56f, 0.585f })
            {
                jacket.Add(TorsoSec(t, t < 0.04f ? 1.045f : 1f));
            }

            kit.Loft(jacket, 14, cJacket);

            // Faixa refletiva no peito/cintura.
            kit.Loft(new List<Sec> { TorsoSec(0.2f, 1.018f), TorsoSec(0.25f, 1.02f) }, 14, cReflect, false, false, false);

            // Capuz do moletom (só o Jefinho).
            if (c.TemCapuz)
            {
                int cHood = kit.Color(c.Moletom, 0.3f);
                kit.Loft(new List<Sec>
                {
                    new Sec(u * (0.52f * s) - f * 0.02f, X, f, 0.12f * s, 0.095f * s, 2.2f),
                    new Sec(u * (0.58f * s) - f * 0.035f, X, f, 0.108f * s, 0.09f * s, 2.2f),
                    new Sec(u * (0.64f * s) - f * 0.03f, X, f, 0.08f * s, 0.07f * s, 2.2f),
                }, 12, cHood);
            }

            // Pescoço e cabeça.
            Vector3 neck = u * (0.6f * s);
            Vector3 hc = neck + V(0f, 0.125f, 0.02f + c.CabecaFrente);
            kit.Tube(new[] { neck - u * 0.03f, hc + V(0f, -0.06f, 0f) }, 0.05f * s, 0.048f * s, 8, cSkin);

            var helmet = new List<Sec>();
            float[][] hs =
            {
                new[] { -0.145f, 0.045f, 0.05f, 0.05f }, new[] { -0.118f, 0.092f, 0.11f, 0.035f }, new[] { -0.06f, 0.12f, 0.14f, 0.015f },
                new[] { 0f, 0.13f, 0.155f, 0f }, new[] { 0.06f, 0.125f, 0.15f, -0.005f }, new[] { 0.115f, 0.1f, 0.12f, -0.01f },
                new[] { 0.15f, 0.05f, 0.065f, -0.01f }, new[] { 0.168f, 0.012f, 0.014f, -0.01f },
            };

            foreach (float[] r in hs)
            {
                helmet.Add(new Sec(hc + V(0f, r[0], r[3]), X, Z, r[1], r[2], 2.3f));
            }

            kit.Loft(helmet, 16, cHelmet);

            // Faixa no alto do capacete e viseira escura.
            var stripe = new List<Sec>();

            foreach (float[] r in new[] { new[] { -0.125f, 0.085f }, new[] { -0.06f, 0.138f }, new[] { 0f, 0.159f }, new[] { 0.06f, 0.146f }, new[] { 0.11f, 0.105f } })
            {
                stripe.Add(new Sec(hc + V(0f, r[1], r[0]), X, Y, 0.016f, 0.004f, 2f));
            }

            kit.Loft(stripe, 6, cHelmetAcc);

            kit.Loft(new List<Sec>
            {
                new Sec(hc + V(0f, 0.015f, 0.05f), X, Y, 0.108f, 0.04f, 3f),
                new Sec(hc + V(0f, 0.015f, 0.115f), X, Y, 0.098f, 0.04f, 3f),
                new Sec(hc + V(0f, 0.015f, 0.145f), X, Y, 0.085f, 0.036f, 3f),
                new Sec(hc + V(0f, 0.015f, 0.163f), X, Y, 0.06f, 0.028f, 3f),
            }, 14, cVisor);

            // Queixeira escura embaixo.
            kit.Loft(new List<Sec>
            {
                new Sec(hc + V(0f, -0.105f, 0.07f), X, Y, 0.05f, 0.012f, 3f),
                new Sec(hc + V(0f, -0.108f, 0.135f), X, Y, 0.03f, 0.01f, 3f),
            }, 8, cVisor);

            if (c.RaboDeCavalo)
            {
                var pts = new[] { hc + V(0f, -0.06f, -0.12f), hc + V(0f, -0.1f, -0.185f), hc + V(0f, -0.2f, -0.2f), hc + V(0f, -0.32f, -0.17f) };
                var w = new[] { 0.032f, 0.03f, 0.03f, 0.012f };
                kit.Loft(MotoMeshKit.Path(pts, w, w, X, 2f, true), 8, cHair);
            }

            parts.Tronco = kit.ToMesh("Tronco");

            // Ombros e quadris (espaço do tronco).
            Sec shoulder = TorsoSec(0.47f, 1f);
            parts.OmbroEsq = shoulder.C + X * (-shoulder.W * 0.9f);
            parts.OmbroDir = shoulder.C + X * (shoulder.W * 0.9f);
            float hipX = 0.092f * Mathf.Lerp(1f, c.Ombros, 0.6f) * s;
            parts.QuadrilEsq = V(-hipX, 0f, 0f);
            parts.QuadrilDir = V(hipX, 0f, 0f);

            // ---------- Braço: manga (jaqueta) e luva ----------

            float la = 0.3f * s;
            float lb = 0.27f * s;
            float lg = 0.085f;
            parts.CompBracoSuperior = la;
            parts.CompAntebraco = lb;
            parts.CompLuva = lg;
            float ar = 0.057f * Mathf.Lerp(1f, c.Ombros, 0.7f) * s;

            var arm = new MotoMeshKit(pal);
            int aJacket = arm.Color(c.Jaqueta, 0.45f);
            int aGlove = arm.Color(c.Luva, 0.35f);
            int aReflect = arm.Color(new Color(0.78f, 0.8f, 0.78f), 0.9f);
            arm.Loft(MotoMeshKit.Path(new[] { Vector3.zero, Z * la }, new[] { ar, ar * 0.88f }, new[] { ar, ar * 0.88f }, Y, 2f, true), 9, aJacket);
            arm.Loft(new List<Sec>
            {
                new Sec(Z * (la * 0.45f), X, Y, ar * 0.97f * 1.02f, ar * 0.97f * 1.02f, 2f),
                new Sec(Z * (la * 0.55f), X, Y, ar * 0.95f * 1.02f, ar * 0.95f * 1.02f, 2f),
            }, 9, aReflect, false, false, false);
            parts.BracoSuperior = arm.ToMesh("BracoSuperior");

            var fore = new MotoMeshKit(pal);
            int fJacket = fore.Color(c.Jaqueta, 0.45f);
            int fGlove = fore.Color(c.Luva, 0.35f);
            float fr = ar * 0.86f;
            fore.Loft(MotoMeshKit.Path(new[] { Vector3.zero, Z * (lb - 0.02f) }, new[] { fr, fr * 0.86f }, new[] { fr, fr * 0.86f }, Y, 2f, true), 9, fJacket);

            // Luva: punho mais largo e mão fechada.
            fore.Loft(MotoMeshKit.Path(new[] { Z * (lb - 0.05f), Z * (lb + 0.02f), Z * (lb + lg - 0.015f) }, new[] { fr * 0.95f, 0.042f, 0.036f }, new[] { fr * 0.95f, 0.038f, 0.034f }, Y, 2.3f, true), 9, fGlove);
            parts.Antebraco = fore.ToMesh("Antebraco");

            // ---------- Perna ----------

            float lt = 0.43f * s;
            float ls = 0.43f * s;
            parts.CompCoxa = lt;
            parts.CompCanela = ls;
            float tr = 0.092f * Mathf.Lerp(1f, c.Ombros, 0.55f) * s;
            var thigh = new MotoMeshKit(pal);
            int tJeans = thigh.Color(c.Jeans, 0.2f);
            thigh.Loft(MotoMeshKit.Path(new[] { Vector3.zero, Z * lt }, new[] { tr, tr * 0.74f }, new[] { tr, tr * 0.74f }, Y, 2f, true), 10, tJeans);
            parts.Coxa = thigh.ToMesh("Coxa");

            var shin = new MotoMeshKit(pal);
            int sJeans = shin.Color(c.Jeans, 0.2f);
            shin.Loft(MotoMeshKit.Path(new[] { Vector3.zero, Z * ls }, new[] { tr * 0.76f, tr * 0.56f }, new[] { tr * 0.76f, tr * 0.56f }, Y, 2f, true), 10, sJeans);
            parts.Canela = shin.ToMesh("Canela");

            // Tênis (tornozelo na origem, sola em -y, bico em +z).
            var shoe = new MotoMeshKit(pal);
            int hShoe = shoe.Color(c.Tenis, 0.35f);
            int hSole = shoe.Color(new Color(0.82f, 0.82f, 0.8f), 0.3f);
            shoe.Loft(new List<Sec>
            {
                new Sec(V(0f, -0.035f, -0.085f), X, Y, 0.034f, 0.05f, 2.4f),
                new Sec(V(0f, -0.032f, -0.04f), X, Y, 0.042f, 0.058f, 2.4f),
                new Sec(V(0f, -0.045f, 0.06f), X, Y, 0.047f, 0.044f, 2.4f),
                new Sec(V(0f, -0.055f, 0.14f), X, Y, 0.048f, 0.034f, 2.4f),
                new Sec(V(0f, -0.062f, 0.2f), X, Y, 0.037f, 0.026f, 2.4f),
            }, 10, hShoe);
            shoe.Loft(new List<Sec>
            {
                new Sec(V(0f, -0.081f, -0.09f), X, Y, 0.036f, 0.012f, 3f),
                new Sec(V(0f, -0.082f, 0.05f), X, Y, 0.05f, 0.012f, 3f),
                new Sec(V(0f, -0.082f, 0.15f), X, Y, 0.05f, 0.012f, 3f),
                new Sec(V(0f, -0.08f, 0.21f), X, Y, 0.038f, 0.012f, 3f),
            }, 10, hSole);
            parts.Tenis = shoe.ToMesh("Tenis");

            parts.Triangulos = kit.Triangles + arm.Triangles + fore.Triangles + thigh.Triangles + shin.Triangles + shoe.Triangles;

            return parts;
        }
    }
}
