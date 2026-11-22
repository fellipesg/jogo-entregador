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
        private const int MaxChoices = 4;

        private static readonly Color[] AvatarColors =
        {
            new Color(0.92f, 0.45f, 0.20f), new Color(0.30f, 0.62f, 1f), new Color(0.22f, 0.75f, 0.50f),
            new Color(0.80f, 0.35f, 0.75f), new Color(0.95f, 0.70f, 0.15f),
        };

        private Canvas _canvas;

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
        private TMP_Text _subtitleText;
        private Image _avatar;
        private TMP_Text _avatarLetter;
        private GameObject _barRoot;
        private RectTransform _barFill;
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

        public string Subtitle => _subtitleText != null ? _subtitleText.text : string.Empty;

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

            var canvasObject = new GameObject("CanvasBalcao", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            _canvas = canvasObject.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 20;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
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
            _promptButton = UiKit.Pill(parent, "Prompt", string.Empty, UiBotaoEstilo.Sucesso, 60f, 22f);
            _promptRoot = _promptButton.gameObject;
            UiKit.Place((RectTransform)_promptRoot.transform, new Vector2(0.5f, 0f), new Vector2(-140f, 150f), new Vector2(340f, 60f));
            _promptButton.onClick.AddListener(() => _promptAction?.Invoke());
            _promptLabel = UiKit.Label(_promptButton);

            RectTransform hint = UiKit.Panel(parent, "Dica", UiTema.RaioChip, UiTema.Vidro, true, true);
            _hintRoot = hint.gameObject;
            UiKit.Place(hint, new Vector2(0.5f, 0f), new Vector2(-140f, 150f), new Vector2(560f, 48f));
            _hintText = UiKit.Text(hint, "Texto", string.Empty, UiTema.TamPequeno + 2f, UiTema.Texto, FontStyles.Normal, TextAlignmentOptions.Center);
            UiKit.Stretch(_hintText.rectTransform, 8f);
        }

        private void BuildChip(Transform parent)
        {
            // Código do pedido (como o app mostra), abaixo do botão do menu no canto superior esquerdo.
            RectTransform chip = UiKit.Panel(parent, "CodigoPedido", UiTema.RaioChip, UiTema.Vidro, true, true);
            _chipRoot = chip.gameObject;
            UiKit.Place(chip, new Vector2(0f, 1f), new Vector2(24f, -96f), new Vector2(300f, 40f));
            _chipText = UiKit.Text(chip, "Texto", string.Empty, UiTema.TamPequeno, UiTema.Texto, FontStyles.Bold, TextAlignmentOptions.Center);
            UiKit.Stretch(_chipText.rectTransform, 6f);
        }

        private void BuildDialogue(Transform parent)
        {
            RectTransform panel = UiKit.Panel(parent, "Balcao", UiTema.RaioCartao + 4, UiTema.VidroForte, true, true);
            _dialogRoot = panel.gameObject;
            UiKit.Place(panel, new Vector2(0.5f, 0f), new Vector2(-140f, 36f), new Vector2(760f, 0f));
            var layout = _dialogRoot.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 20, 22);
            layout.spacing = 12f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            _dialogRoot.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Cabeçalho: nome do restaurante e a função de quem atende.
            _nameText = UiKit.Text(panel, "Restaurante", string.Empty, UiTema.TamSubtitulo, UiTema.Texto, FontStyles.Bold);
            Sized(_nameText.gameObject, 30f);
            _subtitleText = UiKit.Caps(panel, "Subtitulo", "Atendente do balcão", UiTema.TextoSuave);
            Sized(_subtitleText.gameObject, 18f);

            // Conversa: avatar pequeno e balão de fala.
            RectTransform row = UiKit.Rect("Conversa", panel);
            var rowLayout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 12f;
            rowLayout.childControlHeight = true;
            rowLayout.childControlWidth = true;
            rowLayout.childForceExpandHeight = false;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childAlignment = TextAnchor.UpperLeft;

            _avatar = UiKit.Circle(row, "Avatar", AvatarColors[0]);
            var avatarElement = _avatar.gameObject.AddComponent<LayoutElement>();
            avatarElement.minWidth = avatarElement.preferredWidth = 48f;
            avatarElement.minHeight = avatarElement.preferredHeight = 48f;
            _avatarLetter = UiKit.Text(_avatar.transform, "Inicial", "A", 24f, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
            UiKit.Stretch(_avatarLetter.rectTransform);

            RectTransform bubble = UiKit.Panel(row, "Balao", 18, UiTema.SuperficieForte, false, true);
            var bubbleElement = bubble.gameObject.AddComponent<LayoutElement>();
            bubbleElement.flexibleWidth = 1f;
            bubbleElement.minHeight = 64f;
            var bubbleLayout = bubble.gameObject.AddComponent<VerticalLayoutGroup>();
            bubbleLayout.padding = new RectOffset(18, 18, 13, 14);
            bubbleLayout.childControlHeight = true;
            bubbleLayout.childControlWidth = true;
            bubbleLayout.childForceExpandHeight = false;
            _speechText = UiKit.Text(bubble, "Fala", string.Empty, UiTema.TamCorpo, UiTema.Texto, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            _speechText.textWrappingMode = TextWrappingModes.Normal;

            _codeText = UiKit.Text(panel, "CodigoDica", string.Empty, UiTema.TamPequeno + 2f, UiTema.Aviso, FontStyles.Bold);
            Sized(_codeText.gameObject, 26f);

            // Espera: rótulo e barra de progresso.
            RectTransform wait = UiKit.Rect("Espera", panel);
            _barRoot = wait.gameObject;
            var waitLayout = _barRoot.AddComponent<VerticalLayoutGroup>();
            waitLayout.spacing = 8f;
            waitLayout.childControlHeight = true;
            waitLayout.childControlWidth = true;
            waitLayout.childForceExpandHeight = false;
            _barLabel = UiKit.Text(wait, "Rotulo", string.Empty, UiTema.TamPequeno, UiTema.TextoSuave, FontStyles.Normal, TextAlignmentOptions.Left);
            Sized(_barLabel.gameObject, 22f);
            _barFill = UiKit.Bar(wait, "Barra", UiTema.Destaque, 12f, out _);
            Sized(_barFill.parent.gameObject, 12f);

            for (int i = 0; i < MaxChoices; i++)
            {
                Button button = UiKit.Pill(panel, "Escolha" + i, string.Empty, i == 0 ? UiBotaoEstilo.Primario : UiBotaoEstilo.Secundario, 50f, 20f);
                Sized(button.gameObject, 50f);
                _choiceButtons.Add(button);
                _choiceLabels.Add(UiKit.Label(button));
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

        /// <summary>Abre o diálogo. O subtítulo diz quem fala (atendente no balcão; cliente ou porteiro na entrega).</summary>
        public void ShowDialogue(string restaurante, string fala, IReadOnlyList<string> opcoes, Action<int> onChoose, string subtitulo = "Atendente do balcão")
        {
            _subtitleText.text = subtitulo;
            HidePrompt();
            HideHint();
            _dialogRoot.SetActive(true);
            _nameText.text = restaurante;
            int hash = 0;

            foreach (char c in restaurante ?? string.Empty)
            {
                hash = hash * 31 + c;
            }

            _avatar.color = AvatarColors[Mathf.Abs(hash) % AvatarColors.Length];
            _avatarLetter.text = string.IsNullOrEmpty(restaurante) ? "A" : restaurante.Substring(0, 1).ToUpperInvariant();
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
            }
        }

        /// <summary>Mostra a barra de espera (progresso de 0 a 1) e esconde os botões.</summary>
        public void ShowWait(string fala, float progress, string label)
        {
            _speechText.text = fala;
            _barRoot.SetActive(true);
            _barLabel.text = label;
            UiKit.SetBar(_barFill, progress);
            _currentChoices.Clear();

            foreach (Button button in _choiceButtons)
            {
                button.gameObject.SetActive(false);
            }
        }

/// <summary>Troca quem fala (porteiro, cliente) sem fechar o diálogo.</summary>
        public void SetSubtitle(string subtitulo)
        {
            _subtitleText.text = subtitulo;
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

        private static void Sized(GameObject go, float height)
        {
            var element = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            element.preferredHeight = height;
            element.minHeight = height;
        }
    }
}
