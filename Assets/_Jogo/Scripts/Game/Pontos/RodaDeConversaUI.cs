using System;
using System.Collections.Generic;
using Entregador.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Entregador.Game
{
    /// <summary>
    /// Interface da roda de conversa dos entregadores, montada em código (UGUI + TMP) no mesmo estilo do balcão (vidro escuro): o botão
    /// "Trocar ideia [E]" (pílula, também para toque) e o chat: balões com avatar (inicial) e nome de cada entregador, as falas do jogador
    /// à direita e de 2 a 3 respostas como botões. Só recebe dados e devolve cliques; as regras ficam em Conversa (Core).
    /// </summary>
    public sealed class RodaDeConversaUI : MonoBehaviour
    {
        private const int MaxFalas = 4;
        private const int MaxOpcoes = 3;

        private static readonly Color[] CoresAvatar =
        {
            new Color(0.92f, 0.45f, 0.20f), new Color(0.30f, 0.62f, 1f), new Color(0.22f, 0.75f, 0.50f),
            new Color(0.80f, 0.35f, 0.75f), new Color(0.95f, 0.70f, 0.15f), new Color(0.55f, 0.55f, 0.95f),
        };

        private sealed class Linha
        {
            public GameObject Raiz;
            public HorizontalLayoutGroup Layout;
            public Image Avatar;
            public TMP_Text Inicial;
            public GameObject AvatarRaiz;
            public TMP_Text Nome;
            public TMP_Text Texto;
            public Image Fundo;
        }

        private Canvas _canvas;
        private GameObject _promptRoot;
        private Button _promptButton;
        private TMP_Text _promptLabel;
        private Action _promptAction;
        private GameObject _dialogRoot;
        private TMP_Text _titulo;
        private TMP_Text _subtitulo;
        private readonly List<Linha> _linhas = new List<Linha>();
        private readonly List<Button> _botoes = new List<Button>();
        private readonly List<TMP_Text> _rotulos = new List<TMP_Text>();
        private readonly List<string> _opcoesAtuais = new List<string>();
        private Action<int> _aoEscolher;

        public bool Built => _canvas != null;

        public bool PromptVisible => _promptRoot != null && _promptRoot.activeSelf;

        public string PromptLabel => _promptLabel != null ? _promptLabel.text : string.Empty;

        public bool DialogueVisible => _dialogRoot != null && _dialogRoot.activeSelf;

        public IReadOnlyList<string> ChoiceLabels => _opcoesAtuais;

        /// <summary>Texto da última fala mostrada (testes).</summary>
        public string UltimaFala { get; private set; } = string.Empty;

        public void Build()
        {
            if (_canvas != null)
            {
                return;
            }

            if (EventSystem.current == null)
            {
                var sistema = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                sistema.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }

            var canvasObject = new GameObject("CanvasRoda", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            _canvas = canvasObject.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 21;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            _promptButton = UiKit.Pill(canvasObject.transform, "TrocarIdeia", string.Empty, UiBotaoEstilo.Primario, 60f, 22f);
            _promptRoot = _promptButton.gameObject;
            UiKit.Place((RectTransform)_promptRoot.transform, new Vector2(0.5f, 0f), new Vector2(-140f, 226f), new Vector2(340f, 60f));
            _promptButton.onClick.AddListener(() => _promptAction?.Invoke());
            _promptLabel = UiKit.Label(_promptButton);

            BuildDialogo(canvasObject.transform);
            HidePrompt();
            HideDialogue();
        }

        private void BuildDialogo(Transform parent)
        {
            RectTransform painel = UiKit.Panel(parent, "RodaDeConversa", UiTema.RaioCartao + 4, UiTema.VidroForte, true, true);
            _dialogRoot = painel.gameObject;
            UiKit.Place(painel, new Vector2(0.5f, 0f), new Vector2(-140f, 28f), new Vector2(860f, 0f));
            var layout = _dialogRoot.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 18, 20);
            layout.spacing = 10f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            _dialogRoot.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _titulo = UiKit.Text(painel, "Titulo", "Roda de entregadores", UiTema.TamSubtitulo, UiTema.Texto, FontStyles.Bold);
            Altura(_titulo.gameObject, 30f);
            _subtitulo = UiKit.Caps(painel, "Subtitulo", string.Empty, UiTema.TextoSuave);
            Altura(_subtitulo.gameObject, 18f);

            for (int i = 0; i < MaxFalas; i++)
            {
                _linhas.Add(CriarLinha(painel, i));
            }

            for (int i = 0; i < MaxOpcoes; i++)
            {
                Button botao = UiKit.Pill(painel, "Escolha" + i, string.Empty, i == 0 ? UiBotaoEstilo.Primario : UiBotaoEstilo.Secundario, 48f, 20f);
                Altura(botao.gameObject, 48f);
                _botoes.Add(botao);
                _rotulos.Add(UiKit.Label(botao));
            }
        }

        private static Linha CriarLinha(Transform pai, int indice)
        {
            var linha = new Linha();
            RectTransform raiz = UiKit.Rect("Fala" + indice, pai);
            linha.Raiz = raiz.gameObject;
            linha.Layout = linha.Raiz.AddComponent<HorizontalLayoutGroup>();
            linha.Layout.spacing = 10f;
            linha.Layout.childControlHeight = true;
            linha.Layout.childControlWidth = true;
            linha.Layout.childForceExpandHeight = false;
            linha.Layout.childForceExpandWidth = false;

            linha.Avatar = UiKit.Circle(raiz, "Avatar", CoresAvatar[0]);
            linha.AvatarRaiz = linha.Avatar.gameObject;
            var elemento = linha.AvatarRaiz.AddComponent<LayoutElement>();
            elemento.minWidth = elemento.preferredWidth = 42f;
            elemento.minHeight = elemento.preferredHeight = 42f;
            linha.Inicial = UiKit.Text(linha.Avatar.transform, "Inicial", "A", 22f, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
            UiKit.Stretch(linha.Inicial.rectTransform);

            RectTransform balao = UiKit.Panel(raiz, "Balao", 16, UiTema.SuperficieForte, false, true);
            linha.Fundo = balao.Find("Fundo").GetComponent<Image>();
            var balaoElemento = balao.gameObject.AddComponent<LayoutElement>();
            balaoElemento.preferredWidth = 640f;
            balaoElemento.minHeight = 44f;
            var balaoLayout = balao.gameObject.AddComponent<VerticalLayoutGroup>();
            balaoLayout.padding = new RectOffset(16, 16, 8, 10);
            balaoLayout.spacing = 1f;
            balaoLayout.childControlHeight = true;
            balaoLayout.childControlWidth = true;
            balaoLayout.childForceExpandHeight = false;
            linha.Nome = UiKit.Caps(balao, "Nome", string.Empty, UiTema.Aviso);
            Altura(linha.Nome.gameObject, 16f);
            linha.Texto = UiKit.Text(balao, "Texto", string.Empty, UiTema.TamCorpo - 1f, UiTema.Texto, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            linha.Texto.textWrappingMode = TextWrappingModes.Normal;

            return linha;
        }

        private static void Altura(GameObject go, float altura)
        {
            var elemento = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            elemento.preferredHeight = altura;
            elemento.minHeight = altura;
        }

        // ---------- Botão ----------

        public void ShowPrompt(string rotulo, Action aoClicar)
        {
            _promptRoot.SetActive(true);
            _promptLabel.text = rotulo;
            _promptAction = aoClicar;
        }

        public void HidePrompt()
        {
            _promptRoot.SetActive(false);
            _promptAction = null;
        }

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
        public static bool InteractPressed() => BalcaoUI.InteractPressed();

        /// <summary>Teclas 1, 2 e 3 escolhem a resposta (desktop). -1 se nenhuma.</summary>
        public static int NumeroPressionado()
        {
            Keyboard teclado = Keyboard.current;

            if (teclado == null)
            {
                return -1;
            }

            if (teclado.digit1Key.wasPressedThisFrame) return 0;
            if (teclado.digit2Key.wasPressedThisFrame) return 1;
            if (teclado.digit3Key.wasPressedThisFrame) return 2;

            return -1;
        }

        // ---------- Chat ----------

        public void ShowDialogue(IReadOnlyList<string> nomes)
        {
            HidePrompt();
            _dialogRoot.SetActive(true);
            _subtitulo.text = string.Join("  ·  ", nomes);
        }

        public void HideDialogue()
        {
            if (_dialogRoot != null)
            {
                _dialogRoot.SetActive(false);
            }

            _opcoesAtuais.Clear();
        }

        /// <summary>Mostra as últimas falas do chat e as respostas (vazio = só o botão de encerrar, rótulo em fechar).</summary>
        public void Mostrar(IReadOnlyList<FalaConversa> falas, IReadOnlyList<string> nomes, IReadOnlyList<string> opcoes, Action<int> aoEscolher)
        {
            _aoEscolher = aoEscolher;
            int primeira = Mathf.Max(0, falas.Count - MaxFalas);
            UltimaFala = falas.Count > 0 ? falas[falas.Count - 1].Texto : string.Empty;

            for (int i = 0; i < _linhas.Count; i++)
            {
                Linha linha = _linhas[i];
                int indice = primeira + i;
                bool usada = indice < falas.Count;
                linha.Raiz.SetActive(usada);

                if (!usada)
                {
                    continue;
                }

                FalaConversa fala = falas[indice];
                bool jogador = fala.Autor < 0;
                linha.Texto.text = fala.Texto;
                linha.Nome.text = fala.Nome;
                linha.Nome.color = jogador ? UiTema.Azul : UiTema.Aviso;
                linha.Fundo.color = jogador ? UiTema.Alfa(UiTema.Azul, 0.22f) : UiTema.SuperficieForte;
                linha.Layout.childAlignment = jogador ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
                linha.Avatar.color = jogador ? UiTema.Azul : CoresAvatar[fala.Autor % CoresAvatar.Length];
                linha.Inicial.text = fala.Nome.Substring(0, 1).ToUpperInvariant();

                // O jogador fala à direita: o avatar vai depois do balão.
                linha.AvatarRaiz.transform.SetSiblingIndex(jogador ? 1 : 0);
            }

            _opcoesAtuais.Clear();

            for (int i = 0; i < _botoes.Count; i++)
            {
                bool usada = opcoes != null && i < opcoes.Count;
                _botoes[i].gameObject.SetActive(usada);
                _botoes[i].onClick.RemoveAllListeners();

                if (!usada)
                {
                    continue;
                }

                int indice = i;
                _rotulos[i].text = opcoes[i];
                _opcoesAtuais.Add(opcoes[i]);
                _botoes[i].onClick.AddListener(() => _aoEscolher?.Invoke(indice));
            }
        }

        /// <summary>Clica na resposta pelo texto (testes). False se não existe.</summary>
        public bool ClickChoice(string parte)
        {
            for (int i = 0; i < _botoes.Count; i++)
            {
                if (_botoes[i].gameObject.activeSelf && _rotulos[i].text.Contains(parte))
                {
                    _botoes[i].onClick.Invoke();

                    return true;
                }
            }

            return false;
        }
    }
}
