using System.Collections.Generic;
using UnityEngine;

namespace Entregador.Game
{
    /// <summary>Malhas e pontos de referência da moto (sistema da moto: x direita, y cima, z frente; chão em y = 0).</summary>
    public sealed class MotoParts
    {
        public Mesh Quadro;
        public Mesh RodaFrente;
        public Mesh RodaTras;
        public Mesh Garfo;
        public Mesh FarolLente;
        public Mesh LanternaLente;
        public Mesh Bau;

        public Vector3 EixoFrente;
        public Vector3 EixoTras;

        /// <summary>Direção do eixo de esterçamento (do garfo para cima e para trás).</summary>
        public Vector3 EixoDirecao;

        public float RaioFrente;
        public float RaioTras;

        /// <summary>Manoplas, no espaço local do garfo (relativo ao eixo da roda da frente).</summary>
        public Vector3 ManopolaEsq;
        public Vector3 ManopolaDir;

        public Vector3 PedaleiraEsq;
        public Vector3 PedaleiraDir;
        public Vector3 Quadril;

        public Vector3 PlacaCentro;
        public Vector3 BauCentro;
        public Vector3 BauTamanho;

        public int Triangulos;
    }

    /// <summary>
    /// Gera a moto: Titã 160 (street clássica) e Tornado 250 (naked esportiva) compartilhando as mesmas peças
    /// (rodas, garfo, guidão, motor, rabeta) com parâmetros e formas diferentes. Tudo em loft/revolução, sem primitivas.
    /// </summary>
    public static class MotoModel
    {
        private static readonly Vector3 X = Vector3.right;
        private static readonly Vector3 Y = Vector3.up;
        private static readonly Vector3 Z = Vector3.forward;

        private static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        private static List<Sec> ZSecs(float[][] rows, float e)
        {
            var list = new List<Sec>();

            foreach (float[] r in rows)
            {
                list.Add(new Sec(V(r.Length > 4 ? r[4] : 0f, r[1], r[0]), X, Y, r[2], r[3], e));
            }

            return list;
        }

