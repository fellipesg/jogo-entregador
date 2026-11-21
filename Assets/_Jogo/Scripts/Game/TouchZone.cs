using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Entregador.Game
{
    public enum TouchZoneMode
    {
        /// <summary>Arrastar na horizontal a partir de onde o dedo pousou: esquerda e direita.</summary>
        Direcao,

        /// <summary>Arrastar na vertical a partir de onde o dedo pousou: para cima acelera, para baixo freia.</summary>
        Pedal,
    }

    /// <summary>
    /// Zona de toque invisível que ocupa metade da tela. O ponto onde o dedo pousa vira o centro do gesto (um anel discreto aparece ali);
    /// o quanto o dedo se afasta dele, proporcionalmente, vira direção, acelerador ou freio. Soltar o dedo zera tudo (a moto rola).
    /// Funciona também com o mouse no editor. Fica atrás dos botões da interface, que continuam recebendo os cliques.
    /// O pivô da zona precisa ser o centro (0,5; 0,5).
    /// </summary>
    public sealed class TouchZone : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        private const float DeadZone = 0.08f;

        private static Sprite _ringSprite;
        private static Sprite _dotSprite;

        private BikeInput _input;
        private TouchZoneMode _mode;
        private RectTransform _rect;
        private RectTransform _ring;
        private RectTransform _dot;
        private Vector2 _origin;
        private int _pointer = int.MinValue;

        /// <summary>Valor atual do gesto: -1 a 1 na direção; -1 (freio total) a 1 (acelerador total) no pedal.</summary>
        public float Value { get; private set; }

        public bool Active => _pointer != int.MinValue;

        /// <summary>Raio do gesto em pixels de tela: o arrasto até aqui vale 100%.</summary>
        public static float RadiusPixels => Mathf.Max(70f, Mathf.Min(Screen.width, Screen.height) * 0.16f);

        public void Configure(BikeInput input, TouchZoneMode mode)
        {
            _input = input;
            _mode = mode;
            _rect = (RectTransform)transform;
            BuildVisuals();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (Active)
            {
                return;
            }

            _pointer = eventData.pointerId;
            _origin = eventData.position;
            Place(_ring, eventData, _origin);
            Place(_dot, eventData, _origin);
            _ring.gameObject.SetActive(true);
            _dot.gameObject.SetActive(true);
            Apply(0f);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.pointerId != _pointer)
            {
                return;
            }

            Vector2 delta = eventData.position - _origin;
            float raw = _mode == TouchZoneMode.Direcao ? delta.x : delta.y;
            float value = Mathf.Clamp(raw / RadiusPixels, -1f, 1f);
            Apply(Mathf.Abs(value) < DeadZone ? 0f : value);

            // O ponto acompanha o dedo, preso ao raio do anel e ao eixo do gesto.
            Vector2 clamped = Vector2.ClampMagnitude(delta, RadiusPixels);
            Vector2 axis = _mode == TouchZoneMode.Direcao ? new Vector2(clamped.x, 0f) : new Vector2(0f, clamped.y);
            Place(_dot, eventData, _origin + axis);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId == _pointer)
            {
                Release();
            }
        }

        private void OnDisable()
        {
            Release();
        }

        private void Release()
        {
            _pointer = int.MinValue;
            Apply(0f);

            if (_ring != null)
            {
                _ring.gameObject.SetActive(false);
                _dot.gameObject.SetActive(false);
            }
        }

        private void Apply(float value)
        {
            Value = value;

            if (_input == null)
            {
                return;
            }

            if (_mode == TouchZoneMode.Direcao)
            {
                _input.TouchSteer = value;
            }
            else
            {
                _input.TouchGas = Mathf.Max(0f, value);
                _input.TouchBrake = Mathf.Max(0f, -value);
            }
        }

        // ---------- Visual do anel ----------

        private void BuildVisuals()
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            float scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
            float size = RadiusPixels * 2f / scale;
            _ring = NewImage("Anel", GetRing(), new Color(1f, 1f, 1f, 0.22f), size);
            _dot = NewImage("Ponto", GetDot(), new Color(1f, 1f, 1f, 0.35f), size * 0.28f);
            _ring.gameObject.SetActive(false);
            _dot.gameObject.SetActive(false);
        }

        private RectTransform NewImage(string name, Sprite sprite, Color color, float size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(size, size);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;

            return rect;
        }

        private void Place(RectTransform target, PointerEventData eventData, Vector2 screen)
        {
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_rect, screen, eventData.pressEventCamera, out Vector2 local))
            {
                target.anchoredPosition = local;
            }
        }

        private static Sprite GetRing() => _ringSprite != null ? _ringSprite : (_ringSprite = MakeSprite(128, 0.88f, 0.98f));

        private static Sprite GetDot() => _dotSprite != null ? _dotSprite : (_dotSprite = MakeSprite(64, 0f, 0.96f));

        /// <summary>Círculo (ou anel) com borda suave, gerado por código: nenhum asset de imagem.</summary>
        private static Sprite MakeSprite(int size, float inner, float outer)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float half = size * 0.5f;
            float edge = 1.5f / half;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(half, half)) / half;
                    float alpha = Mathf.Clamp01((outer - d) / edge);

                    if (inner > 0f)
                    {
                        alpha *= Mathf.Clamp01((d - inner) / edge);
                    }

                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply();

            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
