using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Entregador.Core;

namespace Entregador.Game
{
    /// <summary>Tudo que a tela de novo jogo mostra e os retornos de chamada de cada escolha.</summary>
    public sealed class SetupDados
    {
        public Personagem Personagem;
        public MotoModelo Modelo;
        public MotoOrigem Origem;
        public bool TemCnh;
        public string TextoOrigem = string.Empty;
        public string TextoCnh = string.Empty;
        public string NotaTitan = string.Empty;
        public string NotaTwister = string.Empty;
        public Action<Personagem> OnPersonagem;
        public Action<MotoOrigem> OnOrigem;
        public Action<MotoModelo> OnModelo;
        public Action<bool> OnCnh;
        public Action OnComecar;
    }

    public sealed partial class GameUI
    {
        public const string PrefVolMotor = "vol_motor";
        public const string PrefVolTransito = "vol_transito";
        public const string PrefVolAmbiente = "vol_ambiente";
        public const string PrefVolUi = "vol_ui";
        public const string PrefVolRadio = "vol_radio";
        public const string PrefVolGps = "vol_gps";

        private static readonly Personagem[] Pilotos = { Personagem.Jefinho, Personagem.Robsao, Personagem.Marcao, Personagem.SeuValdir, Personagem.Camila };

        private static readonly string[] PilotoDescricao =
        {
            "Camiseta vermelha e bermuda jeans", "Camisa azul-petróleo, parrudo", "Polo branca e jeans", "Colete refletivo, veterano", "Blusa laranja e capacete rosa",
        };

        private GameObject _screenRoot;
        private RectTransform _menuRoot;
        private RectTransform _setupRoot;
        private RectTransform _optionsRoot;
        private RectTransform _mapaRoot;
        private RawImage _mapaImagem;
        private Button _mapaFechar;
        private readonly Button[] _menuButtons = new Button[5];
        private SetupDados _setup;
        private readonly Image[] _charBg = new Image[5];
        private readonly Image[] _charLine = new Image[5];
        private readonly Image[] _charCheck = new Image[5];
        private Button _segAlugada;
        private Button _segBob;
        private TextMeshProUGUI _setupOrigemTexto;
        private readonly Image[] _motoLine = new Image[2];
        private readonly CanvasGroup[] _motoGroup = new CanvasGroup[2];
        private readonly TextMeshProUGUI[] _motoNota = new TextMeshProUGUI[2];
        private Image _cnhTrack;
        private RectTransform _cnhKnob;
        private TextMeshProUGUI _cnhTexto;

        // Título de dia.
        private CanvasGroup _dayGroup;
        private TextMeshProUGUI _dayTitle;
        private TextMeshProUGUI _daySub;
        private float _dayStart = -100f;
        private const float DayDuration = 2.1f;

        public bool ScreenVisible => _screenRoot != null && _screenRoot.activeSelf;

        public bool DayTitleVisible => _dayGroup != null && _dayGroup.gameObject.activeSelf;

        public string DayTitleText => _dayTitle != null ? _dayTitle.text : string.Empty;

        // ---------- Montagem geral ----------

        private void BuildScreens(Transform parent)
        {
            _screenRoot = UiKit.Rect("Telas", parent).gameObject;
            UiKit.Stretch(_screenRoot.GetComponent<RectTransform>());
            var raycast = _screenRoot.AddComponent<Image>();
            raycast.color = new Color(0f, 0f, 0f, 0f);
            raycast.raycastTarget = true;
            BuildMainMenu(_screenRoot.transform);
            BuildSetup(_screenRoot.transform);
            BuildOptions(_screenRoot.transform);
            BuildMapa(_screenRoot.transform);
            BuildRadioApp(_screenRoot.transform);
            _screenRoot.SetActive(false);
        }

        private void HideScreens()
        {
            if (_screenRoot != null)
            {
                _screenRoot.SetActive(false);
            }
        }

        private void ShowScreen(RectTransform which, string title, string body, PanelButton[] automation)
        {
            if (_modalRoot.activeSelf)
            {
                _modalRoot.SetActive(false);
            }

            _screenRoot.SetActive(true);
            _menuRoot.gameObject.SetActive(which == _menuRoot);
            _setupRoot.gameObject.SetActive(which == _setupRoot);
            _optionsRoot.gameObject.SetActive(which == _optionsRoot);
            _mapaRoot.gameObject.SetActive(which == _mapaRoot);
            _radioRoot.gameObject.SetActive(which == _radioRoot);
            _titleRaw = title;
            _bodyRaw = body ?? string.Empty;
            _offerMode = false;
            _currentLabels.Clear();
            _virtualButtons.Clear();

            foreach (PanelButton b in automation)
            {
                _currentLabels.Add(b.Label);
                _virtualButtons.Add(b);
            }
        }