        public static MotoParts Build(MotoModelo modelo, MotoCor cor, MotoPalette pal)
        {
            bool tw = modelo == MotoModelo.Twister;
            var p = new MotoParts();

            float wr = tw ? 0.30f : 0.31f;
            float zf = tw ? 0.67f : 0.65f;
            p.RaioFrente = wr;
            p.RaioTras = wr;
            p.EixoFrente = V(0f, wr, zf);
            p.EixoTras = V(0f, wr, -zf);

            float rake = 25f * Mathf.Deg2Rad;
            Vector3 up = V(0f, Mathf.Cos(rake), -Mathf.Sin(rake));
            p.EixoDirecao = up;
            Vector3 axle = p.EixoFrente;

            var quadro = new MotoMeshKit(pal);

            // Cores (a suavidade vai junto na paleta).
            int cPaint = quadro.Color(MotoCatalogo.Pintura(cor), 0.75f);
            int cPaint2 = quadro.Color(MotoCatalogo.Detalhe(cor), 0.7f);
            int cBlack = quadro.Color(new Color(0.045f, 0.045f, 0.05f), 0.4f);
            int cSeat = quadro.Color(new Color(0.03f, 0.03f, 0.035f), 0.25f);
            int cRubber = quadro.Color(new Color(0.03f, 0.03f, 0.03f), 0.12f);
            int cChrome = quadro.Color(new Color(0.88f, 0.89f, 0.91f), 0.9f);
            int cSteel = quadro.Color(new Color(0.55f, 0.56f, 0.58f), 0.6f);
            int cEngine = quadro.Color(tw ? new Color(0.13f, 0.13f, 0.14f) : new Color(0.3f, 0.31f, 0.33f), 0.5f);
            int cDarkMetal = quadro.Color(new Color(0.07f, 0.07f, 0.08f), 0.55f);
            int cWhite = quadro.Color(new Color(0.92f, 0.92f, 0.9f), 0.5f);
            int cBlue = quadro.Color(new Color(0.04f, 0.2f, 0.62f), 0.5f);
            int cPlateText = quadro.Color(new Color(0.03f, 0.03f, 0.03f), 0.3f);

            // ---------- Quadro e carenagens ----------

            // Tanque (loft em Z). Titã: reto e simples; Tornado: mais alto, esculpido, com abas.
            float[][] tank = tw
                ? new[]
                {
                    new[] { 0.34f, 0.865f, 0.055f, 0.055f }, new[] { 0.28f, 0.885f, 0.105f, 0.09f }, new[] { 0.13f, 0.9f, 0.16f, 0.112f },
                    new[] { -0.03f, 0.885f, 0.16f, 0.105f }, new[] { -0.14f, 0.85f, 0.125f, 0.07f }, new[] { -0.2f, 0.83f, 0.085f, 0.045f },
                }
                : new[]
                {
                    new[] { 0.3f, 0.875f, 0.055f, 0.05f }, new[] { 0.26f, 0.88f, 0.1f, 0.078f }, new[] { 0.15f, 0.875f, 0.142f, 0.092f },
                    new[] { 0.02f, 0.862f, 0.148f, 0.092f }, new[] { -0.1f, 0.85f, 0.128f, 0.068f }, new[] { -0.17f, 0.845f, 0.09f, 0.045f },
                };
            quadro.Loft(ZSecs(tank, tw ? 2.8f : 2.5f), 14, cPaint);

            // Selo/decal lateral do tanque (pastilha arredondada) e joelheiras.
            foreach (float sx in new[] { -1f, 1f })
            {
                float tx = sx * (tw ? 0.158f : 0.147f);
                var decal = new List<Sec>
                {
                    new Sec(V(tx - sx * 0.008f, 0.875f, 0.1f), Z, Y, 0.11f, 0.034f, 3f),
                    new Sec(V(tx + sx * 0.004f, 0.875f, 0.1f), Z, Y, 0.1f, 0.03f, 3f),
                };
                quadro.Loft(decal, 12, cPaint2);

                var pad = new List<Sec>
                {
                    new Sec(V(sx * 0.12f, 0.8f, 0.08f), Z, Y, 0.09f, 0.04f, 3f),
                    new Sec(V(sx * (tw ? 0.158f : 0.146f), 0.8f, 0.08f), Z, Y, 0.085f, 0.036f, 3f),
                };
                quadro.Loft(pad, 10, cRubber);
            }

            if (tw)
            {
                // Abas do tanque (shrouds) em cor de detalhe.
                foreach (float sx in new[] { -1f, 1f })
                {
                    quadro.Loft(ShroudSecs(sx), 10, cPaint2);
                }
            }

            // Banco.
            if (tw)
            {
                quadro.Loft(ZSecs(new[]
                {
                    new[] { 0.12f, 0.765f, 0.06f, 0.03f }, new[] { 0.0f, 0.77f, 0.14f, 0.033f }, new[] { -0.15f, 0.772f, 0.145f, 0.033f }, new[] { -0.29f, 0.775f, 0.13f, 0.03f },
                }, 3f), 12, cSeat);
                quadro.Loft(ZSecs(new[]
                {
                    new[] { -0.3f, 0.815f, 0.1f, 0.035f }, new[] { -0.42f, 0.822f, 0.105f, 0.038f }, new[] { -0.55f, 0.825f, 0.095f, 0.035f }, new[] { -0.62f, 0.82f, 0.07f, 0.03f },
                }, 3f), 12, cSeat);
            }
            else
            {
                quadro.Loft(ZSecs(new[]
                {
                    new[] { 0.14f, 0.765f, 0.055f, 0.028f }, new[] { 0.08f, 0.766f, 0.1f, 0.03f }, new[] { -0.02f, 0.768f, 0.145f, 0.034f },
                    new[] { -0.3f, 0.772f, 0.14f, 0.034f }, new[] { -0.5f, 0.777f, 0.125f, 0.03f }, new[] { -0.62f, 0.775f, 0.1f, 0.027f },
                }, 3f), 12, cSeat);
            }

            // Laterais do banco (tampas).
            foreach (float sx in new[] { -1f, 1f })
            {
                CoverLoft(quadro, sx, cPaint);
            }

            if (tw)
            {
                // Rabeta angular do Tornado: cobre o banco do garupa.
                quadro.Loft(ZSecs(new[]
                {
                    new[] { -0.3f, 0.745f, 0.095f, 0.05f }, new[] { -0.5f, 0.76f, 0.085f, 0.045f }, new[] { -0.7f, 0.79f, 0.07f, 0.04f }, new[] { -0.88f, 0.82f, 0.045f, 0.028f },
                }, 3f), 12, cPaint);
            }

            // Para-lama traseiro (arco em volta da roda de trás) e suporte da placa.
            var fenderProfile = MotoMeshKit.RoundProfile(wr + 0.036f, 0f, 0.012f, 0.056f, 6, 3f);
            quadro.Revolve(fenderProfile, p.EixoTras, X, Y, -62f, 40f, 14, cBlack);

            // Direção: cabeça do quadro (fixa) e tubo diagonal até o motor.
            quadro.Tube(axle + up * 0.5f, axle + up * 0.8f, 0.027f, 8, cBlack);
            quadro.Tube(new[] { axle + up * 0.62f + V(0f, 0f, -0.03f), V(0f, 0.55f, 0.18f), V(0f, 0.33f, 0.1f) }, 0.022f, 0.022f, 7, cBlack);

            // ---------- Motor ----------

            var crank = new List<Sec>
            {
                new Sec(V(0f, 0.29f, 0.15f), X, Y, 0.07f, 0.07f, 3f),
                new Sec(V(0f, 0.29f, 0.1f), X, Y, 0.112f, 0.118f, 3f),
                new Sec(V(0f, 0.29f, -0.1f), X, Y, 0.118f, 0.122f, 3f),
                new Sec(V(0f, 0.285f, -0.23f), X, Y, 0.1f, 0.1f, 3f),
            };
            quadro.Loft(crank, 12, cEngine, true);

            foreach (float sx in new[] { -1f, 1f })
            {
                var cover = new List<Sec>
                {
                    new Sec(V(sx * 0.112f, 0.29f, -0.02f), Z, Y, 0.06f, 0.06f, 2f),
                    new Sec(V(sx * 0.132f, 0.29f, -0.02f), Z, Y, 0.052f, 0.052f, 2f),
                };
                quadro.Loft(cover, 12, cSteel, true);
            }

            float cyl = tw ? 1.12f : 1f;
            float tilt = 14f * Mathf.Deg2Rad;
            Vector3 cAxis = V(0f, Mathf.Cos(tilt), Mathf.Sin(tilt));
            Vector3 cPerp = V(0f, -Mathf.Sin(tilt), Mathf.Cos(tilt));
            Vector3 cBase = V(0f, 0.36f, 0.05f);
            var fins = new List<Sec>();

            for (int i = 0; i < 4; i++)
            {
                float s = 0.02f + i * 0.045f;
                fins.Add(new Sec(cBase + cAxis * (s - 0.012f), X, cPerp, 0.062f * cyl, 0.062f * cyl, 2f));
                fins.Add(new Sec(cBase + cAxis * (s - 0.005f), X, cPerp, 0.088f * cyl, 0.088f * cyl, 2f));
                fins.Add(new Sec(cBase + cAxis * (s + 0.005f), X, cPerp, 0.088f * cyl, 0.088f * cyl, 2f));
                fins.Add(new Sec(cBase + cAxis * (s + 0.012f), X, cPerp, 0.062f * cyl, 0.062f * cyl, 2f));
            }

            quadro.Loft(fins, 12, cEngine, true);
            quadro.Loft(new List<Sec>
            {
                new Sec(cBase + cAxis * 0.2f, X, cPerp, 0.085f * cyl, 0.08f * cyl, 2.6f),
                new Sec(cBase + cAxis * 0.235f, X, cPerp, 0.095f * cyl, 0.085f * cyl, 2.6f),
                new Sec(cBase + cAxis * 0.27f, X, cPerp, 0.085f * cyl, 0.07f * cyl, 2.6f),
            }, 12, cDarkMetal, true);

            // ---------- Escapamento (lado direito) ----------

            var header = new[] { V(0.02f, 0.52f, 0.17f), V(0.045f, 0.43f, 0.215f), V(0.1f, 0.3f, 0.17f), V(0.12f, 0.22f, 0.05f), V(0.125f, 0.205f, -0.1f), V(0.125f, 0.21f, -0.24f) };

            if (tw)
            {
                header = new[] { V(0.02f, 0.52f, 0.17f), V(0.05f, 0.4f, 0.23f), V(0.09f, 0.23f, 0.19f), V(0.08f, 0.15f, 0.08f) };
                quadro.Tube(header, 0.016f, 0.016f, 8, cChrome, true);
                quadro.Loft(ZSecs(new[]
                {
                    new[] { 0.14f, 0.15f, 0.045f, 0.045f, 0.075f }, new[] { 0.05f, 0.15f, 0.07f, 0.065f, 0.075f }, new[] { -0.2f, 0.152f, 0.075f, 0.065f, 0.075f },
                    new[] { -0.38f, 0.16f, 0.06f, 0.055f, 0.08f }, new[] { -0.42f, 0.165f, 0.04f, 0.04f, 0.082f },
                }, 2.4f), 14, cDarkMetal, true);
                quadro.Loft(ZSecs(new[] { new[] { -0.4f, 0.168f, 0.032f, 0.032f, 0.085f }, new[] { -0.47f, 0.172f, 0.034f, 0.034f, 0.085f } }, 2f), 10, cChrome, true);
            }
            else
            {
                quadro.Tube(header, 0.016f, 0.016f, 8, cChrome, true);
                quadro.Loft(ZSecs(new[]
                {
                    new[] { -0.2f, 0.212f, 0.024f, 0.024f, 0.127f }, new[] { -0.3f, 0.22f, 0.052f, 0.052f, 0.13f }, new[] { -0.55f, 0.24f, 0.058f, 0.058f, 0.13f },
                    new[] { -0.75f, 0.262f, 0.055f, 0.055f, 0.13f }, new[] { -0.8f, 0.268f, 0.035f, 0.035f, 0.13f },
                }, 2f), 14, cChrome, true);
                quadro.Loft(ZSecs(new[]
                {
                    new[] { -0.32f, 0.227f, 0.057f, 0.057f, 0.13f }, new[] { -0.4f, 0.232f, 0.062f, 0.062f, 0.13f }, new[] { -0.6f, 0.249f, 0.062f, 0.062f, 0.13f }, new[] { -0.66f, 0.254f, 0.057f, 0.057f, 0.13f },
                }, 2f), 14, cDarkMetal, true);
            }

            // ---------- Balança, correia (corrente) e amortecedores ----------

            foreach (float sx in new[] { -1f, 1f })
            {
                quadro.Loft(new List<Sec>
                {
                    new Sec(V(sx * 0.085f, 0.3f, -0.22f), X, Y, 0.022f, 0.03f, 3f),
                    new Sec(V(sx * 0.085f, 0.305f, -0.44f), X, Y, 0.02f, 0.026f, 3f),
                    new Sec(V(sx * 0.085f, wr, p.EixoTras.z), X, Y, 0.02f, 0.025f, 3f),
                }, 8, cDarkMetal, true);

                // Amortecedor: haste cromada embaixo, mola preta em cima.
                Vector3 lo = V(sx * 0.125f, 0.335f, p.EixoTras.z + 0.07f);
                Vector3 hi = V(sx * 0.118f, 0.72f, p.EixoTras.z + 0.2f);
                Vector3 dir = (hi - lo).normalized;
                quadro.Tube(lo, lo + dir * 0.14f, 0.011f, 6, cChrome, true, true);
                var spring = new List<Vector3>();
                var rad = new List<float>();

                for (int i = 0; i <= 8; i++)
                {
                    spring.Add(Vector3.Lerp(lo + dir * 0.1f, hi, i / 8f));
                    rad.Add(i % 2 == 0 ? 0.03f : 0.025f);
                }

                quadro.Loft(MotoMeshKit.Path(spring, rad, rad, X, 2f, true), 8, cBlack);
            }

            // Pinhão e corrente (lado esquerdo).
            float chainX = -0.07f;
            Vector3 pinion = V(chainX, 0.285f, -0.2f);
            quadro.Revolve(new[] { new Vector2(0.01f, -0.006f), new Vector2(0.034f, -0.006f), new Vector2(0.034f, 0.006f), new Vector2(0.01f, 0.006f) }, pinion, X, Y, 0f, 360f, 12, cSteel, true);
            Vector3 sprocketC = V(chainX, wr, p.EixoTras.z);
            float sr = tw ? 0.082f : 0.088f;
            quadro.Tube(pinion + V(0f, 0.034f, 0f), sprocketC + V(0f, sr, 0f), 0.006f, 5, cDarkMetal, true);
            quadro.Tube(pinion + V(0f, -0.034f, 0f), sprocketC + V(0f, -sr, 0f), 0.006f, 5, cDarkMetal, true);

            // Rack traseiro e alça.
            foreach (float sx in new[] { -1f, 1f })
            {
                float ry = tw ? 0.855f : 0.805f;
                quadro.Tube(new[] { V(sx * 0.12f, tw ? 0.8f : 0.73f, -0.42f), V(sx * 0.17f, ry, -0.55f), V(sx * 0.17f, ry, -1f) }, 0.009f, 0.009f, 6, cDarkMetal, true);
                quadro.Tube(V(sx * 0.17f, ry, -0.98f), V(sx * 0.1f, 0.66f, -0.88f), 0.008f, 5, cDarkMetal, true);
            }

            quadro.Tube(V(-0.17f, tw ? 0.855f : 0.805f, -1f), V(0.17f, tw ? 0.855f : 0.805f, -1f), 0.009f, 6, cDarkMetal, true);
            quadro.Tube(V(-0.17f, tw ? 0.855f : 0.805f, -0.6f), V(0.17f, tw ? 0.855f : 0.805f, -0.6f), 0.009f, 6, cDarkMetal, true);

            // Pedaleiras.
            p.PedaleiraEsq = V(-0.19f, tw ? 0.36f : 0.335f, tw ? 0.03f : 0.06f);
            p.PedaleiraDir = V(0.19f, tw ? 0.36f : 0.335f, tw ? 0.03f : 0.06f);
            quadro.Tube(V(-0.12f, p.PedaleiraEsq.y, p.PedaleiraEsq.z), V(-0.25f, p.PedaleiraEsq.y, p.PedaleiraEsq.z), 0.011f, 6, cChrome, true, true);
            quadro.Tube(V(0.12f, p.PedaleiraDir.y, p.PedaleiraDir.z), V(0.25f, p.PedaleiraDir.y, p.PedaleiraDir.z), 0.011f, 6, cChrome, true, true);
            quadro.Tube(V(-0.12f, p.PedaleiraEsq.y, p.PedaleiraEsq.z), V(-0.12f, 0.3f, p.PedaleiraEsq.z - 0.06f), 0.012f, 6, cDarkMetal, true);
            quadro.Tube(V(0.12f, p.PedaleiraDir.y, p.PedaleiraDir.z), V(0.12f, 0.3f, p.PedaleiraDir.z - 0.06f), 0.012f, 6, cDarkMetal, true);

            // Placa Mercosul (branca, faixa azul no topo) e lanterna.
            p.PlacaCentro = V(0f, 0.57f, -0.98f);
            quadro.Loft(new List<Sec>
            {
                new Sec(V(0f, 0.57f, -0.966f), X, Y, 0.2f, 0.065f, 7f),
                new Sec(V(0f, 0.57f, -0.978f), X, Y, 0.2f, 0.065f, 7f),
            }, 14, cWhite);
            quadro.Loft(new List<Sec>
            {
                new Sec(V(0f, 0.6145f, -0.968f), X, Y, 0.196f, 0.0105f, 4f),
                new Sec(V(0f, 0.6145f, -0.9788f), X, Y, 0.196f, 0.0105f, 4f),
            }, 10, cBlue);
            quadro.Tube(new[] { V(0f, 0.7f, -0.82f), V(0f, 0.62f, -0.95f) }, 0.012f, 0.012f, 6, cBlack);
            quadro.Loft(new List<Sec>
            {
                new Sec(V(0f, 0.7f, -0.9f), X, Y, 0.075f, 0.034f, 3f),
                new Sec(V(0f, 0.7f, -0.945f), X, Y, 0.078f, 0.036f, 3f),
            }, 10, cBlack);

            // ---------- Baú de entrega (cubo ~45 cm, cantos arredondados) ----------

            float bagY = tw ? 0.875f : 0.825f;
            float bagZ = -0.83f;
            float half = 0.23f;
            p.BauCentro = V(0f, bagY + 0.22f, bagZ);
            p.BauTamanho = V(half * 2f, 0.44f, half * 2f);
            var bag = new MotoMeshKit(pal);
            int cBag = bag.Color(Color.white, 0.35f);
            bag.Loft(new List<Sec>
            {
                new Sec(V(0f, bagY, bagZ), X, Z, half * 0.9f, half * 0.9f, 5f),
                new Sec(V(0f, bagY + 0.03f, bagZ), X, Z, half * 0.99f, half * 0.99f, 5f),
                new Sec(V(0f, bagY + 0.41f, bagZ), X, Z, half * 0.99f, half * 0.99f, 5f),
                new Sec(V(0f, bagY + 0.44f, bagZ), X, Z, half * 0.9f, half * 0.9f, 5f),
            }, 20, cBag);
            p.Bau = bag.ToMesh("Bau");

            // Faixa escura da tampa e pastilhas brancas do logo (laterais e traseira) no quadro.
            int cLid = quadro.Color(new Color(0.08f, 0.08f, 0.09f), 0.4f);
            quadro.Loft(new List<Sec>
            {
                new Sec(V(0f, bagY + 0.355f, bagZ), X, Z, half * 1.003f, half * 1.003f, 5f),
                new Sec(V(0f, bagY + 0.372f, bagZ), X, Z, half * 1.003f, half * 1.003f, 5f),
            }, 20, cLid, false, false, false);

            foreach (float sx in new[] { -1f, 1f })
            {
                quadro.Loft(new List<Sec>
                {
                    new Sec(V(sx * (half * 0.99f), bagY + 0.19f, bagZ), Z, Y, 0.15f, 0.08f, 3.5f),
                    new Sec(V(sx * (half * 1.0f + 0.004f), bagY + 0.19f, bagZ), Z, Y, 0.15f, 0.08f, 3.5f),
                }, 14, cWhite);
            }

            quadro.Loft(new List<Sec>
            {
                new Sec(V(0f, bagY + 0.19f, bagZ - half * 0.99f), X, Y, 0.15f, 0.08f, 3.5f),
                new Sec(V(0f, bagY + 0.19f, bagZ - half * 1.0f - 0.004f), X, Y, 0.15f, 0.08f, 3.5f),
            }, 14, cWhite);

            // ---------- Lanterna (lente emissiva) ----------

            var tail = new MotoMeshKit(pal);
            int cT = tail.Color(Color.white, 0.8f);
            tail.Loft(new List<Sec>
            {
                new Sec(V(0f, 0.7f, -0.942f), X, Y, 0.066f, 0.03f, 3f),
                new Sec(V(0f, 0.7f, -0.956f), X, Y, 0.062f, 0.027f, 3f),
            }, 12, cT);
            p.LanternaLente = tail.ToMesh("LanternaLente");

            // ---------- Rodas ----------

            p.RodaFrente = Wheel(pal, wr, tw ? 0.105f : 0.085f, tw, true);
            p.RodaTras = Wheel(pal, wr, tw ? 0.14f : 0.095f, tw, false);

            // ---------- Garfo, guidão, farol (giram juntos no esterçamento) ----------

            var fork = new MotoMeshKit(pal) { Origin = axle };
            int fBlack = fork.Color(new Color(0.045f, 0.045f, 0.05f), 0.4f);
            int fRubber = fork.Color(new Color(0.03f, 0.03f, 0.03f), 0.12f);
            int fChrome = fork.Color(new Color(0.88f, 0.89f, 0.91f), 0.9f);
            int fDark = fork.Color(new Color(0.07f, 0.07f, 0.08f), 0.55f);
            int fSteel = fork.Color(new Color(0.55f, 0.56f, 0.58f), 0.6f);
            int fPaint = fork.Color(MotoCatalogo.Pintura(cor), 0.75f);
            int fSmoke = fork.Color(new Color(0.05f, 0.06f, 0.08f), 0.9f);
            int fDial = fork.Color(new Color(0.12f, 0.2f, 0.28f), 0.85f);

            foreach (float sx in new[] { -1f, 1f })
            {
                Vector3 o = X * (sx * 0.085f);

                if (tw)
                {
                    fork.Tube(axle + o, axle + o + up * 0.3f, 0.02f, 8, fDark, true);
                    fork.Tube(axle + o + up * 0.28f, axle + o + up * 0.68f, 0.027f, 10, fChrome, true);
                }
                else
                {
                    fork.Tube(axle + o, axle + o + up * 0.3f, 0.0215f, 8, fChrome, true);
                    fork.Tube(axle + o + up * 0.3f, axle + o + up * 0.52f, 0.029f, 8, fRubber);
                    fork.Tube(axle + o + up * 0.5f, axle + o + up * 0.68f, 0.0195f, 8, fChrome, true);
                }

                // Pinça do freio.
                fork.Loft(new List<Sec>
                {
                    new Sec(axle + V(sx * 0.055f, -0.03f, -0.115f), X, Y, 0.016f, 0.03f, 3f),
                    new Sec(axle + V(sx * 0.055f, -0.03f, -0.085f), X, Y, 0.016f, 0.03f, 3f),
                }, 8, fDark, true);
            }

            // Pontes do garfo (a de baixo sustenta o para-lama).
            fork.Tube(axle + up * 0.46f - X * 0.09f, axle + up * 0.46f + X * 0.09f, 0.014f, 6, fDark);
            fork.Tube(axle + up * 0.66f - X * 0.09f, axle + up * 0.66f + X * 0.09f, 0.018f, 8, fDark);
            var fenderF = MotoMeshKit.RoundProfile(wr + 0.024f, 0f, 0.01f, 0.052f, 6, 3f);
            fork.Revolve(fenderF, axle, X, Y, -28f, 72f, 14, tw ? fPaint : fBlack);

            // Guidão: tubo com ligeira inclinação para trás nas pontas.
            float bt = tw ? 0.74f : 0.78f;
            Vector3 bc = axle + up * bt;
            fork.Tube(axle + up * 0.64f, bc, 0.016f, 7, fDark);
            float hw = tw ? 0.335f : 0.355f;
            var barPts = new List<Vector3>();
            float[] bx = { -1f, -0.7f, -0.34f, 0f, 0.34f, 0.7f, 1f };

            foreach (float k in bx)
            {
                float yy = tw ? 0.012f * Mathf.Abs(k) : 0.026f * Mathf.Abs(k);
                float zz = -0.062f * Mathf.Pow(Mathf.Abs(k), 1.5f);
                barPts.Add(bc + V(k * hw, yy, zz));
            }

            fork.Tube(barPts, 0.0115f, 0.0115f, 6, tw ? fDark : fChrome, !tw);

            Vector3 gl = bc + V(-0.31f, tw ? 0.011f : 0.02f, -0.048f);
            Vector3 gr = bc + V(0.31f, tw ? 0.011f : 0.02f, -0.048f);
            p.ManopolaEsq = gl - axle;
            p.ManopolaDir = gr - axle;

            foreach (float sx in new[] { -1f, 1f })
            {
                Vector3 g = sx < 0f ? gl : gr;
                fork.Tube(g + X * (sx * 0.065f), g - X * (sx * 0.05f), 0.0175f, 8, fRubber, false, true);

                // Alavanca (embreagem/freio) e base.
                fork.Tube(new[] { g - X * (sx * 0.07f) + V(0f, 0.004f, 0.012f), g - X * (sx * 0.055f) + V(0f, -0.004f, 0.1f) }, 0.006f, 0.004f, 5, fChrome, true, true);
                fork.Loft(new List<Sec>
                {
                    new Sec(g - X * (sx * 0.1f) + V(0f, -0.004f, -0.003f), X, Y, 0.022f, 0.016f, 3f),
                    new Sec(g - X * (sx * 0.14f) + V(0f, -0.004f, -0.003f), X, Y, 0.022f, 0.016f, 3f),
                }, 8, fBlack);

                // Retrovisor: haste, corpo e vidro cromado.
                Vector3 sb = bc + V(sx * 0.3f, 0.025f, -0.05f);
                Vector3 st = bc + V(sx * (tw ? 0.36f : 0.37f), tw ? 0.2f : 0.23f, -0.085f);
                fork.Tube(sb, st, 0.006f, 5, fBlack, false, true);
                fork.Loft(new List<Sec>
                {
                    new Sec(st + V(0f, 0.012f, 0.012f), X, Y, 0.032f, 0.022f, 2.2f),
                    new Sec(st + V(0f, 0.012f, -0.004f), X, Y, 0.049f, 0.033f, 2.2f),
                    new Sec(st + V(0f, 0.012f, -0.014f), X, Y, 0.047f, 0.031f, 2.2f),
                }, 10, fBlack);
                fork.Loft(new List<Sec>
                {
                    new Sec(st + V(0f, 0.012f, -0.013f), X, Y, 0.042f, 0.026f, 2.2f),
                    new Sec(st + V(0f, 0.012f, -0.0185f), X, Y, 0.04f, 0.024f, 2.2f),
                }, 10, fChrome, true);
            }

            // Painel de instrumentos.
            fork.Loft(new List<Sec>
            {
                new Sec(bc + V(0f, 0.035f, 0.01f), X, Z, tw ? 0.055f : 0.05f, 0.035f, 2.6f),
                new Sec(bc + V(0f, 0.07f, -0.0f), X, Z, tw ? 0.05f : 0.046f, 0.032f, 2.6f),
                new Sec(bc + V(0f, 0.08f, -0.012f), X, Z, tw ? 0.045f : 0.04f, 0.025f, 2.6f),
            }, 12, fBlack);
            fork.Loft(new List<Sec>
            {
                new Sec(bc + V(0f, 0.071f, -0.026f), X, Y, tw ? 0.04f : 0.034f, 0.026f, 2.4f),
                new Sec(bc + V(0f, 0.073f, -0.0305f), X, Y, tw ? 0.036f : 0.03f, 0.022f, 2.4f),
            }, 10, fDial);

            // Farol.
            var lens = new MotoMeshKit(pal) { Origin = axle };
            int lc = lens.Color(Color.white, 0.8f);
            Vector3 hl = V(0f, tw ? 0.94f : 0.93f, tw ? 0.47f : 0.465f);

            foreach (float sx in new[] { -1f, 1f })
            {
                fork.Tube(V(sx * 0.085f, hl.y - 0.04f, axle.z + (hl.y - 0.04f - axle.y) / up.y * up.z), V(sx * 0.082f, hl.y - 0.015f, hl.z - 0.03f), 0.01f, 6, fDark);
            }

            if (tw)
            {
                fork.Loft(new List<Sec>
                {
                    new Sec(hl + V(0f, -0.005f, -0.1f), X, Y, 0.06f, 0.05f, 3.2f),
                    new Sec(hl + V(0f, 0f, -0.05f), X, Y, 0.098f, 0.075f, 3.2f),
                    new Sec(hl + V(0f, 0.002f, 0.03f), X, Y, 0.108f, 0.082f, 3.2f),
                    new Sec(hl + V(0f, 0f, 0.07f), X, Y, 0.092f, 0.07f, 3.2f),
                }, 14, fPaint);
                lens.Loft(new List<Sec>
                {
                    new Sec(hl + V(0f, 0f, 0.064f), X, Y, 0.085f, 0.054f, 3.4f),
                    new Sec(hl + V(0f, 0f, 0.079f), X, Y, 0.078f, 0.048f, 3.4f),
                }, 14, lc);

                // Mini carenagem/para-brisa fumê sobre o farol.
                fork.Loft(new List<Sec>
                {
                    new Sec(hl + V(0f, 0.065f, -0.02f), X, Z, 0.07f, 0.003f, 3f),
                    new Sec(hl + V(0f, 0.15f, -0.075f), X, Z, 0.06f, 0.003f, 3f),
                }, 12, fSmoke);
            }
            else
            {
                var housing = new[] { new Vector2(0.0f, -0.075f), new Vector2(0.05f, -0.074f), new Vector2(0.082f, -0.04f), new Vector2(0.09f, 0.01f), new Vector2(0.088f, 0.052f), new Vector2(0.0f, 0.052f) };
                fork.Revolve(housing, hl, Z, X, 0f, 360f, 18, fBlack);
                var ring = new[] { new Vector2(0.078f, 0.044f), new Vector2(0.095f, 0.046f), new Vector2(0.095f, 0.068f), new Vector2(0.081f, 0.068f) };
                fork.Revolve(ring, hl, Z, X, 0f, 360f, 18, fChrome, true);
                var dome = new[] { new Vector2(0.0f, 0.074f), new Vector2(0.045f, 0.071f), new Vector2(0.079f, 0.062f), new Vector2(0.079f, 0.05f), new Vector2(0.0f, 0.05f) };
                lens.Revolve(dome, hl, Z, X, 0f, 360f, 18, lc);
            }

            p.Garfo = fork.ToMesh("Garfo");
            p.FarolLente = lens.ToMesh("FarolLente");
            p.Quadril = V(0f, 0.865f, -0.15f);

            p.Quadro = quadro.ToMesh("Quadro");
            p.Triangulos = quadro.Triangles + fork.Triangles + lens.Triangles + tail.Triangles + bag.Triangles;

            return p;
        }

