using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Entregador.Editor
{
    /// <summary>Quadro local de uma fachada: O = canto inferior esquerdo da frente (no chão), F = para fora, R = direita de quem olha.</summary>
    public struct Frame
    {
        public Vector3 O;
        public Vector3 F;
        public Vector3 R;

        public Frame(Vector3 o, Vector3 f)
        {
            O = o;
            F = f;
            R = Vector3.Cross(f, Vector3.up);
        }

        /// <summary>Ponto em x (ao longo da fachada), y (altura) e d (profundidade para dentro do prédio; negativo = para fora).</summary>
        public Vector3 Pt(float x, float y, float d) => O + R * x + Vector3.up * y - F * d;

        public Frame Back(float d) => new Frame(O - F * d, F);
    }

    /// <summary>Abertura numa parede (janela, porta, vitrine): x e y do canto inferior esquerdo, largura, altura e recuo (reveal).</summary>
    public sealed class Hole
    {
        public float X;
        public float Y;
        public float W;
        public float H;
        public float E;

        public Hole(float x, float y, float w, float h, float e)
        {
            X = x;
            Y = y;
            W = w;
            H = h;
            E = e;
        }
    }

    /// <summary>Contexto de geração de um quarteirão: a malha fundida, o gerador aleatório e onde pendurar os letreiros em TextMeshPro.</summary>
    public sealed class BuildingContext
    {
        public MeshBatch B;
        public System.Random Rnd;
        public Transform Signs;
        public int SignCount;

        /// <summary>Detalhe reduzido (quarteirões longe do largo): sem recuos, grades, ar-condicionado e letreiros em texto.</summary>
        public bool Lite;

        /// <summary>Lado esquerdo/direito do lote exposto (esquina ou vão): ganha janelas na parede lateral.</summary>
        public bool ExposeLeft;
        public bool ExposeRight;
    }

    /// <summary>
    /// Gerador procedural de prédios de São Paulo (determinístico pela semente do System.Random): sobrados, comércio com
    /// sobreloja, prédios médios e restaurantes. É o único lugar do código que cria fachadas. Tudo vira geometria em
    /// MeshBatch (uma malha por quarteirão); só os letreiros são TextMeshPro.
    /// </summary>
    public static class CityBuildings
    {
        public const float CurbHeight = 0.15f;
        private const float FloorHeight = 3f;

        private static readonly string[] ShopNames =
        {
            "Padaria Pão Dourado", "Farmácia Saúde Já", "Bar do Zé", "Mercadinho Amaro", "Açougue Boi Bom",
            "Salão da Neide", "Loja de Celular", "Lanchonete", "Papelaria Estrela", "Barbearia do Tião",
            "Pet Shop Au Au", "Sapataria Ideal", "Lotérica Sorte Grande", "Café & Pão", "Quitanda Verde Vida",
        };

        // ---------- Paredes com aberturas ----------

        private static void WallRect(BuildingContext c, Material mat, Frame fr, float x0, float x1, float y0, float y1, float d, Vector2 uv)
        {
            if (x1 - x0 > 0.001f && y1 - y0 > 0.001f)
            {
                c.B.Rect(mat, fr.Pt(x0, y0, d), fr.R, Vector3.up, x1 - x0, y1 - y0, uv);
            }
        }

        /// <summary>
        /// Parede da fachada entre y0 e y1 com aberturas. As aberturas que se sobrepõem em altura formam uma "faixa";
        /// em cada faixa saem as peças de parede entre elas, acima e abaixo, e as quatro faces do recuo (reveal).
        /// </summary>
        public static void WallWithHoles(BuildingContext c, Material mat, Frame fr, float w, float y0, float y1, List<Hole> holes, Vector2 uv)
        {
            if (c.Lite)
            {
                // Detalhe reduzido: parede lisa; as esquadrias entram como placas coladas 2 cm à frente.
                WallRect(c, mat, fr, 0f, w, y0, y1, 0f, uv);

                return;
            }

            holes.Sort((a, b) =>
            {
                int byY = a.Y.CompareTo(b.Y);

                return byY != 0 ? byY : a.X.CompareTo(b.X);
            });

            float cursor = y0;
            int i = 0;

            while (i < holes.Count)
            {
                float rowBottom = holes[i].Y;
                float rowTop = holes[i].Y + holes[i].H;
                int j = i + 1;

                while (j < holes.Count && holes[j].Y < rowTop - 0.01f)
                {
                    rowTop = Mathf.Max(rowTop, holes[j].Y + holes[j].H);
                    j++;
                }

                WallRect(c, mat, fr, 0f, w, cursor, rowBottom, 0f, uv);
                var row = holes.GetRange(i, j - i);
                row.Sort((a, b) => a.X.CompareTo(b.X));
                float x = 0f;

                foreach (Hole h in row)
                {
                    WallRect(c, mat, fr, x, h.X, rowBottom, rowTop, 0f, uv);
                    WallRect(c, mat, fr, h.X, h.X + h.W, rowBottom, h.Y, 0f, uv);
                    WallRect(c, mat, fr, h.X, h.X + h.W, h.Y + h.H, rowTop, 0f, uv);
                    Reveal(c, mat, fr, h, uv);
                    x = h.X + h.W;
                }

                WallRect(c, mat, fr, x, w, rowBottom, rowTop, 0f, uv);
                cursor = rowTop;
                i = j;
            }

            WallRect(c, mat, fr, 0f, w, cursor, y1, 0f, uv);
        }

        private static void Reveal(BuildingContext c, Material mat, Frame fr, Hole h, Vector2 uv)
        {
            if (h.E <= 0.001f)
            {
                return;
            }

            MeshBatch b = c.B;
            Vector3 up = Vector3.up;
            b.Rect(mat, fr.Pt(h.X, h.Y, 0f), -fr.F, up, h.E, h.H, uv);
            b.Rect(mat, fr.Pt(h.X + h.W, h.Y, h.E), fr.F, up, h.E, h.H, uv);
            b.Rect(mat, fr.Pt(h.X, h.Y + h.H, h.E), fr.R, fr.F, h.W, h.E, uv);
            b.Rect(mat, fr.Pt(h.X, h.Y, 0f), fr.R, -fr.F, h.W, h.E, uv);
        }

        // ---------- Esquadrias ----------

        /// <summary>Recuo (reveal) das aberturas: 0 nos quarteirões de detalhe reduzido.</summary>
        private static float Recess(BuildingContext c, float value) => c.Lite ? -0.02f : value;

        /// <summary>Janela: aro de alumínio, vidro e travessas; grade de ferro na frente nos quarteirões de detalhe completo.</summary>
        private static void WindowFill(BuildingContext c, Frame fr, Hole h, bool bars, bool tall)
        {
            MeshBatch b = c.B;
            Vector3 up = Vector3.up;
            Material aluminum = CityStyle.Aluminum();
            const float rim = 0.05f;
            b.Rect(aluminum, fr.Pt(h.X, h.Y, h.E), fr.R, up, h.W, h.H);
            b.Rect(CityStyle.Glass(), fr.Pt(h.X + rim, h.Y + rim, h.E - 0.012f), fr.R, up, h.W - 2f * rim, h.H - 2f * rim);

            if (c.Lite)
            {
                return;
            }

            b.Rect(aluminum, fr.Pt(h.X + h.W * 0.5f - 0.02f, h.Y + rim, h.E - 0.02f), fr.R, up, 0.04f, h.H - 2f * rim);

            b.Rect(aluminum, fr.Pt(h.X + rim, h.Y + h.H * (tall ? 0.7f : 0.5f) - 0.02f, h.E - 0.02f), fr.R, up, h.W - 2f * rim, 0.04f);

            if (bars)
            {
                Material iron = CityStyle.IronBlack();
                int n = Mathf.Max(3, Mathf.RoundToInt(h.W / 0.25f));

                for (int k = 0; k < n; k++)
                {
                    float x = h.X + (k + 0.5f) * h.W / n - 0.012f;
                    b.Rect(iron, fr.Pt(x, h.Y, 0.04f), fr.R, up, 0.024f, h.H);
                }

                b.Rect(iron, fr.Pt(h.X, h.Y + h.H * 0.5f, 0.035f), fr.R, up, h.W, 0.03f);
            }
        }

        private static void Railing(BuildingContext c, Vector3 a, Vector3 b2, float height)
        {
            Material iron = CityStyle.IronBlack();
            Vector3 dir = b2 - a;
            float length = dir.magnitude;
            dir /= length;
            Vector3 up = Vector3.up;
            c.B.Rect(iron, a + up * (height - 0.05f), dir, up, length, 0.05f);
            c.B.Rect(iron, a + up * 0.12f, dir, up, length, 0.04f);

            if (c.Lite)
            {
                c.B.Rect(iron, a + up * 0.12f, dir, up, length, height - 0.2f);

                return;
            }

            int n = Mathf.Max(2, Mathf.RoundToInt(length / 0.25f));

            for (int k = 0; k <= n; k++)
            {
                Vector3 p = a + dir * (length * k / n);
                c.B.Rect(iron, p - dir * 0.012f + up * 0.12f, dir, up, 0.024f, height - 0.2f);
            }
        }

        private static void Balcony(BuildingContext c, Frame fr, float x, float y, float width, float depth, Material slab)
        {
            c.B.Box(slab, fr.Pt(x, y - 0.14f, -depth), fr.F, width, 0.14f, depth, true, false, true, true);
            Vector3 left = fr.Pt(x + 0.05f, y, -depth + 0.05f);
            Vector3 right = fr.Pt(x + width - 0.05f, y, -depth + 0.05f);
            Railing(c, left, right, 1.0f);
            Railing(c, fr.Pt(x + 0.05f, y, 0f), left, 1.0f);
            Railing(c, right, fr.Pt(x + width - 0.05f, y, 0f), 1.0f);
        }

        // ---------- Volume, telhado e topo ----------

        private static void Mass(BuildingContext c, Material wall, Frame fr, float w, float h, float d, Vector2 uv)
        {
            Vector3 up = Vector3.up;
            c.B.Rect(wall, fr.Pt(w, 0f, 0f), -fr.F, up, d, h, uv);
            c.B.Rect(wall, fr.Pt(0f, 0f, d), fr.F, up, d, h, uv);
            c.B.Rect(wall, fr.Pt(w, 0f, d), -fr.R, up, w, h, uv);
        }

        /// <summary>Janelas planas nas paredes laterais expostas (esquinas): sem elas o lote vira uma parede cega gigante.</summary>
        private static void SideWindows(BuildingContext c, Frame fr, float w, float d, float y0, int floors)
        {
            for (int side = 0; side < 2; side++)
            {
                bool right = side == 1;

                if (right ? !c.ExposeRight : !c.ExposeLeft)
                {
                    continue;
                }

                Frame sf = right ? new Frame(fr.Pt(w, 0f, 0f), fr.R) : new Frame(fr.Pt(0f, 0f, d), -fr.R);
                float usable = d - 3.0f;
                int n = Mathf.Max(1, Mathf.FloorToInt(usable / 3.6f));
                float cell = usable / n;

                for (int k = 0; k < floors; k++)
                {
                    for (int i = 0; i < n; i++)
                    {
                        WindowFill(c, sf, new Hole(1.5f + i * cell + (cell - 1.4f) * 0.5f, y0 + k * FloorHeight + 0.9f, 1.4f, 1.3f, -0.02f), false, false);
                    }
                }
            }
        }

        private static void WaterTank(BuildingContext c, Vector3 center, float radius)
        {
            Material concrete = CityStyle.Concrete();
            MeshBatch b = c.B;

            if (c.Lite)
            {
                b.Frustum(CityStyle.Water(), center, radius, radius * 0.8f, radius * 1.4f, 7, true);

                return;
            }

            b.AxisBox(concrete, center + Vector3.up * 0.25f, new Vector3(radius * 2.3f, 0.5f, radius * 2.3f));
            b.Frustum(CityStyle.Water(), center + Vector3.up * 0.5f, radius, radius, radius * 1.15f, 12, false);
            b.Frustum(CityStyle.Water(), center + Vector3.up * (0.5f + radius * 1.15f), radius, radius * 0.7f, radius * 0.25f, 12, true);
        }

        /// <summary>Laje impermeabilizada: topo plano, parapeito interno, caixa d'água, casinha e ferros de espera.</summary>
        private static void SlabRoof(BuildingContext c, Frame fr, Material wall, float w, float h, float d, float parapet, bool tank, bool room, bool rebar, Vector2 uv)
        {
            MeshBatch b = c.B;
            Vector3 up = Vector3.up;
            b.Rect(CityStyle.Concrete(), fr.Pt(0f, h, 0f), fr.R, -fr.F, w, d);

            if (parapet > 0.01f)
            {
                float t = 0.15f;
                b.Rect(wall, fr.Pt(w, h, t), -fr.R, up, w, parapet, uv);
                b.Rect(wall, fr.Pt(w - t, h, d), fr.F, up, d, parapet, uv);
                b.Rect(wall, fr.Pt(t, h, 0f), -fr.F, up, d, parapet, uv);
                b.Rect(wall, fr.Pt(0f, h, d - t), fr.R, up, w, parapet, uv);
                b.Rect(CityStyle.Concrete(), fr.Pt(0f, h + parapet, 0f), fr.R, -fr.F, w, t);
            }

            if (room && w > 6f && !c.Lite)
            {
                b.Box(wall, fr.Pt(0.5f, h, d - 3.2f), fr.F, 2.6f, 2.3f, 2.6f, true, true, true, true, uv);
            }

            if (tank)
            {
                WaterTank(c, fr.Pt(w - 1.5f, h, d - 2.2f), 0.85f);
            }

            if (rebar && !c.Lite)
            {
                Material iron = CityStyle.IronBlack();

                foreach (float x in new[] { 0.3f, w - 0.3f })
                {
                    for (int k = 0; k < 3; k++)
                    {
                        Vector3 p = fr.Pt(x, h, 0.4f + k * 0.12f);
                        b.Wire(iron, p, p + up * 0.7f, 0.03f);
                    }
                }
            }
        }

        /// <summary>Telhado de duas águas (cumeeira paralela à fachada) com empena nas laterais.</summary>
        private static void GableRoof(BuildingContext c, Frame fr, Material roof, Material wall, float w, float h, float d, float rise, Vector2 uv)
        {
            MeshBatch b = c.B;
            const float over = 0.25f;
            Vector3 outFront = (Vector3.up + fr.F).normalized;
            Vector3 outBack = (Vector3.up - fr.F).normalized;
            Vector3 e0 = fr.Pt(-over, h, -0.35f);
            Vector3 e1 = fr.Pt(w + over, h, -0.35f);
            Vector3 r0 = fr.Pt(-over, h + rise, d * 0.5f);
            Vector3 r1 = fr.Pt(w + over, h + rise, d * 0.5f);
            Vector3 f0 = fr.Pt(-over, h, d + 0.35f);
            Vector3 f1 = fr.Pt(w + over, h, d + 0.35f);
            b.Quad(roof, e0, e1, r1, r0, outFront);
            b.Quad(roof, f0, f1, r1, r0, outBack);
            b.Tri(wall, fr.Pt(0f, h, 0f), fr.Pt(0f, h, d), fr.Pt(0f, h + rise, d * 0.5f), -fr.R, uv);
            b.Tri(wall, fr.Pt(w, h, 0f), fr.Pt(w, h, d), fr.Pt(w, h + rise, d * 0.5f), fr.R, uv);
        }

        private static void AcUnit(BuildingContext c, Frame fr, float x, float y)
        {
            if (c.Lite)
            {
                return;
            }

            c.B.Box(CityStyle.AcUnit(), fr.Pt(x, y, -0.32f), fr.F, 0.85f, 0.5f, 0.32f, true, false, true, true);
            c.B.Rect(CityStyle.Dark(), fr.Pt(x + 0.1f, y + 0.1f, -0.325f), fr.R, Vector3.up, 0.4f, 0.3f);
        }

        // ---------- Letreiros ----------

        public static void Sign(BuildingContext c, Frame fr, float cx, float cy, float width, float height, string text, Color color, float d = -0.075f)
        {
            var go = new GameObject("Letreiro_" + text);
            go.transform.SetParent(c.Signs, false);
            go.transform.SetPositionAndRotation(fr.Pt(cx, cy, d), Quaternion.LookRotation(-fr.F, Vector3.up));
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.rectTransform.sizeDelta = new Vector2(width, height);
            tmp.text = text;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 0.5f;
            tmp.fontSizeMax = 6f;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.color = color;
            tmp.fontSize = 3f;
            c.SignCount++;
        }

        // ---------- Térreo comercial ----------

        /// <summary>Descrição de um ponto comercial do térreo.</summary>
        public sealed class Shop
        {
            public string Name;
            public int Board;      // variante da placa
            public int Kind;       // 0 = enrolar aberta, 1 = enrolar fechada, 2 = vitrine de vidro
            public bool Awning;
            public int AwningColor;
            public bool Tires;
            public Material AwningMaterial;
            public bool BigSign;
        }

        public static Shop RandomShop(System.Random r, bool avenue)
        {
            return new Shop
            {
                Name = ShopNames[r.Next(ShopNames.Length)],
                Board = r.Next(7),
                Kind = avenue ? (r.NextDouble() < 0.5f ? 2 : r.Next(2)) : r.Next(3),
                Awning = r.NextDouble() < 0.55f,
                AwningColor = new[] { 0, 1, 2, 3, 5 }[r.Next(5)],
            };
        }

        /// <summary>Térreo (altura h0): abertura da loja, porta do prédio, toldo, placa com o nome. Cobre x de 0 a w.</summary>
        public static void GroundFloor(BuildingContext c, Frame fr, Material wall, float w, float h0, Shop shop, Vector2 uv, bool apartmentDoor = true)
        {
            MeshBatch b = c.B;
            Vector3 up = Vector3.up;
            const float opening = 3.0f;
            float ox = 0.5f;
            float ow = w - 0.5f - (apartmentDoor ? 2.1f : 0.5f);
            var holes = new List<Hole> { new Hole(ox, 0f, ow, opening, Recess(c, 0.28f)) };

            if (apartmentDoor)
            {
                holes.Add(new Hole(w - 1.6f, 0f, 1.0f, 2.3f, Recess(c, 0.2f)));
            }

            WallWithHoles(c, wall, fr, w, 0f, h0, holes, uv);

            if (apartmentDoor)
            {
                b.Rect(CityStyle.Door(), fr.Pt(w - 1.6f, 0f, Recess(c, 0.2f) - 0.01f), fr.R, up, 1.0f, 2.3f);
            }

            float e = Recess(c, 0.28f);
            float doorE = Recess(c, 0.2f);

            switch (shop.Kind)
            {
                case 0:
                    // Porta de enrolar aberta: interior escuro, mercadoria, rolo no alto e guias laterais.
                    b.Rect(CityStyle.Interior(), fr.Pt(ox, 0f, c.Lite ? e : 2.6f), fr.R, up, ow, opening);
                    if (!c.Lite)
                    {
                        b.Rect(CityStyle.Interior(), fr.Pt(ox, 0f, e), fr.R, up, ow, 0.05f);
                        b.Rect(CityStyle.Interior(), fr.Pt(ox, opening, e), fr.R, -fr.F, ow, 2.6f - e);
                        b.Rect(CityStyle.Floor(), fr.Pt(ox, 0.01f, e), fr.R, -fr.F, ow, 2.6f - e);
                    }

                    for (int k = 0; k < (c.Lite ? 1 : 3); k++)
                    {
                        float gx = ox + 0.4f + c.Rnd.Next(0, Mathf.Max(1, Mathf.RoundToInt(ow - 1.4f)));
                        b.Box(CityStyle.Goods(c.Rnd.Next(5)), fr.Pt(gx, 0f, e + 0.5f + k * 0.7f), fr.F, 0.8f, 0.6f + 0.5f * (float)c.Rnd.NextDouble(), 0.5f);
                    }

                    b.Box(CityStyle.RollUp(c.Rnd.Next(4)), fr.Pt(ox, opening - 0.32f, 0.04f), fr.F, ow, 0.32f, 0.3f);
                    break;
                case 1:
                    b.Rect(CityStyle.RollUp(c.Rnd.Next(4)), fr.Pt(ox, 0f, e - 0.06f), fr.R, up, ow, opening);

                    for (int k = 1; k < (c.Lite ? 4 : 12); k++)
                    {
                        b.Rect(CityStyle.IronBlack(), fr.Pt(ox, k * opening / (c.Lite ? 4f : 12f), e - 0.065f), fr.R, up, ow, 0.012f);
                    }

                    b.Box(CityStyle.RollUp(0), fr.Pt(ox, opening - 0.1f, 0.02f), fr.F, ow, 0.12f, 0.2f);
                    break;
                default:
                    b.Rect(CityStyle.ShopLight(), fr.Pt(ox, 0f, c.Lite ? e : e + 0.6f), fr.R, up, ow, opening);
                    if (!c.Lite)
                    {
                        b.Rect(CityStyle.ShopLight(), fr.Pt(ox, opening, e), fr.R, -fr.F, ow, 0.6f);
                    }
                    int bays = Mathf.Max(2, Mathf.RoundToInt(ow / 1.5f));

                    for (int k = 0; k <= bays; k++)
                    {
                        float x = ox + k * ow / bays - 0.03f;
                        b.Rect(CityStyle.Aluminum(), fr.Pt(Mathf.Clamp(x, ox, ox + ow - 0.06f), 0f, e + 0.02f), fr.R, up, 0.06f, opening);
                    }

                    b.Rect(CityStyle.Aluminum(), fr.Pt(ox, 0.9f, e + 0.02f), fr.R, up, ow, 0.05f);
                    b.Rect(CityStyle.Aluminum(), fr.Pt(ox, 2.4f, e + 0.02f), fr.R, up, ow, 0.05f);
                    break;
            }

            if (shop.Tires && !c.Lite)
            {
                for (int s = 0; s < 3; s++)
                {
                    Vector3 basePoint = fr.Pt(ox + 0.8f + s * 1.3f, 0f, -0.9f);

                    for (int k = 0; k < 4 - s % 2; k++)
                    {
                        b.Frustum(CityStyle.Tire(), basePoint + up * (k * 0.22f), 0.33f, 0.33f, 0.2f, 10);
                    }
                }
            }

            // Toldo listrado inclinado e a placa com o nome.
            float signBottom = opening + 0.15f;

            if (shop.Awning && h0 > 3.5f)
            {
                Material m1 = shop.AwningMaterial != null ? shop.AwningMaterial : CityStyle.Awning(shop.AwningColor);
                Material m2 = CityStyle.Awning(4);
                float proj = 1.5f;
                float y = opening + 0.05f;
                float sw = c.Lite ? 1.0f : 0.5f;
                int stripes = Mathf.Max(2, Mathf.CeilToInt(ow / sw));
                float step = ow / stripes;

                for (int k = 0; k < stripes; k++)
                {
                    Material m = k % 2 == 0 ? m1 : m2;
                    float x = ox + k * step;
                    b.Quad(m, fr.Pt(x, y, 0f), fr.Pt(x + step, y, 0f), fr.Pt(x + step, y - 0.55f, -proj), fr.Pt(x, y - 0.55f, -proj), up + fr.F);
                    b.Rect(m, fr.Pt(x, y - 0.55f - 0.28f, -proj), fr.R, up, step, 0.28f);
                }

                b.Quad(CityStyle.Dark(), fr.Pt(ox, y, 0f), fr.Pt(ox + ow, y, 0f), fr.Pt(ox + ow, y - 0.55f, -proj), fr.Pt(ox, y - 0.55f, -proj), -up);
                signBottom = opening + 0.2f;
            }

            if (h0 - signBottom > 0.5f)
            {
                float height = Mathf.Min(0.78f, h0 - signBottom - 0.08f);
                float signX = ox;
                float signW = ow;
                float cy = h0 - 0.08f - height * 0.5f;
                b.Rect(CityStyle.Board(shop.Board), fr.Pt(signX, cy - height * 0.5f, -0.06f), fr.R, up, signW, height);
                b.Box(CityStyle.Dark(), fr.Pt(signX - 0.03f, cy - height * 0.5f - 0.03f, -0.065f), fr.F, signW + 0.06f, height + 0.06f, 0.02f, false, false, false, false);
                if (!c.Lite)
                {
                    Sign(c, fr, signX + signW * 0.5f, cy, signW - 0.2f, height - 0.1f, shop.Name, CityStyle.BoardText(shop.Board));
                }
            }
        }

        // ---------- Andares ----------

        private static void UpperFloors(BuildingContext c, Frame fr, Material wall, float w, float y0, int floors, Vector2 uv, bool balconies, bool detailed)
        {
            var holes = new List<Hole>();
            int n = Mathf.Max(2, Mathf.FloorToInt(w / 3.4f));
            float cell = w / n;
            var tall = new List<bool>();
            var barred = new List<bool>();
            var windows = new List<Hole>();
            Material slab = CityStyle.Concrete();

            for (int k = 0; k < floors; k++)
            {
                float y = y0 + k * FloorHeight;
                bool balconyFloor = balconies && c.Rnd.NextDouble() < 0.4f;
                bool bars = c.Rnd.NextDouble() < 0.3f;

                for (int i = 0; i < n; i++)
                {
                    bool door = balconyFloor && (i % 2 == 0);
                    float ww = Mathf.Min(1.7f, cell * 0.62f);
                    var hole = new Hole(i * cell + (cell - ww) * 0.5f, y + (door ? 0.05f : 0.9f), ww, door ? 2.15f : 1.3f, Recess(c, 0.14f));
                    holes.Add(hole);
                    windows.Add(hole);
                    tall.Add(door);
                    barred.Add(bars && !door);

                    if (door)
                    {
                        Balcony(c, fr, hole.X - 0.2f, y, ww + 0.4f, 1.1f, slab);
                    }
                    else if (c.Rnd.NextDouble() < 0.18f)
                    {
                        AcUnit(c, fr, hole.X + hole.W + 0.15f, y + 1.4f);
                    }
                }
            }

            WallWithHoles(c, wall, fr, w, y0, y0 + floors * FloorHeight, holes, uv);

            for (int i = 0; i < windows.Count; i++)
            {
                WindowFill(c, fr, windows[i], barred[i], tall[i]);
            }

            if (detailed)
            {
                // Faixa de laje entre andares.
                for (int k = 0; k <= floors; k++)
                {
                    c.B.Rect(slab, fr.Pt(0f, y0 + k * FloorHeight - 0.08f, -0.02f), fr.R, Vector3.up, w, 0.16f);
                }
            }
        }

        /// <summary>Fachada de prédio médio: parede lisa com janelas (vidro, caixilho e peitoril) e varandas em grade, em geometria.</summary>
        private static void TowerFloors(BuildingContext c, Frame fr, Material wall, float w, float y0, int floors, Vector2 uv)
        {
            MeshBatch b = c.B;
            Vector3 up = Vector3.up;
            float height = floors * FloorHeight;
            WallRect(c, wall, fr, 0f, w, y0, y0 + height, 0f, uv);
            int n = Mathf.Max(3, Mathf.FloorToInt(w / 3.3f));
            float cell = w / n;
            Material slab = CityStyle.Concrete();
            Material aluminum = CityStyle.Aluminum();
            Material glass = CityStyle.Glass();
            bool balconyColumns = c.Rnd.NextDouble() < 0.7;

            for (int k = 0; k < floors; k++)
            {
                float y = y0 + k * FloorHeight;
                b.Rect(slab, fr.Pt(0f, y - 0.1f, -0.04f), fr.R, up, w, 0.22f);

                for (int i = 0; i < n; i++)
                {
                    bool balcony = balconyColumns && i % 2 == 1;
                    float ww = Mathf.Min(1.9f, cell * 0.66f);
                    float x = i * cell + (cell - ww) * 0.5f;
                    float wy = y + (balcony ? 0.2f : 0.95f);
                    float wh = balcony ? 2.2f : 1.3f;
                    b.Rect(aluminum, fr.Pt(x - 0.05f, wy - 0.05f, -0.015f), fr.R, up, ww + 0.1f, wh + 0.1f);
                    b.Rect(glass, fr.Pt(x, wy, -0.03f), fr.R, up, ww, wh);
                    if (!c.Lite)
                    {
                        b.Rect(aluminum, fr.Pt(x + ww * 0.5f - 0.02f, wy, -0.035f), fr.R, up, 0.04f, wh);
                    }

                    if (balcony)
                    {
                        b.Box(slab, fr.Pt(x - 0.25f, y - 0.1f, -1.2f), fr.F, ww + 0.5f, 0.14f, 1.2f, true, false, true, true);
                        b.Rect(CityStyle.IronBlack(), fr.Pt(x - 0.25f, y + 0.04f, -1.19f), fr.R, up, ww + 0.5f, 0.95f);
                        b.Rect(aluminum, fr.Pt(x - 0.25f, y + 0.99f, -1.2f), fr.R, up, ww + 0.5f, 0.05f);
                    }
                    else if (c.Rnd.NextDouble() < 0.07f)
                    {
                        AcUnit(c, fr, x + ww * 0.1f, wy - 0.55f);
                    }
                }
            }
        }

        // ---------- Prédios ----------

        public static float Hash(System.Random r) => (float)r.NextDouble();

        /// <summary>
        /// Sobrado (2 ou 3 andares): muro de frente com portão de garagem e portão social, casa recuada 3 m, janelas com
        /// grade, laje ou telhado (cerâmica ou fibrocimento), às vezes andar superior em tijolo aparente e caixa d'água.
        /// </summary>
        public static void Sobrado(BuildingContext c, Frame fr, float w, float depth)
        {
            System.Random r = c.Rnd;
            MeshBatch b = c.B;
            Vector3 up = Vector3.up;
            Vector2 uv = new Vector2((float)r.NextDouble() * 9f, (float)r.NextDouble() * 9f);
            int floors = r.NextDouble() < 0.5 ? 2 : 3;
            float setback = 3f;
            float d = Mathf.Min(depth - setback, 9f);
            float h = floors * FloorHeight;
            Material wall = r.NextDouble() < 0.7 ? CityStyle.Plaster(r) : CityStyle.OldPlaster(r);
            Material wallFront = wall;
            bool brickUpper = r.NextDouble() < 0.22;
            Material muroMat = r.NextDouble() < 0.5 ? wall : CityStyle.Plaster(r);
            int roofKind = r.NextDouble() < 0.42 ? 0 : (r.NextDouble() < 0.55 ? 1 : 2);
            float parapet = roofKind == 0 ? 0.9f : 0f;

            // Quintal da frente e muro.
            b.Rect(CityStyle.Floor(), fr.Pt(0f, 0.012f, 0f), fr.R, -fr.F, w, setback);
            float muro = 2.3f;
            bool garageLeft = r.NextDouble() < 0.5;
            float gateW = 2.9f;
            float pedW = 0.95f;
            float gx = garageLeft ? 0.7f : w - 0.7f - gateW;
            float px = garageLeft ? gx + gateW + 0.55f : gx - 0.55f - pedW;
            var muroHoles = new List<Hole> { new Hole(gx, 0f, gateW, 2.2f, 0f), new Hole(px, 0f, pedW, 2.05f, 0f) };
            WallWithHoles(c, muroMat, fr, w, 0f, muro, muroHoles, uv);
            int gateVariant = r.Next(7);
            Material gate = CityStyle.Gate(gateVariant);
            float gd = c.Lite ? -0.02f : 0.03f;
            b.Rect(gate, fr.Pt(gx, 0f, gd), fr.R, up, gateW, 2.2f);
            b.Rect(gate, fr.Pt(px, 0f, gd), fr.R, up, pedW, 2.05f);
            if (!c.Lite)
            {
                b.Rect(CityStyle.IronBlack(), fr.Pt(gx, 1.1f, 0.025f), fr.R, up, gateW, 0.04f);
            }
            b.Rect(CityStyle.Concrete(), fr.Pt(0f, muro, 0f), fr.R, -fr.F, w, 0.2f);
            b.Rect(muroMat, fr.Pt(w, 0f, 0f), -fr.F, up, setback, muro, uv);
            b.Rect(muroMat, fr.Pt(0f, 0f, setback), fr.F, up, setback, muro, uv);

            // Casa.
            Frame hf = fr.Back(setback);
            var ground = new List<Hole>();
            float garageX = gx + 0.1f;
            ground.Add(new Hole(garageX, 0f, gateW - 0.2f, 2.4f, Recess(c, 0.1f)));
            float doorX = px + 0.0f;
            ground.Add(new Hole(doorX, 0f, 1.0f, 2.2f, Recess(c, 0.18f)));
            float winX = garageLeft ? w * 0.62f : w * 0.12f;
            bool groundWindow = Mathf.Abs(winX - garageX) > 3.2f && Mathf.Abs(winX - doorX) > 1.8f && winX + 1.4f < w - 0.3f;

            if (groundWindow)
            {
                ground.Add(new Hole(winX, 1.0f, 1.4f, 1.1f, Recess(c, 0.14f)));
            }

            WallWithHoles(c, wallFront, hf, w, 0f, FloorHeight, ground, uv);
            b.Rect(CityStyle.Interior(), hf.Pt(garageX, 0f, c.Lite ? -0.02f : 0.5f), hf.R, up, gateW - 0.2f, 2.4f);
            b.Rect(CityStyle.Door(), hf.Pt(doorX, 0f, Recess(c, 0.18f) - 0.01f), hf.R, up, 1.0f, 2.2f);

            if (groundWindow)
            {
                WindowFill(c, hf, ground[ground.Count - 1], true, false);
            }

            if (floors > 1)
            {
                Material upper = brickUpper ? CityStyle.Brick() : wall;
                int n = w >= 9f ? 3 : (w >= 7.5f ? 2 : 1);
                float cell = w / n;
                var holes = new List<Hole>();
                var tallFlags = new List<bool>();
                var barFlags = new List<bool>();
                bool balconyFloor = r.NextDouble() < 0.45;

                for (int k = 1; k < floors; k++)
                {
                    for (int i = 0; i < n; i++)
                    {
                        bool door = balconyFloor && k == 1 && i == 0;
                        float ww = Mathf.Min(1.6f, cell * 0.6f);
                        var hole = new Hole(i * cell + (cell - ww) * 0.5f, k * FloorHeight + (door ? 0.05f : 0.9f), ww, door ? 2.15f : 1.3f, Recess(c, 0.14f));
                        holes.Add(hole);
                        tallFlags.Add(door);
                        barFlags.Add(!door && r.NextDouble() < 0.35);

                        if (door)
                        {
                            Balcony(c, hf, hole.X - 0.25f, k * FloorHeight, ww + 0.5f, 1.1f, CityStyle.Concrete());
                        }
                        else if (r.NextDouble() < 0.2)
                        {
                            AcUnit(c, hf, hole.X + hole.W + 0.12f, k * FloorHeight + 1.3f);
                        }
                    }
                }

                WallWithHoles(c, upper, hf, w, FloorHeight, h + parapet, holes, brickUpper ? Vector2.zero : uv);

                for (int i = 0; i < holes.Count; i++)
                {
                    WindowFill(c, hf, holes[i], barFlags[i], tallFlags[i]);
                }

                if (brickUpper)
                {
                    b.Rect(CityStyle.Concrete(), hf.Pt(0f, FloorHeight - 0.14f, -0.02f), hf.R, up, w, 0.3f);
                    b.Box(CityStyle.Concrete(), hf.Pt(0f, FloorHeight, -0.03f), hf.F, 0.25f, h - FloorHeight + 0.3f, 0.25f);
                    b.Box(CityStyle.Concrete(), hf.Pt(w - 0.25f, FloorHeight, -0.03f), hf.F, 0.25f, h - FloorHeight + 0.3f, 0.25f);
                }
            }
            else
            {
                WallRect(c, wallFront, hf, 0f, w, FloorHeight, h + parapet, 0f, uv);
            }

            Material sideWall = brickUpper ? wall : wall;
            Mass(c, sideWall, hf, w, h + parapet, d, uv);
            SideWindows(c, hf, w, d, FloorHeight, floors - 1);

            switch (roofKind)
            {
                case 0:
                    SlabRoof(c, hf, wall, w, h, d, parapet, r.NextDouble() < 0.7, r.NextDouble() < 0.4, brickUpper, uv);
                    break;
                case 1:
                    GableRoof(c, hf, CityStyle.ClayRoof(), wall, w, h, d, 1.7f, uv);

                    if (r.NextDouble() < 0.6)
                    {
                        WaterTank(c, hf.Pt(w * 0.5f, h + 0.9f, d * 0.5f), 0.7f);
                    }

                    break;
                default:
                    GableRoof(c, hf, CityStyle.Fibrocement(), wall, w, h, d, 1.0f, uv);
                    break;
            }
        }

        /// <summary>
        /// Comércio de 2 a 4 andares: térreo com loja (porta de enrolar, vitrine ou toldo e letreiro) e entrada do prédio,
        /// andares de apartamento acima, laje com caixa d'água.
        /// </summary>
        public static void Commerce(BuildingContext c, Frame fr, float w, float depth, Shop shop, int floors)
        {
            System.Random r = c.Rnd;
            Vector2 uv = new Vector2((float)r.NextDouble() * 9f, (float)r.NextDouble() * 9f);
            Material wall = r.NextDouble() < 0.6 ? CityStyle.Plaster(r) : CityStyle.OldPlaster(r);
            float h0 = 4.0f;
            float h = h0 + (floors - 1) * FloorHeight;
            float parapet = 0.9f;
            float d = Mathf.Min(depth, 14f);
            GroundFloor(c, fr, wall, w, h0, shop, uv);
            UpperFloors(c, fr, wall, w, h0, floors - 1, uv, true, true);
            WallRect(c, wall, fr, 0f, w, h, h + parapet, 0f, uv);
            Mass(c, wall, fr, w, h + parapet, d, uv);
            SideWindows(c, fr, w, d, h0, floors - 1);
            SlabRoof(c, fr, wall, w, h, d, parapet, r.NextDouble() < 0.7, r.NextDouble() < 0.5, false, uv);
        }

        /// <summary>Prédio de 5 a 12 andares: térreo comercial, janelas e varandas em geometria, casa de máquinas e caixa d'água.</summary>
        public static void Tower(BuildingContext c, Frame fr, float w, float depth, Shop shop, int floors)
        {
            System.Random r = c.Rnd;
            Vector2 uv = new Vector2((float)r.NextDouble() * 9f, (float)r.NextDouble() * 9f);
            Material wall = r.NextDouble() < 0.4 ? CityStyle.GreyPlaster() : CityStyle.Plaster(r);
            float h0 = 4.5f;
            float h = h0 + floors * FloorHeight;
            float parapet = 1.1f;
            float d = Mathf.Min(depth, 14f);
            GroundFloor(c, fr, wall, w, h0, shop, uv, true);
            TowerFloors(c, fr, wall, w, h0, floors, uv);
            WallRect(c, wall, fr, 0f, w, h, h + parapet, 0f, uv);
            Mass(c, wall, fr, w, h + parapet, d, uv);
            SideWindows(c, fr, w, d, h0, floors);
            SlabRoof(c, fr, wall, w, h, d, parapet, true, true, false, uv);
            WaterTank(c, fr.Pt(w * 0.3f, h, d * 0.55f), 1.2f);
        }

        /// <summary>
        /// Restaurante: fachada larga e recuada (terraço de 3 m), toldos na cor do restaurante, porta aberta, placa grande com o nome
        /// e mesas com cadeiras na calçada. Dois andares de salão/escritório em cima.
        /// </summary>
        public static void Restaurant(BuildingContext c, Frame fr, float w, float depth, string name, int index, Color accent)
        {
            System.Random r = c.Rnd;
            Vector2 uv = new Vector2((float)r.NextDouble() * 9f, (float)r.NextDouble() * 9f);
            Material wall = CityStyle.Plaster(r);
            var shop = new Shop
            {
                Name = name,
                Board = new[] { 3, 0, 1, 6 }[index % 4],
                Kind = 2,
                Awning = true,
                AwningMaterial = PbrMaterials.Flat("ToldoRestaurante" + index, accent, 0.15f),
                BigSign = true,
            };

            float h0 = 4.4f;
            int floors = 3;
            float h = h0 + (floors - 1) * FloorHeight;
            float d = Mathf.Min(depth, 14f);
            GroundFloor(c, fr, wall, w, h0, shop, uv, true);
            UpperFloors(c, fr, wall, w, h0, floors - 1, uv, true, true);
            WallRect(c, wall, fr, 0f, w, h, h + 0.9f, 0f, uv);
            Mass(c, wall, fr, w, h + 0.9f, d, uv);
            SideWindows(c, fr, w, d, h0, floors - 1);
            SlabRoof(c, fr, wall, w, h, d, 0.9f, true, true, false, uv);

            // Terraço: piso, mesas, cadeiras e guarda-sóis na calçada (o prédio já está recuado 3 m).
            MeshBatch b = c.B;
            Material white = CityStyle.Plastic("Branco", new Color(0.92f, 0.92f, 0.9f));
            Material chair = CityStyle.Plastic("CadeiraVermelha", new Color(0.8f, 0.12f, 0.1f));
            Material umbrella = PbrMaterials.Flat("GuardaSol" + index, accent, 0.2f);

            for (int k = 0; k < 4; k++)
            {
                float x = (k + 0.5f) * w / 4f;
                Vector3 table = fr.Pt(x, 0f, -1.7f);
                b.Frustum(white, table, 0.4f, 0.4f, 0.74f, 10);
                b.Frustum(CityStyle.Aluminum(), table, 0.05f, 0.05f, 0.7f, 5, false);
                b.Frustum(umbrella, table + Vector3.up * 2.3f, 1.5f, 0.1f, 0.5f, 8);
                b.Frustum(CityStyle.Aluminum(), table, 0.03f, 0.03f, 2.3f, 5, false);

                for (int s = 0; s < 4; s++)
                {
                    float a = s * Mathf.PI * 0.5f + 0.4f;
                    Vector3 p = table + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.75f;
                    b.AxisBox(chair, p + Vector3.up * 0.22f, new Vector3(0.38f, 0.44f, 0.38f));
                    b.AxisBox(chair, p + Vector3.up * 0.6f, new Vector3(0.36f, 0.3f, 0.06f));
                }
            }
        }

        /// <summary>Pavilhão simples e baixo para os fundos do quarteirão: nada de vazio visível por cima.</summary>
        public static void BackBuilding(BuildingContext c, Vector3 center, Vector3 size, Material wall)
        {
            c.B.AxisBox(wall, center + Vector3.up * size.y * 0.5f, size);
        }
    }
}