        private void TickScreens(float dt)
        {
        }

        // ---------- Menu principal ----------

        private void BuildMainMenu(Transform parent)
        {
            _menuRoot = UiKit.Rect("MenuPrincipal", parent);
            UiKit.Stretch(_menuRoot);

            Image shade = UiKit.Img(_menuRoot, "Degrade", UiSprites.GradientH(), new Color(0.02f, 0.03f, 0.05f, 0.88f), false);
            shade.rectTransform.anchorMin = Vector2.zero;
            shade.rectTransform.anchorMax = new Vector2(0.6f, 1f);
            shade.rectTransform.offsetMin = shade.rectTransform.offsetMax = Vector2.zero;

            RectTransform col = UiKit.Rect("Coluna", _menuRoot);
            UiKit.Place(col, new Vector2(0f, 0.5f), new Vector2(110f, 0f), new Vector2(520f, 820f));

            Image bar = UiKit.Img(col, "Barra", UiSprites.Fill(2), UiTema.Destaque, true);
            UiKit.Place(bar.rectTransform, new Vector2(0f, 1f), new Vector2(0f, -4f), new Vector2(56f, 5f));
            TextMeshProUGUI kicker = UiKit.Caps(col, "Local", "Santo Amaro · São Paulo", UiTema.Destaque, TextAlignmentOptions.Left, 15f);
            UiKit.Place(kicker.rectTransform, new Vector2(0f, 1f), new Vector2(0f, -24f), new Vector2(500f, 22f));
            TextMeshProUGUI title = UiKit.Text(col, "Titulo", "Jogo do\nEntregador", 84f, UiTema.Texto, FontStyles.Bold, TextAlignmentOptions.TopLeft, -2f);
            UiKit.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(-2f, -54f), new Vector2(520f, 190f));
            title.lineSpacing = -18f;
            title.textWrappingMode = TextWrappingModes.Normal;
            TextMeshProUGUI sub = UiKit.Text(col, "Sub", "Uma semana de entregas de moto: aluguel, chuva, blitz e a meta do app.", 21f, UiTema.TextoSuave, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            UiKit.Place(sub.rectTransform, new Vector2(0f, 1f), new Vector2(0f, -250f), new Vector2(430f, 64f));
            sub.textWrappingMode = TextWrappingModes.Normal;
            sub.lineSpacing = 10f;

            string[] labels = { "Jogar", "Continuar", "Opções", "Créditos", "Sair" };

            for (int i = 0; i < 5; i++)
            {
                Button b = UiKit.Pill(col, "Botao" + i, labels[i], i == 0 ? UiBotaoEstilo.Primario : UiBotaoEstilo.Secundario, 62f, 22f);
                UiKit.Place((RectTransform)b.transform, new Vector2(0f, 1f), new Vector2(0f, -340f - i * 76f), new Vector2(380f, 62f));
                _menuButtons[i] = b;
            }

            TextMeshProUGUI foot = UiKit.Caps(col, "Versao", "Protótipo · feito com dados do OpenStreetMap", UiTema.TextoFraco, TextAlignmentOptions.Left, 12f);
            UiKit.Place(foot.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 6f), new Vector2(500f, 18f));
            _menuRoot.gameObject.SetActive(false);
        }

        /// <summary>Tela inicial sobre a câmera que gira pela cidade. Continuar só aparece se houver partida.</summary>
        public void ShowMainMenu(bool canContinue, Action onPlay, Action onContinue, Action onOptions, Action onCredits, Action onQuit)
        {
            Action[] actions = { onPlay, onContinue, onOptions, onCredits, onQuit };
            string[] labels = { "Jogar", "Continuar", "Opções", "Créditos", "Sair" };
            var automation = new System.Collections.Generic.List<PanelButton>();
            float y = -340f;

            for (int i = 0; i < 5; i++)
            {
                bool used = i != 1 || canContinue;
                _menuButtons[i].gameObject.SetActive(used);
                _menuButtons[i].onClick.RemoveAllListeners();

                if (!used)
                {
                    continue;
                }

                Action action = actions[i];
                _menuButtons[i].onClick.AddListener(() => action?.Invoke());
                ((RectTransform)_menuButtons[i].transform).anchoredPosition = new Vector2(0f, y);
                y -= 76f;
                automation.Add(new PanelButton(labels[i], action));
            }

            ShowScreen(_menuRoot, "Menu principal", string.Empty, automation.ToArray());
        }

        // ---------- Opções e créditos ----------

