using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Entregador.Game
{
    /// <summary>Uma ação de botão num painel.</summary>
    public readonly struct PanelButton
    {
        public readonly string Label;
        public readonly Action OnClick;

        public PanelButton(string label, Action onClick)
        {
            Label = label;
            OnClick = onClick;
        }
    }

    /// <summary>
    /// Toda a interface, montada por código: HUD de direção, botões de toque, minimapa, avisos e o painel genérico
    /// (título, texto e até seis botões) usado em todas as telas do fluxo.
    /// </summary>
    public sealed class GameUI : MonoBehaviour
    {
        private static readonly Color PanelColor = new Color(0.08f, 0.09f, 0.12f, 0.93f);
        private static readonly Color ButtonColor = new Color(0.96f, 0.55f, 0.1f, 1f);
        private static readonly Color HudColor = new Color(0f, 0f, 0f, 0.45f);

        private Canvas _canvas;
        private BikeInput _input;

        private GameObject _hudRoot;
        private TMP_Text _balanceText;
        private TMP_Text _dayText;
        private TMP_Text _targetText;
        private TMP_Text _timeText;
        private TMP_Text _speedText;
        private TMP_Text _gearText;
        private GameObject _limitPlate;
        private TMP_Text _limitText;
        private RectTransform _foodFill;
        private Image _foodFillImage;
        private TMP_Text _foodLabel;
        private Button _skipButton;
        private GameObject _controlsRoot;
        private RawImage _minimapImage;

        private GameObject _modalRoot;
        private TMP_Text _modalTitle;
        private TMP_Text _modalBody;
        private readonly List<Button> _modalButtons = new List<Button>();
        private readonly List<TMP_Text> _modalButtonLabels = new List<TMP_Text>();
        private readonly List<string> _currentLabels = new List<string>();

        private TMP_Text _toastText;
        private float _toastUntil;

        public Canvas Canvas => _canvas;

        public string ModalTitle => _modalTitle.text;

        public string ModalBody => _modalBody.text;

        public bool ModalVisible => _modalRoot.activeSelf;

        public IReadOnlyList<string> ModalButtonLabels => _currentLabels;

        public bool HudVisible => _hudRoot.activeSelf;

        public string ToastMessage => Time.unscaledTime < _toastUntil ? _toastText.text : string.Empty;

        public void Build(BikeInput input, Texture minimap, Action onSkip)
        {
            _input = input;

            EnsureEventSystem();

            var canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            _canvas = canvasObject.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;

            BuildHud(canvasObject.transform, minimap, onSkip);
            BuildModal(canvasObject.transform);
            BuildToast(canvasObject.transform);

            SetHudVisible(false);
            HideModal();
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            var systemObject = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            systemObject.transform.SetParent(null);
            systemObject.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        // ---------- HUD ----------

        private void BuildHud(Transform parent, Texture minimap, Action onSkip)
        {
            _hudRoot = NewRect("Hud", parent).gameObject;
            Stretch(_hudRoot.GetComponent<RectTransform>());

            // Saldo e dia, canto superior esquerdo.
            RectTransform topLeft = NewPanel("SaldoDia", _hudRoot.transform, HudColor, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -16f), new Vector2(300f, 84f));
            _balanceText = NewText("Saldo", topLeft, 34, TextAlignmentOptions.TopLeft, Color.white, new Vector2(14f, -6f), new Vector2(280f, 44f));
            _dayText = NewText("Dia", topLeft, 22, TextAlignmentOptions.TopLeft, new Color(0.85f, 0.9f, 1f), new Vector2(14f, -50f), new Vector2(280f, 30f));

            // Alvo e tempo, topo central.
            RectTransform topCenter = NewPanel("AlvoTempo", _hudRoot.transform, HudColor, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(520f, 84f));
            _targetText = NewText("Alvo", topCenter, 26, TextAlignmentOptions.Top, Color.white, new Vector2(0f, -6f), new Vector2(500f, 36f));
            _timeText = NewText("Tempo", topCenter, 24, TextAlignmentOptions.Top, new Color(0.85f, 0.9f, 1f), new Vector2(0f, -46f), new Vector2(500f, 32f));

            // Minimapa e botão de pular, canto superior direito.
            RectTransform mapFrame = NewPanel("MinimapaMoldura", _hudRoot.transform, new Color(1f, 1f, 1f, 0.8f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -16f), new Vector2(196f, 196f));
            var mapObject = new GameObject("Minimapa", typeof(RectTransform), typeof(RawImage));
            mapObject.transform.SetParent(mapFrame, false);
            RectTransform mapRect = mapObject.GetComponent<RectTransform>();
            Stretch(mapRect);
            mapRect.offsetMin = new Vector2(3f, 3f);
            mapRect.offsetMax = new Vector2(-3f, -3f);
            _minimapImage = mapObject.GetComponent<RawImage>();
            _minimapImage.texture = minimap;
            _minimapImage.enabled = minimap != null;

            // Atribuição obrigatória dos dados do mapa (licença ODbL), pequena, no canto de baixo do minimapa.
            RectTransform credit = NewPanel("AtribuicaoOSM", mapFrame, new Color(0f, 0f, 0f, 0.55f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(3f, 3f), new Vector2(190f, 16f));
            var creditObject = new GameObject("Texto", typeof(RectTransform), typeof(TextMeshProUGUI));
            creditObject.transform.SetParent(credit, false);
            Stretch(creditObject.GetComponent<RectTransform>());
            var creditText = creditObject.GetComponent<TextMeshProUGUI>();
            creditText.fontSize = 11;
            creditText.alignment = TextAlignmentOptions.Center;
            creditText.color = Color.white;
            creditText.richText = false;
            creditText.raycastTarget = false;
            creditText.text = "© OpenStreetMap contributors";

            _skipButton = NewButton("Pular", _hudRoot.transform, "Pular corrida", 22, new Color(0.35f, 0.38f, 0.45f, 0.95f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -222f), new Vector2(196f, 50f));
            _skipButton.onClick.AddListener(() => onSkip?.Invoke());

            // Velocímetro e comida, base central.
            RectTransform bottom = NewPanel("Painel", _hudRoot.transform, HudColor, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(280f, 132f));
            _speedText = NewText("Velocidade", bottom, 40, TextAlignmentOptions.Top, Color.white, new Vector2(0f, -4f), new Vector2(260f, 50f));
            RectTransform foodBack = NewPanel("ComidaFundo", bottom, new Color(1f, 1f, 1f, 0.2f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(240f, 18f));
            var fillObject = new GameObject("ComidaBarra", typeof(RectTransform), typeof(Image));
            fillObject.transform.SetParent(foodBack, false);
            _foodFill = fillObject.GetComponent<RectTransform>();
            _foodFill.anchorMin = Vector2.zero;
            _foodFill.anchorMax = Vector2.one;
            _foodFill.offsetMin = _foodFill.offsetMax = Vector2.zero;
            _foodFillImage = fillObject.GetComponent<Image>();
            _foodLabel = NewText("ComidaTexto", bottom, 16, TextAlignmentOptions.Bottom, Color.white, new Vector2(0f, 0f), new Vector2(240f, 24f));
            _foodLabel.rectTransform.anchorMin = _foodLabel.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            _foodLabel.rectTransform.pivot = new Vector2(0.5f, 0f);
            _foodLabel.rectTransform.anchoredPosition = new Vector2(0f, 34f);
            _foodLabel.text = "Comida";

            // Marcha e giro (texto simples; a próxima rodada de UI redesenha o painel).
            _gearText = NewText("Marcha", bottom, 16, TextAlignmentOptions.Top, new Color(0.85f, 0.9f, 1f), new Vector2(0f, -54f), new Vector2(260f, 22f));

            // Placa do limite de velocidade da rua (círculo vermelho com o número), à esquerda do velocímetro.
            RectTransform ring = NewPanel("PlacaLimiteAro", _hudRoot.transform, new Color(0.85f, 0.1f, 0.1f, 0.95f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-180f, 24f), new Vector2(64f, 64f));
            _limitPlate = ring.gameObject;
            RectTransform face = NewPanel("PlacaLimiteFace", ring, new Color(1f, 1f, 1f, 0.98f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(46f, 46f));
            _limitText = NewText("PlacaLimiteTexto", face, 24, TextAlignmentOptions.Center, Color.black, Vector2.zero, new Vector2(46f, 46f));
            _limitText.fontStyle = FontStyles.Bold;
            _limitText.rectTransform.anchorMin = _limitText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _limitText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _limitText.rectTransform.anchoredPosition = Vector2.zero;
            _limitPlate.SetActive(false);

            BuildControls(_hudRoot.transform);
        }

        /// <summary>
        /// Controles de toque sem botões: a metade esquerda da tela dirige (arrastar para os lados) e a direita acelera ou freia
        /// (arrastar para cima ou para baixo). As zonas são invisíveis; um anel discreto marca onde o dedo pousou.
        /// Ficam atrás de todo o resto do HUD (este objeto é o primeiro filho), então os botões continuam clicáveis.
        /// </summary>
        private void BuildControls(Transform parent)
        {
            _controlsRoot = NewRect("Controles", parent).gameObject;
            Stretch(_controlsRoot.GetComponent<RectTransform>());
            _controlsRoot.transform.SetAsFirstSibling();

            NewTouchZone("ZonaDirecao", new Vector2(0f, 0f), new Vector2(0.5f, 1f), TouchZoneMode.Direcao);
            NewTouchZone("ZonaPedal", new Vector2(0.5f, 0f), new Vector2(1f, 1f), TouchZoneMode.Pedal);
        }

        private void NewTouchZone(string name, Vector2 anchorMin, Vector2 anchorMax, TouchZoneMode mode)
        {
            RectTransform rect = NewRect(name, _controlsRoot.transform);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);

            // Imagem totalmente transparente, mas que recebe o toque.
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0f);
            image.raycastTarget = true;
            rect.gameObject.AddComponent<TouchZone>().Configure(_input, mode);
        }

        public void SetHudVisible(bool visible)
        {
            _hudRoot.SetActive(visible);

            if (!visible && _input != null)
            {
                _input.TouchSteer = _input.TouchGas = _input.TouchBrake = 0f;
            }
        }

        public void SetSkipVisible(bool visible)
        {
            _skipButton.gameObject.SetActive(visible);
        }

        public void SetMinimap(Texture texture)
        {
            _minimapImage.texture = texture;
            _minimapImage.enabled = texture != null;
        }

        public void UpdateHud(string balance, string day, string target, float elapsed, float expected, float speedKmh, float food,
            int limitKmh = 0, int gear = 0, float rpm = 0f)
        {
            if (_limitPlate != null)
            {
                _limitPlate.SetActive(limitKmh > 0);
                _limitText.text = limitKmh.ToString();
                _limitText.color = speedKmh > limitKmh * 1.05f ? new Color(0.8f, 0.05f, 0.05f) : Color.black;
            }

            if (_gearText != null)
            {
                _gearText.text = gear > 0 ? $"Marcha {gear}  |  {Mathf.RoundToInt(rpm / 100f) * 100} rpm" : string.Empty;
            }

            _balanceText.text = balance;
            _dayText.text = day;
            _targetText.text = target;
            _timeText.text = $"Tempo {FormatTime(elapsed)} / {FormatTime(expected)}";
            _timeText.color = elapsed > expected ? new Color(1f, 0.55f, 0.5f) : new Color(0.85f, 0.9f, 1f);
            _speedText.text = $"{Mathf.RoundToInt(speedKmh)} km/h";

            food = Mathf.Clamp01(food);
            _foodFill.anchorMax = new Vector2(food, 1f);
            _foodFillImage.color = Color.Lerp(new Color(0.9f, 0.2f, 0.2f), new Color(0.3f, 0.85f, 0.35f), food);
            _foodLabel.text = $"Comida {Mathf.RoundToInt(food * 100f)}%";
        }

        public static string FormatTime(float seconds)
        {
            int total = Mathf.Max(0, Mathf.RoundToInt(seconds));

            return $"{total / 60}:{total % 60:00}";
        }

        // ---------- Painel genérico ----------

        private void BuildModal(Transform parent)
        {
            var dim = NewPanel("PainelFundo", parent, new Color(0f, 0f, 0f, 0.55f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Stretch(dim);
            dim.GetComponent<Image>().raycastTarget = true;
            _modalRoot = dim.gameObject;

            RectTransform panel = NewPanel("Painel", dim, PanelColor, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 100f));
            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(32, 32, 22, 22);
            layout.spacing = 10f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _modalTitle = NewLayoutText("Titulo", panel, 40, FontStyles.Bold, ButtonColor, TextAlignmentOptions.Left);
            _modalBody = NewLayoutText("Corpo", panel, 26, FontStyles.Normal, Color.white, TextAlignmentOptions.TopLeft);

            for (int i = 0; i < 6; i++)
            {
                Button button = NewButton("Botao" + i, panel, "-", 28, ButtonColor, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                var element = button.gameObject.AddComponent<LayoutElement>();
                element.minHeight = 58f;
                element.preferredHeight = 58f;
                _modalButtons.Add(button);
                _modalButtonLabels.Add(button.GetComponentInChildren<TMP_Text>());
            }
        }

        public void ShowModal(string title, string body, params PanelButton[] buttons)
        {
            _modalRoot.SetActive(true);
            _modalTitle.text = title;
            _modalBody.text = body;
            _currentLabels.Clear();

            for (int i = 0; i < _modalButtons.Count; i++)
            {
                bool used = i < buttons.Length;
                _modalButtons[i].gameObject.SetActive(used);
                _modalButtons[i].onClick.RemoveAllListeners();
                _modalButtons[i].interactable = true;

                if (used)
                {
                    Action action = buttons[i].OnClick;
                    _modalButtonLabels[i].text = buttons[i].Label;
                    _modalButtons[i].onClick.AddListener(() => action?.Invoke());
                    _currentLabels.Add(buttons[i].Label);
                }
            }
        }

        /// <summary>Troca só o texto do corpo (contagem regressiva, por exemplo).</summary>
        public void SetModalBody(string body)
        {
            _modalBody.text = body;
        }

        public void SetModalButtonInteractable(int index, bool interactable)
        {
            _modalButtons[index].interactable = interactable;
        }

        public void HideModal()
        {
            _modalRoot.SetActive(false);
            _currentLabels.Clear();
        }

        /// <summary>Clica no botão pelo texto. Devolve false se não existe (testes e automação).</summary>
        public bool ClickModalButton(string label)
        {
            for (int i = 0; i < _modalButtons.Count; i++)
            {
                if (_modalButtons[i].gameObject.activeSelf && _modalButtons[i].interactable && _modalButtonLabels[i].text.Contains(label))
                {
                    _modalButtons[i].onClick.Invoke();

                    return true;
                }
            }

            return false;
        }

        // ---------- Aviso rápido ----------

        private void BuildToast(Transform parent)
        {
            RectTransform back = NewPanel("Aviso", parent, new Color(0f, 0f, 0f, 0.7f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 150f), new Vector2(640f, 56f));
            _toastText = NewText("Texto", back, 28, TextAlignmentOptions.Center, Color.white, Vector2.zero, new Vector2(620f, 50f));
            _toastText.rectTransform.anchoredPosition = Vector2.zero;
            back.gameObject.SetActive(false);
        }

        public void Toast(string message, float seconds = 2.5f)
        {
            _toastText.text = message;
            _toastUntil = Time.unscaledTime + seconds;
            _toastText.transform.parent.gameObject.SetActive(true);
        }

        /// <summary>Esconde o aviso na hora (antes de abrir um painel que ocuparia o mesmo lugar).</summary>
        public void ClearToast()
        {
            _toastUntil = 0f;
            _toastText.transform.parent.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (_toastText != null && _toastText.transform.parent.gameObject.activeSelf && Time.unscaledTime >= _toastUntil)
            {
                _toastText.transform.parent.gameObject.SetActive(false);
            }
        }

        // ---------- Ferramentas de montagem ----------

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            return go.GetComponent<RectTransform>();
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static RectTransform NewPanel(string name, Transform parent, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = anchorMin == anchorMax ? anchorMin : new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;

            return rect;
        }

        private TMP_Text NewText(string name, Transform parent, int size, TextAlignmentOptions anchor, Color color, Vector2 position, Vector2 boxSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = boxSize;

            var text = go.GetComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.alignment = anchor;
            text.color = color;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            text.richText = false;
            text.raycastTarget = false;

            // Texto centralizado usa o centro do painel como referência.
            if (anchor == TextAlignmentOptions.Top || anchor == TextAlignmentOptions.Center || anchor == TextAlignmentOptions.Bottom)
            {
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
            }

            return text;
        }

        private TMP_Text NewLayoutText(string name, Transform parent, int size, FontStyles style, Color color, TextAlignmentOptions anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = anchor;
            text.color = color;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            text.richText = false;
            text.raycastTarget = false;

            return text;
        }

        private Button NewButton(string name, Transform parent, string label, int fontSize, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size)
        {
            RectTransform rect = NewPanel(name, parent, color, anchorMin, anchorMax, position, size);
            rect.GetComponent<Image>().raycastTarget = true;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();

            var textObject = new GameObject("Texto", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(rect, false);
            Stretch(textObject.GetComponent<RectTransform>());
            var text = textObject.GetComponent<TextMeshProUGUI>();
            text.fontSize = fontSize;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.text = label;
            text.raycastTarget = false;

            return button;
        }
    }
}
