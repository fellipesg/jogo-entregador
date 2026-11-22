using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Entregador.Game
{
    public enum UiBotaoEstilo
    {
        /// <summary>Preenchido na cor do app.</summary>
        Primario,

        /// <summary>Contorno sobre fundo translúcido.</summary>
        Secundario,

        /// <summary>Preenchido verde (confirmar).</summary>
        Sucesso,
    }

    /// <summary>Fábrica de elementos de interface (painéis de vidro, textos, botões em pílula, chips, barras) em código.</summary>
    public static class UiKit
    {
        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            return go.GetComponent<RectTransform>();
        }

        public static void Stretch(RectTransform rect, float pad = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(pad, pad);
            rect.offsetMax = new Vector2(-pad, -pad);
        }

        /// <summary>Âncora e pivô iguais (posição dentro do pai), tamanho fixo.</summary>
        public static RectTransform Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            return rect;
        }

        public static Image Img(Transform parent, string name, Sprite sprite, Color color, bool sliced)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            image.color = color;
            image.raycastTarget = false;

            return image;
        }

        public static Image Circle(Transform parent, string name, Color color)
        {
            return Img(parent, name, UiSprites.Circle(), color, false);
        }

        public static T Gfx<T>(Transform parent, string name) where T : Graphic
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(T));
            go.transform.SetParent(parent, false);
            var g = go.GetComponent<T>();
            g.raycastTarget = false;

            return g;
        }

        /// <summary>
        /// Painel de vidro: contêiner com sombra suave, fundo arredondado e contorno de 1 px. O conteúdo é filho do contêiner
        /// (os três visuais são os primeiros filhos). Devolve o contêiner; o Image do fundo fica em "Fundo".
        /// </summary>
        public static RectTransform Panel(Transform parent, string name, int radius, Color fill, bool shadow = true, bool border = true)
        {
            RectTransform root = Rect(name, parent);

            if (shadow)
            {
                Image s = Img(root, "Sombra", UiSprites.Shadow(radius), new Color(0f, 0f, 0f, 0.42f), true);
                float pad = UiSprites.ShadowPad;
                s.rectTransform.anchorMin = Vector2.zero;
                s.rectTransform.anchorMax = Vector2.one;
                s.rectTransform.offsetMin = new Vector2(-pad, -pad - 7f);
                s.rectTransform.offsetMax = new Vector2(pad, pad - 7f);
            }

            Image back = Img(root, "Fundo", UiSprites.Fill(radius), fill, true);
            Stretch(back.rectTransform);

            if (border)
            {
                Image o = Img(root, "Borda", UiSprites.Outline(radius), UiTema.Borda, true);
                Stretch(o.rectTransform);
            }

            foreach (Transform child in root)
            {
                child.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            }

            return root;
        }

        public static TextMeshProUGUI Text(Transform parent, string name, string text, float size, Color color,
            FontStyles style = FontStyles.Normal, TextAlignmentOptions align = TextAlignmentOptions.Left, float spacing = 0f)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.fontStyle = style;
            t.alignment = align;
            t.characterSpacing = spacing;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.richText = true;
            t.raycastTarget = false;

            return t;
        }

        /// <summary>Rótulo pequeno em caixa alta com espaçamento entre letras (11-13 px de referência).</summary>
        public static TextMeshProUGUI Caps(Transform parent, string name, string text, Color color, TextAlignmentOptions align = TextAlignmentOptions.Left, float size = UiTema.TamMicro)
        {
            return Text(parent, name, text, size, color, FontStyles.Bold | FontStyles.UpperCase, align, 6f);
        }

        /// <summary>Botão em pílula com texto. O rótulo é o TextMeshProUGUI filho "Rotulo".</summary>
        public static Button Pill(Transform parent, string name, string label, UiBotaoEstilo style, float height = 54f, float fontSize = 20f)
        {
            RectTransform root = Rect(name, parent);
            root.sizeDelta = new Vector2(200f, height);
            bool primary = style != UiBotaoEstilo.Secundario;
            Color accent = style == UiBotaoEstilo.Sucesso ? UiTema.Sucesso : UiTema.Destaque;

            Image fill = Img(root, "Fundo", UiSprites.Fill(26), primary ? accent : UiTema.Superficie, true);
            Stretch(fill.rectTransform);
            fill.raycastTarget = true;

            if (!primary)
            {
                Image outline = Img(root, "Contorno", UiSprites.Outline(26), new Color(1f, 1f, 1f, 0.28f), true);
                Stretch(outline.rectTransform);
            }

            TextMeshProUGUI text = Text(root, "Rotulo", label, fontSize, primary ? Color.white : UiTema.Texto, FontStyles.Bold, TextAlignmentOptions.Center, 1f);
            Stretch(text.rectTransform, 6f);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Truncate;

            var button = root.gameObject.AddComponent<Button>();
            button.targetGraphic = fill;
            button.transition = Selectable.Transition.None;

            var fx = root.gameObject.AddComponent<UiButtonFx>();
            fx.Target = fill;

            if (primary)
            {
                fx.Normal = accent;
                fx.Hover = Color.Lerp(accent, Color.white, 0.18f);
                fx.Pressed = Color.Lerp(accent, Color.black, 0.25f);
                fx.Disabled = new Color(0.32f, 0.34f, 0.38f, 1f);
            }
            else
            {
                fx.Normal = UiTema.Superficie;
                fx.Hover = UiTema.SuperficieForte;
                fx.Pressed = new Color(1f, 1f, 1f, 0.03f);
                fx.Disabled = new Color(1f, 1f, 1f, 0.03f);
            }

            return button;
        }

        public static TMP_Text Label(Button button) => button.transform.Find("Rotulo").GetComponent<TMP_Text>();

        /// <summary>Barra de progresso arredondada: trilho e preenchimento. Devolve o RectTransform do preenchimento (ancora X máxima = valor).</summary>
        public static RectTransform Bar(Transform parent, string name, Color fillColor, float height, out Image fillImage)
        {
            RectTransform track = Rect(name, parent);
            track.sizeDelta = new Vector2(100f, height);
            Image back = Img(track, "Trilho", UiSprites.Fill((int)(height / 2f)), new Color(1f, 1f, 1f, 0.14f), true);
            Stretch(back.rectTransform);
            fillImage = Img(track, "Preenchimento", UiSprites.Fill((int)(height / 2f)), fillColor, true);
            fillImage.rectTransform.anchorMin = Vector2.zero;
            fillImage.rectTransform.anchorMax = new Vector2(1f, 1f);
            fillImage.rectTransform.offsetMin = fillImage.rectTransform.offsetMax = Vector2.zero;

            return fillImage.rectTransform;
        }

        /// <summary>Posiciona a barra inteira (o trilho) a partir do RectTransform devolvido por Bar.</summary>
        public static void PlaceBar(RectTransform fill, Vector2 anchor, Vector2 position, Vector2 size)
        {
            Place((RectTransform)fill.parent, anchor, position, size);
        }

        public static void SetBar(RectTransform fill, float value)
        {
            fill.anchorMax = new Vector2(Mathf.Clamp01(value), 1f);
        }

        /// <summary>Estrela de cinco pontas.</summary>
        public static UiShape Star(Transform parent, string name, float size, Color color)
        {
            UiShape s = Gfx<UiShape>(parent, name);
            s.rectTransform.sizeDelta = new Vector2(size, size);
            s.SetPolygons(new[] { UiShape.Star() });
            s.color = color;

            return s;
        }
    }
}