        private void BuildOptions(Transform parent)
        {
            _optionsRoot = UiKit.Rect("Opcoes", parent);
            UiKit.Stretch(_optionsRoot);
            Image dim = UiKit.Img(_optionsRoot, "Fundo", UiSprites.Fill(1), new Color(0f, 0f, 0f, 0.55f), false);
            UiKit.Stretch(dim.rectTransform);

            RectTransform card = UiKit.Panel(_optionsRoot, "Cartao", UiTema.RaioTelefone, UiTema.VidroForte);
            UiKit.Place(card, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(680f, 790f));
            TextMeshProUGUI title = UiKit.Text(card, "Titulo", "Opções", 34f, UiTema.Texto, FontStyles.Bold, TextAlignmentOptions.Left);
            UiKit.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(40f, -34f), new Vector2(500f, 44f));
            TextMeshProUGUI sub = UiKit.Caps(card, "Sub", "Volumes", UiTema.TextoFraco, TextAlignmentOptions.Left, 13f);
            UiKit.Place(sub.rectTransform, new Vector2(0f, 1f), new Vector2(40f, -86f), new Vector2(300f, 18f));

            string[] names = { "Motor", "Trânsito", "Ambiente", "Interface", "Rádio", "GPS" };
            AudioBus[] buses = { AudioBus.Motor, AudioBus.Transito, AudioBus.Ambiente, AudioBus.Ui, AudioBus.Radio, AudioBus.Gps };

            for (int i = 0; i < buses.Length; i++)
            {
                BuildSliderRow(card, names[i], buses[i], -122f - i * 74f);
            }

            BuildInternetRow(card, -122f - buses.Length * 74f - 2f);