        private static List<Sec> ShroudSecs(float sx)
        {
            return new List<Sec>
            {
                new Sec(V(sx * 0.152f, 0.79f, 0.31f), X, Y, 0.022f, 0.025f, 2.5f),
                new Sec(V(sx * 0.158f, 0.785f, 0.2f), X, Y, 0.03f, 0.06f, 2.5f),
                new Sec(V(sx * 0.158f, 0.78f, 0.02f), X, Y, 0.028f, 0.055f, 2.5f),
                new Sec(V(sx * 0.15f, 0.78f, -0.05f), X, Y, 0.02f, 0.035f, 2.5f),
            };
        }

        /// <summary>Tampa lateral longa sob o banco.</summary>
        private static void CoverLoft(MotoMeshKit kit, float sx, int cell)
        {
            var secs = new List<Sec>
            {
                new Sec(V(sx * 0.1f, 0.665f, 0.03f), X, Y, 0.012f, 0.03f, 3f),
                new Sec(V(sx * 0.105f, 0.67f, -0.04f), X, Y, 0.035f, 0.085f, 3f),
                new Sec(V(sx * 0.105f, 0.672f, -0.3f), X, Y, 0.035f, 0.088f, 3f),
                new Sec(V(sx * 0.095f, 0.685f, -0.52f), X, Y, 0.022f, 0.05f, 3f),
            };

            kit.Loft(secs, 12, cell);
        }

