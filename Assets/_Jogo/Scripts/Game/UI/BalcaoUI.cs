using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Entregador.Game
{
    /// <summary>
    /// Interface do balcão do restaurante, montada em código (UGUI + TMP) num Canvas próprio: o botão "Retirar pedido" (e a tecla E) quando a
    /// moto está parada na zona, o chip com o código do pedido durante o trajeto e o diálogo com a atendente (balão de fala com o nome do
    /// restaurante, barra de espera e de 1 a 3 botões de resposta). Só recebe dados e devolve cliques: as regras ficam em Retirada (Core)
    /// e no GameFlow. Visual escuro e translúcido, fácil de reestilizar depois.
    /// </summary>
    public sealed class BalcaoUI : MonoBehaviour
    {
        private static readonly Color PanelColor = new Color(0.05f, 0.06f, 0.09f, 0.9f);
        private static readonly Color BubbleColor = new Color(0.17f, 0.2f, 0.27f, 0.96f);
        private static readonly Color ButtonColor = new Color(0.2f, 0.45f, 0.85f, 0.95f);
        private static readonly Color ButtonAltColor = new Color(0.32f, 0.34f, 0.4f, 0.95f);
        private static readonly Color AccentColor = new Color(0.16f, 0.7f, 0.4f, 0.95f);

        private const int MaxChoices = 4;

        private Canvas _canvas;
        private Sprite _rounded;

        private GameObject _promptRoot;
        private Button _promptButton;
        private TMP_Text _promptLabel;
        private Action _promptAction;

        private GameObject _hintRoot;
        private TMP_Text _hintText;

        private GameObject _chipRoot;
        private TMP_Text _chipText;

        private GameObject _dialogRoot;
        private TMP_Text _nameText;
        private TMP_Text _speechText;
        private TMP_Text _codeText;
        private GameObject _barRoot;
        private Image _barFill;
        private TMP_Text _barLabel;
        private readonly List<Button> _choiceButtons = new List<Button>();
        private readonly List<TMP_Text> _choiceLabels = new List<TMP_Text>();
        private readonly List<string> _currentChoices = new List<string>();

        public bool Built => _canvas != null;

        public bool PromptVisible => _promptRoot != null && _promptRoot.activeSelf;

        public string PromptLabel => _promptLabel != null ? _promptLabel.text : string.Empty;

        public bool HintVisible => _hintRoot != null && _hintRoot.activeSelf;

        public bool DialogueVisible => _dialogRoot != null && _dialogRoot.activeSelf;

        public string RestaurantName => _nameText != null ? _nameText.text : string.Empty;

        public string Speech => _speechText != null ? _speechText.text : string.Empty;

        public bool WaitBarVisible => _barRoot != null && _barRoot.activeSelf;

        public bool ChipVisible => _chipRoot != null && _chipRoot.activeSelf;

        public string ChipText => _chipText != null ? _chipText.text : string.Empty;

        public IReadOnlyList<string> ChoiceLabels => _currentChoices;

        // ---------- Montagem ----------

        public void Build()
        {
            if (_canvas != null)
            {
                return;
            }

            EnsureEventSystem();
            _rounded = MakeRoundedSprite();

            var canvasObject = new GameObject("CanvasBalcao", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            _canvas = canvasObject.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 20;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;

            BuildPrompt(canvasObject.transform);
            BuildChip(canvasObject.transform);
            BuildDialogue(canvasObject.transform);
            HidePrompt();
            HideDialogue();
            SetCode(null);
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            var systemObject = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            systemObject.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        private void BuildPrompt(Transform parent)
        {
            _promptRoot = Panel("Prompt", parent, AccentColor, new Vector2(0.5f, 0f), new Vector2(0f, 190f), new Vector2(380f, 70f));
            _promptButton = _promptRoot.AddComponent<Button>();
            _promptButton.targetGraphic = _promptRoot.GetComponent<Image>();
            _promptButton.onClick.AddListener(() => _promptAction?.Invoke());
            _promptLabel = Text("Rotulo", _promptRoot.transform, 30, TextAlignmentOptions.Center, Color.white);
            Stretch(_promptLabel.rectTransform, 8f);

            _hintRoot = Panel("Dica", parent, new Color(0f, 0f, 0f, 0.7f), new Vector2(0.5f, 0f), new Vector2(0f, 190f), new Vector2(520f, 54f));
            _hintText = Text("Texto", _hintRoot.transform, 24, TextAlignmentOptions.Center, Color.white);
            Stretch(_hintText.rectTransform, 8f);
        }

        private void BuildChip(Transform parent)
        {
            // Código do pedido (como o app mostra), abaixo do saldo no canto superior esquerdo.
            _chipRoot = Panel("CodigoPedido", parent, PanelColor, new Vector2(0f, 1f), new Vector2(16f, -112f), new Vector2(300f, 44f), new Vector2(0f, 1f));
            _chipText = Text("Texto", _chipRoot.transform, 22, TextAlignmentOptions.Center, new Color(0.85f, 0.95f, 1f));
            Stretch(_chipText.rectTransform, 6f);
        }

        private void BuildDialogue(Transform parent)
        {
            _dialogRoot = Panel("Balcao", parent, PanelColor, new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(780f, 0f));
            var layout = _dialogRoot.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(22, 22, 18, 20);
            layout.spacing = 10f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            _dialogRoot.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _nameText = Text("Restaurante", _dialogRoot.transform, 28, TextAlignmentOptions.Left, Color.white);
            _nameText.fontStyle = FontStyles.Bold;
            Sized(_nameText.gameObject, 36f);
            TMP_Text subtitle = Text("Subtitulo", _dialogRoot.transform, 16, TextAlignmentOptions.Left, new Color(0.65f, 0.72f, 0.85f));
            subtitle.text = "Atendente do balcão";
            Sized(subtitle.gameObject, 22f);

            GameObject bubble = Panel("Balao", _dialogRoot.transform, BubbleColor, Vector2.zero, Vector2.zero, Vector2.zero);
            var bubbleLayout = bubble.AddComponent<VerticalLayoutGroup>();
            bubbleLayout.padding = new RectOffset(18, 18, 14, 14);
            bubbleLayout.childControlHeight = true;
            bubbleLayout.childControlWidth = true;
            bubbleLayout.childForceExpandHeight = false;
            _speechText = Text("Fala", bubble.transform, 26, TextAlignmentOptions.TopLeft, Color.white);
            _speechText.textWrappingMode = TextWrappingModes.Normal;
            Sized(bubble, 0f, 76f);

            _codeText = Text("CodigoDica", _dialogRoot.transform, 22, TextAlignmentOptions.Left, new Color(1f, 0.9f, 0.4f));
            Sized(_codeText.gameObject, 28f);

            _barRoot = Panel("Espera", _dialogRoot.transform, new Color(1f, 1f, 1f, 0.12f), Vector2.zero, Vector2.zero, Vector2.zero);
            Sized(_barRoot, 30f);
            GameObject fill = Panel("Preenchimento", _barRoot.transform, AccentColor, Vector2.zero, Vector2.zero, Vector2.zero);
            _barFill = fill.GetComponent<Image>();
            _barFill.type = Image.Type.Sliced;
            var fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            fill.GetComponent<LayoutElement>().ignoreLayout = true;
            _barLabel = Text("Rotulo", _barRoot.transform, 18, TextAlignmentOptions.Center, Color.white);
            Stretch(_barLabel.rectTransform, 0f);
            _barLabel.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

            for (int i = 0; i < MaxChoices; i++)
            {
                GameObject button = Panel("Escolha" + i, _dialogRoot.transform, ButtonColor, Vector2.zero, Vector2.zero, Vector2.zero);
                Sized(button, 54f);
                var component = button.AddComponent<Button>();
                component.targetGraphic = button.GetComponent<Image>();
                TMP_Text label = Text("Rotulo", button.transform, 24, TextAlignmentOptions.Center, Color.white);
                Stretch(label.rectTransform, 8f);
                label.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                _choiceButtons.Add(component);
                _choiceLabels.Add(label);
            }
        }

        // ---------- Botão de retirar ----------

        public void ShowPrompt(string label, Action onClick)
        {
            _hintRoot.SetActive(false);
            _promptRoot.SetActive(true);
            _promptLabel.text = label;
            _promptAction = onClick;
        }

        public void HidePrompt()
        {
            _promptRoot.SetActive(false);
            _promptAction = null;
        }

        public void ShowHint(string text)
        {
            _promptRoot.SetActive(false);
            _hintRoot.SetActive(true);
            _hintText.text = text;
        }

        public void HideHint()
        {
            _hintRoot.SetActive(false);
        }

        /// <summary>Clica no botão "Retirar pedido" (testes e a tecla E).</summary>
        public bool ClickPrompt()
        {
            if (!PromptVisible)
            {
                return false;
            }

            _promptAction?.Invoke();

            return true;
        }

        /// <summary>A tecla E foi apertada neste quadro.</summary>
        public static bool InteractPressed()
        {
            Keyboard keyboard = Keyboard.current;

            return keyboard != null && keyboard.eKey.wasPressedThisFrame;
        }

        // ---------- Código do pedido ----------

        public void SetCode(string text)
        {
            if (_chipRoot == null)
            {
                return;
            }

            _chipRoot.SetActive(!string.IsNullOrEmpty(text));
            _chipText.text = text ?? string.Empty;
        }

        // ---------- Diálogo ----------

        public void ShowDialogue(string restaurante, string fala, IReadOnlyList<string> opcoes, Action<int> onChoose)
        {
            HidePrompt();
            HideHint();
            _dialogRoot.SetActive(true);
            _nameText.text = restaurante;
            SetCodeHint(null);
            SetSpeech(fala, opcoes, onChoose);
        }

        /// <summary>Troca a fala e os botões (vazio = nenhum botão).</summary>
        public void SetSpeech(string fala, IReadOnlyList<string> opcoes, Action<int> onChoose)
        {
            _speechText.text = fala;
            _barRoot.SetActive(false);
            _currentChoices.Clear();

            for (int i = 0; i < _choiceButtons.Count; i++)
            {
                bool used = opcoes != null && i < opcoes.Count;
                _choiceButtons[i].gameObject.SetActive(used);
                _choiceButtons[i].onClick.RemoveAllListeners();

                if (!used)
                {
                    continue;
                }

                int index = i;
                _choiceLabels[i].text = opcoes[i];
                _currentChoices.Add(opcoes[i]);
                _choiceButtons[i].onClick.AddListener(() => onChoose?.Invoke(index));
                _choiceButtons[i].GetComponent<Image>().color = i == 0 ? ButtonColor : ButtonAltColor;
            }
        }

        /// <summary>Mostra a barra de espera (progresso de 0 a 1) e esconde os botões.</summary>
        public void ShowWait(string fala, float progress, string label)
        {
            _speechText.text = fala;
            _barRoot.SetActive(true);
            _barLabel.text = label;
            RectTransform fill = _barFill.rectTransform;
            fill.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
            _currentChoices.Clear();

            foreach (Button button in _choiceButtons)
            {
                button.gameObject.SetActive(false);
            }
        }

        public void SetCodeHint(string text)
        {
            _codeText.gameObject.SetActive(!string.IsNullOrEmpty(text));
            _codeText.text = text ?? string.Empty;
        }

        public void HideDialogue()
        {
            if (_dialogRoot != null)
            {
                _dialogRoot.SetActive(false);
            }

            _currentChoices.Clear();
        }

        /// <summary>Clica no botão de resposta pelo texto (testes). False se não existe.</summary>
        public bool ClickChoice(string labelPart)
        {
            for (int i = 0; i < _choiceButtons.Count; i++)
            {
                if (_choiceButtons[i].gameObject.activeSelf && _choiceLabels[i].text.Contains(labelPart))
                {
                    _choiceButtons[i].onClick.Invoke();

                    return true;
                }
            }

            return false;
        }

        // ---------- Ferramentas ----------

        private GameObject Panel(string name, Transform parent, Color color, Vector2 anchor, Vector2 position, Vector2 size, Vector2? pivot = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = _rounded;
            image.type = Image.Type.Sliced;
            image.color = color;
            var rect = go.GetComponent<RectTransform>();

            if (size != Vector2.zero || anchor != Vector2.zero)
            {
                rect.anchorMin = anchor;
                rect.anchorMax = anchor;
                rect.pivot = pivot ?? new Vector2(anchor.x, anchor.y);
                rect.anchoredPosition = position;
                rect.sizeDelta = size;
            }

            return go;
        }

        private static TMP_Text Text(string name, Transform parent, float size, TextAlignmentOptions alignment, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;

            return text;
        }

        private static void Stretch(RectTransform rect, float margin)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(margin, margin);
            rect.offsetMax = new Vector2(-margin, -margin);
        }

        private static void Sized(GameObject go, float height, float minHeight = 0f)
        {
            var element = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            element.preferredHeight = height > 0f ? height : -1f;
            element.minHeight = minHeight > 0f ? minHeight : height;
        }

        /// <summary>Retângulo arredondado 9-slice gerado em código (sem arquivo de imagem).</summary>
        private static Sprite MakeRoundedSprite()
        {
            const int size = 48;
            const float radius = 14f;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(radius - x - 0.5f, x + 0.5f - (size - radius), 0f);
                    float dy = Mathf.Max(radius - y - 0.5f, y + 0.5f - (size - radius), 0f);
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01(radius - distance + 0.5f);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply();

            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }
    }
}