            Button back = UiKit.Pill(card, "Voltar", "Voltar", UiBotaoEstilo.Primario, 58f, 21f);
            UiKit.Place((RectTransform)back.transform, new Vector2(0.5f, 0f), new Vector2(0f, 34f), new Vector2(300f, 58f));
            _optionsBack = back;
            _optionsRoot.gameObject.SetActive(false);
        }

        private Button _optionsBack;
        private Image _inetTrack;
        private RectTransform _inetKnob;

        /// <summary>Opção "Rádio pela internet" (padrão ligado) com o aviso de dados móveis.</summary>
        private void BuildInternetRow(Transform card, float y)
        {
            RectTransform row = UiKit.Panel(card, "LinhaInternet", 20, new Color(1f, 1f, 1f, 0.06f), false, true);
            UiKit.Place(row, new Vector2(0f, 1f), new Vector2(40f, y), new Vector2(600f, 78f));
            Image hit = row.Find("Fundo").GetComponent<Image>();
            hit.raycastTarget = true;
            var button = row.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = hit;
            TextMeshProUGUI title = UiKit.Text(row, "Titulo", "Rádio pela internet", 22f, UiTema.Texto, FontStyles.Bold, TextAlignmentOptions.Left);
            UiKit.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(22f, -11f), new Vector2(430f, 28f));
            TextMeshProUGUI note = UiKit.Text(row, "Nota", "Usa dados móveis. Desligado, só o rádio do jogo.", 15f, UiTema.TextoSuave, FontStyles.Normal, TextAlignmentOptions.Left);
            UiKit.Place(note.rectTransform, new Vector2(0f, 1f), new Vector2(22f, -44f), new Vector2(470f, 22f));
            _inetTrack = UiKit.Img(row, "Trilho", UiSprites.Fill(17), UiTema.Sucesso, true);
            UiKit.Place(_inetTrack.rectTransform, new Vector2(1f, 0.5f), new Vector2(-22f, 0f), new Vector2(62f, 34f));
            Image knob = UiKit.Circle(_inetTrack.transform, "Bola", Color.white);
            UiKit.Place(knob.rectTransform, new Vector2(0f, 0.5f), new Vector2(31f, 0f), new Vector2(28f, 28f));
            _inetKnob = knob.rectTransform;
            button.onClick.AddListener(() =>
            {
                RadioPlayer.InternetAtiva = !RadioPlayer.InternetPreferida;
                RefreshInternetRow();
            });
        }

        private void RefreshInternetRow()
        {
            bool on = RadioPlayer.InternetPreferida;
            _inetTrack.color = on ? UiTema.Sucesso : new Color(1f, 1f, 1f, 0.2f);
            _inetKnob.anchoredPosition = new Vector2(on ? 31f : 3f, 0f);
        }

        /// <summary>Liga ou desliga o rádio pela internet (os testes usam; a tela das Opções faz o mesmo).</summary>
        public void ToggleInternetRadio()
        {
            RadioPlayer.InternetAtiva = !RadioPlayer.InternetPreferida;
            RefreshInternetRow();
        }

        private static void BuildSliderRow(Transform parent, string label, AudioBus bus, float y)
        {
            RectTransform row = UiKit.Rect("Linha_" + bus, parent);
            UiKit.Place(row, new Vector2(0f, 1f), new Vector2(40f, y), new Vector2(600f, 56f));
            TextMeshProUGUI name = UiKit.Text(row, "Nome", label, 22f, UiTema.Texto, FontStyles.Bold, TextAlignmentOptions.Left);
            UiKit.Place(name.rectTransform, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(150f, 30f));
            TextMeshProUGUI value = UiKit.Text(row, "Valor", "100%", 20f, UiTema.TextoSuave, FontStyles.Bold, TextAlignmentOptions.Right);
            UiKit.Place(value.rectTransform, new Vector2(1f, 0.5f), Vector2.zero, new Vector2(80f, 30f));

            RectTransform sl = UiKit.Rect("Slider", row);
            UiKit.Place(sl, new Vector2(0f, 0.5f), new Vector2(160f, 0f), new Vector2(340f, 30f));
            Image track = UiKit.Img(sl, "Trilho", UiSprites.Fill(5), new Color(1f, 1f, 1f, 0.16f), true);
            track.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            track.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            track.rectTransform.sizeDelta = new Vector2(0f, 10f);
            track.raycastTarget = true;

            RectTransform fillArea = UiKit.Rect("AreaPreench", sl);
            fillArea.anchorMin = new Vector2(0f, 0.5f);
            fillArea.anchorMax = new Vector2(1f, 0.5f);
            fillArea.offsetMin = new Vector2(0f, -5f);
            fillArea.offsetMax = new Vector2(0f, 5f);
            Image fill = UiKit.Img(fillArea, "Preenchimento", UiSprites.Fill(5), UiTema.Destaque, true);
            fill.rectTransform.sizeDelta = Vector2.zero;

            RectTransform handleArea = UiKit.Rect("AreaAlca", sl);
            UiKit.Stretch(handleArea);
            handleArea.offsetMin = new Vector2(14f, 0f);
            handleArea.offsetMax = new Vector2(-14f, 0f);
            Image handle = UiKit.Circle(handleArea, "Alca", Color.white);
            handle.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            handle.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            handle.rectTransform.sizeDelta = new Vector2(28f, 28f);
            handle.raycastTarget = true;

            var slider = sl.gameObject.AddComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.transition = Selectable.Transition.None;
            slider.SetValueWithoutNotify(AudioSettingsJogo.Get(bus));
            value.text = Mathf.RoundToInt(slider.value * 100f) + "%";
            slider.onValueChanged.AddListener(v =>
            {
                AudioSettingsJogo.Set(bus, v);
                value.text = Mathf.RoundToInt(v * 100f) + "%";
            });
        }

        /// <summary>Opções de som (os volumes ficam nas PlayerPrefs vol_motor, vol_transito, vol_ambiente e vol_ui; valores de 0 a 1).</summary>
        public void ShowOptions(Action onBack)
        {
            _optionsBack.onClick.RemoveAllListeners();
            _optionsBack.onClick.AddListener(() => onBack?.Invoke());
            RefreshInternetRow();
            ShowScreen(_optionsRoot, "Opções", string.Empty, new[] { new PanelButton("Voltar", onBack) });
        }

        public void ShowCredits(Action onBack)
        {
            string text = "Mapa · © OpenStreetMap contributors (licença ODbL)\n" +
                "Personagens · Mixamo (Adobe)\n" +
                "Fiat Uno · \"Fiat Uno 2001\" por B.M.A (Sketchfab, CC-BY 4.0)\n" +
                "Chevrolet Onix · \"chevrolet onix\" por uzb_rx7 (Sketchfab, CC-BY 4.0)\n" +
                "Texturas e céus · Poly Haven (CC0)\n" +
                "Modelos · Kenney (CC0)\n" +
                RadioPlayer.TextoCreditos();
            ShowModal("Créditos", text, new PanelButton("Voltar", onBack));
        }

        // ---------- Mapa ----------

        private void BuildMapa(Transform parent)
        {
            _mapaRoot = UiKit.Rect("Mapa", parent);
            UiKit.Stretch(_mapaRoot);
            Image dim = UiKit.Img(_mapaRoot, "Fundo", UiSprites.Fill(1), new Color(0f, 0f, 0f, 0.6f), false);
            UiKit.Stretch(dim.rectTransform);
            RectTransform card = UiKit.Panel(_mapaRoot, "Cartao", UiTema.RaioTelefone, UiTema.VidroForte);
            UiKit.Place(card, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 860f));
            TextMeshProUGUI title = UiKit.Text(card, "Titulo", "Mapa", 32f, UiTema.Texto, FontStyles.Bold, TextAlignmentOptions.Left);
            UiKit.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(36f, -28f), new Vector2(400f, 42f));
            RectTransform mask = UiKit.Rect("Mascara", card);
            UiKit.Place(mask, new Vector2(0.5f, 1f), new Vector2(0f, -86f), new Vector2(688f, 688f));
            var maskImage = mask.gameObject.AddComponent<Image>();
            maskImage.sprite = UiSprites.Fill(26);
            maskImage.type = Image.Type.Sliced;
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var raw = new GameObject("Imagem", typeof(RectTransform), typeof(RawImage));
            raw.transform.SetParent(mask, false);
            UiKit.Stretch(raw.GetComponent<RectTransform>());
            _mapaImagem = raw.GetComponent<RawImage>();
            Image arrow = UiKit.Circle(mask, "Voce", Color.white);
            UiKit.Place(arrow.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(26f, 26f));
            Image dot = UiKit.Circle(arrow.transform, "Ponto", UiTema.Destaque);
            UiKit.Stretch(dot.rectTransform, 6f);
            _mapaFechar = UiKit.Pill(card, "Fechar", "Voltar", UiBotaoEstilo.Primario, 56f, 20f);
            UiKit.Place((RectTransform)_mapaFechar.transform, new Vector2(0.5f, 0f), new Vector2(0f, 28f), new Vector2(300f, 56f));
            _mapaRoot.gameObject.SetActive(false);
        }

        /// <summary>Mapa grande da região (a textura do minimapa), com a moto no centro.</summary>
        public void ShowMapa(Texture texture, Action onClose)
        {
            _mapaImagem.texture = texture;
            _mapaImagem.enabled = texture != null;
            _mapaFechar.onClick.RemoveAllListeners();
            _mapaFechar.onClick.AddListener(() => onClose?.Invoke());
            ShowScreen(_mapaRoot, "Mapa", string.Empty, new[] { new PanelButton("Voltar", onClose) });
        }

        // ---------- Novo jogo ----------

        private void BuildSetup(Transform parent)
        {
            _setupRoot = UiKit.Rect("NovoJogo", parent);
            UiKit.Stretch(_setupRoot);

            RectTransform banner = UiKit.Panel(_setupRoot, "Cabecalho", 26, UiTema.Vidro);
            UiKit.Place(banner, new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(560f, 96f));
            TextMeshProUGUI kicker = UiKit.Caps(banner, "Kicker", "Novo jogo", UiTema.Destaque, TextAlignmentOptions.Center, 14f);
            UiKit.Place(kicker.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(400f, 20f));
            TextMeshProUGUI head = UiKit.Text(banner, "Titulo", "Monte seu entregador", 36f, UiTema.Texto, FontStyles.Bold, TextAlignmentOptions.Center);
            UiKit.Place(head.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -38f), new Vector2(540f, 46f));

            // Coluna da esquerda: o piloto.
            RectTransform left = UiKit.Panel(_setupRoot, "Piloto", 30, UiTema.Vidro);
            UiKit.Place(left, new Vector2(0f, 0.5f), new Vector2(56f, -26f), new Vector2(470f, 640f));
            TextMeshProUGUI lc = UiKit.Caps(left, "Cab", "Piloto", UiTema.TextoFraco, TextAlignmentOptions.Left, 13f);
            UiKit.Place(lc.rectTransform, new Vector2(0f, 1f), new Vector2(28f, -26f), new Vector2(300f, 18f));

            for (int i = 0; i < 5; i++)
            {
                BuildPilotoRow(left, i, -56f - i * 112f);
            }

            // Coluna da direita: moto e documentos.
            RectTransform right = UiKit.Panel(_setupRoot, "Moto", 30, UiTema.Vidro);
            UiKit.Place(right, new Vector2(1f, 0.5f), new Vector2(-56f, -26f), new Vector2(660f, 640f));
            TextMeshProUGUI rc = UiKit.Caps(right, "Cab", "Moto", UiTema.TextoFraco, TextAlignmentOptions.Left, 13f);
            UiKit.Place(rc.rectTransform, new Vector2(0f, 1f), new Vector2(28f, -26f), new Vector2(300f, 18f));

            _segAlugada = UiKit.Pill(right, "Alugada", "Alugada · documentada", UiBotaoEstilo.Secundario, 52f, 18f);
            UiKit.Place((RectTransform)_segAlugada.transform, new Vector2(0f, 1f), new Vector2(28f, -52f), new Vector2(296f, 52f));
            _segAlugada.onClick.AddListener(() => _setup?.OnOrigem?.Invoke(MotoOrigem.Alugada));
            _segBob = UiKit.Pill(right, "Bob", "Bob · sem documentos", UiBotaoEstilo.Secundario, 52f, 18f);
            UiKit.Place((RectTransform)_segBob.transform, new Vector2(0f, 1f), new Vector2(336f, -52f), new Vector2(296f, 52f));
            _segBob.onClick.AddListener(() => _setup?.OnOrigem?.Invoke(MotoOrigem.Bob));

            _setupOrigemTexto = UiKit.Text(right, "Origem", string.Empty, 17f, UiTema.TextoSuave, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            UiKit.Place(_setupOrigemTexto.rectTransform, new Vector2(0f, 1f), new Vector2(28f, -116f), new Vector2(604f, 50f));
            _setupOrigemTexto.textWrappingMode = TextWrappingModes.Normal;

            BuildMotoCard(right, 0, MotoModelo.Titan, 28f);
            BuildMotoCard(right, 1, MotoModelo.Twister, 336f);

            // CNH.
            RectTransform cnh = UiKit.Panel(right, "Cnh", 20, new Color(1f, 1f, 1f, 0.06f), false, true);
            UiKit.Place(cnh, new Vector2(0f, 1f), new Vector2(28f, -428f), new Vector2(604f, 100f));
            Button cnhButton = cnh.gameObject.AddComponent<Button>();
            cnhButton.transition = Selectable.Transition.None;
            var cnhHit = cnh.Find("Fundo").GetComponent<Image>();
            cnhHit.raycastTarget = true;
            cnhButton.targetGraphic = cnhHit;
            cnhButton.onClick.AddListener(() => _setup?.OnCnh?.Invoke(!_setup.TemCnh));
            _cnhTrack = UiKit.Img(cnh, "Trilho", UiSprites.Fill(17), UiTema.Sucesso, true);
            UiKit.Place(_cnhTrack.rectTransform, new Vector2(0f, 0.5f), new Vector2(20f, 0f), new Vector2(62f, 34f));
            Image knob = UiKit.Circle(_cnhTrack.transform, "Bola", Color.white);
            UiKit.Place(knob.rectTransform, new Vector2(0f, 0.5f), new Vector2(3f, 0f), new Vector2(28f, 28f));
            _cnhKnob = knob.rectTransform;
            TextMeshProUGUI cnhTitle = UiKit.Text(cnh, "Titulo", "Tenho CNH categoria A", 21f, UiTema.Texto, FontStyles.Bold, TextAlignmentOptions.Left);
            UiKit.Place(cnhTitle.rectTransform, new Vector2(0f, 1f), new Vector2(104f, -14f), new Vector2(480f, 28f));
            _cnhTexto = UiKit.Text(cnh, "Risco", string.Empty, 16f, UiTema.TextoSuave, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            UiKit.Place(_cnhTexto.rectTransform, new Vector2(0f, 1f), new Vector2(104f, -44f), new Vector2(484f, 48f));
            _cnhTexto.textWrappingMode = TextWrappingModes.Normal;

            Button start = UiKit.Pill(right, "Comecar", "Começar", UiBotaoEstilo.Primario, 62f, 22f);
            UiKit.Place((RectTransform)start.transform, new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(604f, 62f));
            start.onClick.AddListener(() => _setup?.OnComecar?.Invoke());
            _setupRoot.gameObject.SetActive(false);
        }

        private void BuildPilotoRow(RectTransform parent, int index, float y)
        {
            Personagem p = Pilotos[index];
            PersonagemConfig cfg = MotoCatalogo.Config(p);
            RectTransform row = UiKit.Rect("Piloto_" + p, parent);
            UiKit.Place(row, new Vector2(0f, 1f), new Vector2(24f, y), new Vector2(422f, 100f));
            _charBg[index] = UiKit.Img(row, "Fundo", UiSprites.Fill(22), UiTema.Superficie, true);
            UiKit.Stretch(_charBg[index].rectTransform);
            _charBg[index].raycastTarget = true;
            _charLine[index] = UiKit.Img(row, "Borda", UiSprites.Outline(22), new Color(1f, 1f, 1f, 0.1f), true);
            UiKit.Stretch(_charLine[index].rectTransform);

            Image avatar = UiKit.Circle(row, "Avatar", cfg.Capacete);
            UiKit.Place(avatar.rectTransform, new Vector2(0f, 0.5f), new Vector2(18f, 0f), new Vector2(64f, 64f));
            Image visor = UiKit.Circle(avatar.transform, "Viseira", new Color(0.05f, 0.06f, 0.08f, 0.95f));
            UiKit.Place(visor.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -4f), new Vector2(40f, 24f));
            Image stripe = UiKit.Img(avatar.transform, "Faixa", UiSprites.Fill(2), cfg.CapaceteDetalhe, true);
            UiKit.Place(stripe.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -4f), new Vector2(10f, 18f));

            TextMeshProUGUI name = UiKit.Text(row, "Nome", cfg.Nome, 26f, UiTema.Texto, FontStyles.Bold, TextAlignmentOptions.Left);
            UiKit.Place(name.rectTransform, new Vector2(0f, 0.5f), new Vector2(100f, 14f), new Vector2(260f, 32f));
            TextMeshProUGUI desc = UiKit.Text(row, "Desc", PilotoDescricao[index], 16f, UiTema.TextoSuave, FontStyles.Normal, TextAlignmentOptions.Left);
            UiKit.Place(desc.rectTransform, new Vector2(0f, 0.5f), new Vector2(100f, -16f), new Vector2(300f, 22f));
            desc.overflowMode = TextOverflowModes.Truncate;

            _charCheck[index] = UiKit.Circle(row, "Marca", UiTema.Destaque);
            UiKit.Place(_charCheck[index].rectTransform, new Vector2(1f, 0.5f), new Vector2(-22f, 0f), new Vector2(26f, 26f));
            Image tick = UiKit.Circle(_charCheck[index].transform, "Centro", Color.white);
            UiKit.Place(tick.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10f, 10f));

            var button = row.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = _charBg[index];
            button.onClick.AddListener(() => _setup?.OnPersonagem?.Invoke(p));
        }

        private void BuildMotoCard(RectTransform parent, int index, MotoModelo modelo, float x)
        {
            RectTransform card = UiKit.Rect("Moto_" + modelo, parent);
            UiKit.Place(card, new Vector2(0f, 1f), new Vector2(x, -176f), new Vector2(296f, 236f));
            _motoGroup[index] = card.gameObject.AddComponent<CanvasGroup>();
            Image bg = UiKit.Img(card, "Fundo", UiSprites.Fill(22), UiTema.Superficie, true);
            UiKit.Stretch(bg.rectTransform);
            bg.raycastTarget = true;
            _motoLine[index] = UiKit.Img(card, "Borda", UiSprites.Outline(22), new Color(1f, 1f, 1f, 0.1f), true);
            UiKit.Stretch(_motoLine[index].rectTransform);

            MotoFisicaPerfil perfil = MotoFisicaCatalogo.Para(modelo);
            bool titan = modelo == MotoModelo.Titan;
            TextMeshProUGUI name = UiKit.Text(card, "Nome", MotoCatalogo.Nome(modelo), 26f, UiTema.Texto, FontStyles.Bold, TextAlignmentOptions.Left);
            UiKit.Place(name.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -16f), new Vector2(260f, 32f));
            TextMeshProUGUI kind = UiKit.Caps(card, "Tipo", titan ? "Street clássica" : "Naked esportiva", UiTema.TextoFraco, TextAlignmentOptions.Left, 12f);
            UiKit.Place(kind.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -50f), new Vector2(260f, 16f));

            StatRow(card, "Velocidade", perfil.VelocidadeFinalKmh / 150f, Mathf.RoundToInt(perfil.VelocidadeFinalKmh) + " km/h", -78f);
            StatRow(card, "0 a 60 km/h", Mathf.InverseLerp(8f, 3f, titan ? 6f : 4.5f), (titan ? "6,0" : "4,5") + " s", -112f);
            StatRow(card, "Giro máximo", perfil.RpmLimite / 10000f, Mathf.RoundToInt(perfil.RpmLimite / 1000f) + " mil rpm", -146f);

            _motoNota[index] = UiKit.Text(card, "Nota", string.Empty, 15f, UiTema.TextoSuave, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            UiKit.Place(_motoNota[index].rectTransform, new Vector2(0f, 1f), new Vector2(20f, -184f), new Vector2(262f, 44f));
            _motoNota[index].textWrappingMode = TextWrappingModes.Normal;

            var button = card.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = bg;
            button.onClick.AddListener(() => _setup?.OnModelo?.Invoke(modelo));
        }

        private static void StatRow(Transform parent, string label, float value, string text, float y)
        {
            TextMeshProUGUI l = UiKit.Caps(parent, "Rot", label, UiTema.TextoFraco, TextAlignmentOptions.Left, 11f);
            UiKit.Place(l.rectTransform, new Vector2(0f, 1f), new Vector2(20f, y), new Vector2(120f, 14f));
            TextMeshProUGUI v = UiKit.Text(parent, "Val", text, 15f, UiTema.Texto, FontStyles.Bold, TextAlignmentOptions.Right);
            UiKit.Place(v.rectTransform, new Vector2(1f, 1f), new Vector2(-20f, y + 2f), new Vector2(120f, 18f));
            RectTransform bar = UiKit.Bar(parent, "Barra", UiTema.Destaque, 6f, out _);
            UiKit.PlaceBar(bar, new Vector2(0f, 1f), new Vector2(20f, y - 18f), new Vector2(256f, 6f));
            UiKit.SetBar(bar, Mathf.Clamp01(value));
        }

        /// <summary>
        /// Tela de novo jogo (piloto, moto, CNH). Chamada de novo a cada escolha: só atualiza o visual. Os botões de automação
        /// (testes) clicam pelo texto, como no painel antigo.
        /// </summary>
        public void ShowSetup(SetupDados data, params PanelButton[] automation)
        {
            _setup = data;
            ShowScreen(_setupRoot, "Novo jogo", data.TextoOrigem, automation);

            for (int i = 0; i < 5; i++)
            {
                bool selected = Pilotos[i] == data.Personagem;
                _charBg[i].color = selected ? UiTema.Alfa(UiTema.Destaque, 0.16f) : UiTema.Superficie;
                _charLine[i].color = selected ? UiTema.Destaque : new Color(1f, 1f, 1f, 0.1f);
                _charCheck[i].gameObject.SetActive(selected);
            }

            bool rented = data.Origem == MotoOrigem.Alugada;
            Paint(_segAlugada, rented);
            Paint(_segBob, !rented);
            _setupOrigemTexto.text = data.TextoOrigem;

            for (int m = 0; m < 2; m++)
            {
                MotoModelo model = m == 0 ? MotoModelo.Titan : MotoModelo.Twister;
                bool selected = data.ModeloEfetivo() == model;
                bool locked = !rented && model == MotoModelo.Twister;
                _motoLine[m].color = selected ? UiTema.Destaque : new Color(1f, 1f, 1f, 0.1f);
                _motoGroup[m].alpha = locked ? 0.4f : 1f;
                _motoNota[m].text = m == 0 ? data.NotaTitan : data.NotaTwister;
            }

            _cnhTrack.color = data.TemCnh ? UiTema.Sucesso : new Color(1f, 1f, 1f, 0.22f);
            _cnhKnob.anchoredPosition = new Vector2(data.TemCnh ? 31f : 3f, 0f);
            _cnhTexto.text = data.TextoCnh;
            _cnhTexto.color = data.TemCnh ? UiTema.TextoSuave : UiTema.Perigo;
        }

        private static void Paint(Button b, bool selected)
        {
            var fx = b.GetComponent<UiButtonFx>();
            Transform outline = b.transform.Find("Contorno");

            if (outline != null)
            {
                outline.gameObject.SetActive(!selected);
            }

            fx.Normal = selected ? UiTema.Destaque : UiTema.Superficie;
            fx.Hover = selected ? Color.Lerp(UiTema.Destaque, Color.white, 0.15f) : UiTema.SuperficieForte;
            fx.Pressed = selected ? Color.Lerp(UiTema.Destaque, Color.black, 0.25f) : new Color(1f, 1f, 1f, 0.03f);
            UiKit.Label(b).color = selected ? Color.white : UiTema.TextoSuave;
        }

        // ---------- Título de dia ----------

        private void BuildDayTitle(Transform parent)
        {
            RectTransform root = UiKit.Rect("TituloDia", parent);
            UiKit.Stretch(root);
            Image black = root.gameObject.AddComponent<Image>();
            black.color = new Color(0.01f, 0.015f, 0.025f, 1f);
            black.raycastTarget = false;
            _dayGroup = root.gameObject.AddComponent<CanvasGroup>();
            _dayGroup.blocksRaycasts = false;
            _dayGroup.interactable = false;

            Image bar = UiKit.Img(root, "Barra", UiSprites.Fill(2), UiTema.Destaque, true);
            UiKit.Place(bar.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 62f), new Vector2(64f, 5f));
            _dayTitle = UiKit.Text(root, "Titulo", string.Empty, 76f, UiTema.Texto, FontStyles.Bold, TextAlignmentOptions.Center, -1f);
            UiKit.Place(_dayTitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(1200f, 100f));
            _daySub = UiKit.Caps(root, "Sub", string.Empty, UiTema.TextoSuave, TextAlignmentOptions.Center, 20f);
            UiKit.Place(_daySub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -66f), new Vector2(1200f, 28f));
            root.gameObject.SetActive(false);
        }

        /// <summary>Cartão de título de dia: escurece rápido, mostra o título e some. Não bloqueia os toques.</summary>
        public void ShowDayTitle(string title, string subtitle = "")
        {
            _dayTitle.text = title;
            _daySub.text = subtitle ?? string.Empty;
            _dayStart = Time.unscaledTime;
            _dayGroup.alpha = 0f;
            _dayGroup.gameObject.SetActive(true);
        }

        private void TickDayTitle()
        {
            if (_dayGroup == null || !_dayGroup.gameObject.activeSelf)
            {
                return;
            }

            float t = Time.unscaledTime - _dayStart;

            if (t >= DayDuration)
            {
                _dayGroup.gameObject.SetActive(false);

                return;
            }

            _dayGroup.alpha = Mathf.Min(Ease(t / 0.25f), Ease((DayDuration - t) / 0.6f));
            float rise = Ease((t - 0.1f) / 0.5f);
            _dayTitle.rectTransform.anchoredPosition = new Vector2(0f, (1f - rise) * -18f);
        }
    }

    internal static class SetupDadosExtensions
    {
        public static MotoModelo ModeloEfetivo(this SetupDados d)
        {
            return d.Origem == MotoOrigem.Bob ? MotoModelo.Titan : d.Modelo;
        }
    }
}
