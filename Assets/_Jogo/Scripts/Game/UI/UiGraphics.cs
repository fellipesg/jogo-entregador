using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Entregador.Game
{
    /// <summary>Arco (fatia de anel) desenhado em malha, com borda suave de 1 px. Ângulos em graus, anti-horário a partir de +X.</summary>
    public sealed class UiArc : MaskableGraphic
    {
        public float StartDeg = 225f;
        public float EndDeg = -45f;
        public float Inner = 60f;
        public float Outer = 70f;
        public int Segments = 64;


        public void Set(float startDeg, float endDeg)
        {
            if (Mathf.Approximately(startDeg, StartDeg) && Mathf.Approximately(endDeg, EndDeg))
            {
                return;
            }

            StartDeg = startDeg;
            EndDeg = endDeg;
            SetVerticesDirty();
        }

        public void SetRadii(float inner, float outer)
        {
            Inner = inner;
            Outer = outer;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Vector2 center = rectTransform.rect.center;
            float span = EndDeg - StartDeg;

            if (Mathf.Abs(span) < 0.05f || Outer <= Inner)
            {
                return;
            }

            int segs = Mathf.Max(2, Mathf.CeilToInt(Segments * Mathf.Abs(span) / 270f));
            const float f = 0.8f;
            float[] radii = { Outer + f, Outer - f, Inner + f, Inner - f };
            float[] alphas = { 0f, 1f, 1f, 0f };

            if (Outer - Inner < 2f * f + 0.5f)
            {
                radii = new[] { Outer + f, (Outer + Inner) * 0.5f, (Outer + Inner) * 0.5f, Inner - f };
            }

            for (int i = 0; i <= segs; i++)
            {
                float a = (StartDeg + span * i / segs) * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));

                for (int k = 0; k < 4; k++)
                {
                    Color32 col = color;
                    col.a = (byte)(col.a * alphas[k]);
                    vh.AddVert(center + dir * radii[k], col, Vector2.zero);
                }
            }

            for (int i = 0; i < segs; i++)
            {
                int b = i * 4;

                for (int k = 0; k < 3; k++)
                {
                    vh.AddTriangle(b + k, b + k + 4, b + k + 5);
                    vh.AddTriangle(b + k, b + k + 5, b + k + 1);
                }
            }
        }
    }

    /// <summary>Marcas radiais (ticks) do mostrador: cada uma é um retângulo girado.</summary>
    public sealed class UiTicks : MaskableGraphic
    {
        public struct Tick
        {
            public float Deg;
            public float R0;
            public float R1;
            public float Width;
            public Color Color;
        }

        private readonly List<Tick> _ticks = new List<Tick>();


        public void Clear()
        {
            _ticks.Clear();
            SetVerticesDirty();
        }

        public void Add(float deg, float r0, float r1, float width, Color tickColor)
        {
            _ticks.Add(new Tick { Deg = deg, R0 = r0, R1 = r1, Width = width, Color = tickColor });
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Vector2 center = rectTransform.rect.center;

            foreach (Tick t in _ticks)
            {
                float a = t.Deg * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                var side = new Vector2(-dir.y, dir.x) * (t.Width * 0.5f);
                Color32 c = t.Color * color;
                int i = vh.currentVertCount;
                vh.AddVert(center + dir * t.R0 - side, c, Vector2.zero);
                vh.AddVert(center + dir * t.R0 + side, c, Vector2.zero);
                vh.AddVert(center + dir * t.R1 + side, c, Vector2.zero);
                vh.AddVert(center + dir * t.R1 - side, c, Vector2.zero);
                vh.AddTriangle(i, i + 1, i + 2);
                vh.AddTriangle(i, i + 2, i + 3);
            }
        }
    }

    /// <summary>
    /// Polígonos preenchidos (ícones, ponteiros, estrelas, setas). Os pontos ficam em coordenadas 0..1 dentro do retângulo
    /// (y para cima) e são triangulados por "ear clipping", então valem polígonos côncavos simples.
    /// </summary>
    public sealed class UiShape : MaskableGraphic
    {
        private readonly List<Vector2[]> _polygons = new List<Vector2[]>();


        public void SetPolygons(IEnumerable<Vector2[]> polygons)
        {
            _polygons.Clear();
            _polygons.AddRange(polygons);
            SetVerticesDirty();
        }

        public static Vector2[] Rect(float x0, float y0, float x1, float y1)
        {
            return new[] { new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1) };
        }

        public static Vector2[] Disc(float cx, float cy, float rx, float ry, int n = 20)
        {
            var pts = new Vector2[n];

            for (int i = 0; i < n; i++)
            {
                float a = Mathf.PI * 2f * i / n;
                pts[i] = new Vector2(cx + Mathf.Cos(a) * rx, cy + Mathf.Sin(a) * ry);
            }

            return pts;
        }

        public static Vector2[] Star(int points = 5)
        {
            var pts = new Vector2[points * 2];

            for (int i = 0; i < pts.Length; i++)
            {
                float a = Mathf.PI / 2f + Mathf.PI * i / points;
                float r = i % 2 == 0 ? 0.5f : 0.21f;
                pts[i] = new Vector2(0.5f + Mathf.Cos(a) * r, 0.5f + 0.03f + Mathf.Sin(a) * r);
            }

            return pts;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();

            foreach (Vector2[] poly in _polygons)
            {
                Triangulate(vh, poly, r);
            }
        }

        private void Triangulate(VertexHelper vh, Vector2[] poly, Rect r)
        {
            int n = poly.Length;

            if (n < 3)
            {
                return;
            }

            int baseIndex = vh.currentVertCount;
            Color32 c = color;

            for (int i = 0; i < n; i++)
            {
                vh.AddVert(new Vector2(r.xMin + poly[i].x * r.width, r.yMin + poly[i].y * r.height), c, Vector2.zero);
            }

            var idx = new List<int>(n);
            float area = 0f;

            for (int i = 0; i < n; i++)
            {
                Vector2 a = poly[i];
                Vector2 b = poly[(i + 1) % n];
                area += a.x * b.y - b.x * a.y;
                idx.Add(i);
            }

            if (area < 0f)
            {
                idx.Reverse();
            }

            int guard = 0;

            while (idx.Count > 3 && guard++ < 500)
            {
                bool clipped = false;

                for (int i = 0; i < idx.Count; i++)
                {
                    int ia = idx[(i + idx.Count - 1) % idx.Count];
                    int ib = idx[i];
                    int ic = idx[(i + 1) % idx.Count];
                    Vector2 a = poly[ia];
                    Vector2 b = poly[ib];
                    Vector2 cc = poly[ic];

                    if ((b.x - a.x) * (cc.y - a.y) - (b.y - a.y) * (cc.x - a.x) <= 1e-6f)
                    {
                        continue;
                    }

                    bool inside = false;

                    for (int k = 0; k < idx.Count && !inside; k++)
                    {
                        int ik = idx[k];

                        if (ik == ia || ik == ib || ik == ic)
                        {
                            continue;
                        }

                        inside = PointInTriangle(poly[ik], a, b, cc);
                    }

                    if (inside)
                    {
                        continue;
                    }

                    vh.AddTriangle(baseIndex + ia, baseIndex + ib, baseIndex + ic);
                    idx.RemoveAt(i);
                    clipped = true;

                    break;
                }

                if (!clipped)
                {
                    break;
                }
            }

            if (idx.Count == 3)
            {
                vh.AddTriangle(baseIndex + idx[0], baseIndex + idx[1], baseIndex + idx[2]);
            }
        }

        private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool neg = d1 < 0f || d2 < 0f || d3 < 0f;
            bool pos = d1 > 0f || d2 > 0f || d3 > 0f;

            return !(neg && pos);
        }
    }

    /// <summary>Botão com animação de passar o mouse e pressionar (escala e cor), em tempo não escalado. Usa o Button do próprio objeto.</summary>
    public sealed class UiButtonFx : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler,
        UnityEngine.EventSystems.IPointerDownHandler, UnityEngine.EventSystems.IPointerUpHandler
    {
        public Image Target;
        public Color Normal = Color.white;
        public Color Hover = Color.white;
        public Color Pressed = Color.gray;
        public Color Disabled = Color.gray;

        private Button _button;
        private bool _over;
        private bool _down;
        private float _scale = 1f;
        private Color _color;
        private bool _init;

        private void Awake()
        {
            _button = GetComponent<Button>();
        }

        private void OnEnable()
        {
            _over = _down = false;
            _scale = 1f;
            transform.localScale = Vector3.one;
            _init = false;
        }

        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e) => _over = true;

        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e)
        {
            _over = false;
            _down = false;
        }

        public void OnPointerDown(UnityEngine.EventSystems.PointerEventData e) => _down = true;

        public void OnPointerUp(UnityEngine.EventSystems.PointerEventData e) => _down = false;

        private void Update()
        {
            if (Target == null)
            {
                return;
            }

            bool enabled = _button == null || _button.interactable;
            float goalScale = !enabled ? 1f : _down ? 0.96f : _over ? 1.03f : 1f;
            Color goalColor = !enabled ? Disabled : _down ? Pressed : _over ? Hover : Normal;
            float k = 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime);
            _scale = Mathf.Lerp(_scale, goalScale, k);

            if (!_init)
            {
                _color = goalColor;
                _init = true;
            }

            _color = Color.Lerp(_color, goalColor, k);
            transform.localScale = new Vector3(_scale, _scale, 1f);
            Target.color = _color;
        }
    }
}