        /// <summary>Roda: pneu de perfil arredondado, aro, raios fundidos (cast), cubo e disco/tambor.</summary>
        private static Mesh Wheel(MotoPalette pal, float radius, float width, bool tw, bool front)
        {
            var kit = new MotoMeshKit(pal);
            int cRubber = kit.Color(new Color(0.03f, 0.03f, 0.03f), 0.12f);
            int cRim = kit.Color(tw ? new Color(0.09f, 0.09f, 0.1f) : new Color(0.72f, 0.73f, 0.75f), tw ? 0.7f : 0.75f);
            int cDisc = kit.Color(new Color(0.7f, 0.71f, 0.73f), 0.8f);
            int cDark = kit.Color(new Color(0.07f, 0.07f, 0.08f), 0.55f);

            float bead = 0.2246f;
            kit.Revolve(MotoMeshKit.RoundProfile((radius + bead) * 0.5f, 0f, (radius - bead) * 0.5f, width * 0.5f, 10, 3f), Vector3.zero, X, Y, 0f, 360f, 26, cRubber);
            kit.Revolve(new[] { new Vector2(0.2f, -0.03f), new Vector2(0.23f, -0.036f), new Vector2(0.23f, 0.036f), new Vector2(0.2f, 0.03f) }, Vector3.zero, X, Y, 0f, 360f, 26, cRim, true);
            kit.Revolve(new[] { new Vector2(0f, -0.032f), new Vector2(0.038f, -0.032f), new Vector2(0.046f, -0.018f), new Vector2(0.046f, 0.018f), new Vector2(0.038f, 0.032f), new Vector2(0f, 0.032f) }, Vector3.zero, X, Y, 0f, 360f, 10, cRim, true);

            int spokes = tw ? 6 : 5;

            for (int i = 0; i < spokes; i++)
            {
                float a = Mathf.PI * 2f * i / spokes + 0.3f;
                Vector3 d = V(0f, Mathf.Cos(a), Mathf.Sin(a));
                var pts = new[] { d * 0.04f, d * 0.12f, d * 0.205f };
                var w = tw ? new[] { 0.022f, 0.016f, 0.028f } : new[] { 0.02f, 0.013f, 0.022f };
                var h = new[] { 0.016f, 0.012f, 0.016f };
                kit.Loft(MotoMeshKit.Path(pts, w, h, X, 2.6f), 6, cRim, true);
            }

            if (front)
            {
                kit.Revolve(new[] { new Vector2(0.06f, 0.041f), new Vector2(0.138f, 0.041f), new Vector2(0.138f, 0.0455f), new Vector2(0.06f, 0.0455f) }, Vector3.zero, X, Y, 0f, 360f, 22, cDisc, true);
            }
            else
            {
                float sr = tw ? 0.082f : 0.088f;
                kit.Revolve(new[] { new Vector2(0.045f, -0.04f), new Vector2(sr, -0.04f), new Vector2(sr, -0.045f), new Vector2(0.045f, -0.045f) }, Vector3.zero, X, Y, 0f, 360f, 18, cDark, true);

                if (tw)
                {
                    kit.Revolve(new[] { new Vector2(0.06f, 0.041f), new Vector2(0.115f, 0.041f), new Vector2(0.115f, 0.0455f), new Vector2(0.06f, 0.0455f) }, Vector3.zero, X, Y, 0f, 360f, 20, cDisc, true);
                }
                else
                {
                    kit.Revolve(new[] { new Vector2(0f, 0.034f), new Vector2(0.088f, 0.034f), new Vector2(0.088f, 0.056f), new Vector2(0f, 0.056f) }, Vector3.zero, X, Y, 0f, 360f, 16, cDark);
                }
            }

            return kit.ToMesh(front ? "RodaFrente" : "RodaTras");
        }
    }
}
