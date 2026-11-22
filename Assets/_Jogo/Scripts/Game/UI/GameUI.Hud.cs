using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Entregador.Game
{
    public sealed partial class GameUI
    {
        private const float ShadeHeight = 450f;
        private const float ShadeWidth = 1140f;

        private GameObject _hudRoot;
        private PainelMotoView _painelView;
        private GameObject _controlsRoot;
        private RawImage _minimapImage;
        private RectTransform _northMarker;
        private Button _skipButton;
        private Button _menuButton;
        private UiShape _menuIcon;
        private UiShape _menuIconTela;
        private UiShape _pauseIcon;
        private Button _infoButton;

        // Navegação.
        private RectTransform _navRoot;
        private Image _navTile;
        private UiShape _navIcon;
        private TextMeshProUGUI _navDist;
        private TextMeshProUGUI _navRua;
        private TextMeshProUGUI _navDepois;
        private bool _navAtiva;
        private float _navDistM;
        private int _navManobra = -1;

        // Cortina de informações.
        private RectTransform _shadeRoot;
        private CanvasGroup _shadeDim;
        private GameObject _shadeCatcher;
        private float _shade;
        private float _shadeVel;
        private bool _shadeDragging;
        private bool _infoPinned;
        private bool _infoHold;
        private float _lastRefresh;
        private InfoPainel _info = new InfoPainel();
        private TextMeshProUGUI _iEtapa;
        private TextMeshProUGUI _iRota;
        private TextMeshProUGUI _iEndereco;
        private TextMeshProUGUI _iDist;
        private TextMeshProUGUI _iTempo;
        private Image _iTempoBarra;
        private RectTransform _iTempoFill;
        private TextMeshProUGUI _iPaga;
        private TextMeshProUGUI _iGorjeta;
        private TextMeshProUGUI _iProximo;
        private TextMeshProUGUI _iSaldo;
        private TextMeshProUGUI _iDia;
        private TextMeshProUGUI _iEntregas;
        private RectTransform _iEntregasFill;
        private UiShape[] _iStars = new UiShape[5];
        private TextMeshProUGUI _iNota;
        private TextMeshProUGUI _iMoto;
        private RectTransform _iCombFill;
        private Image _iCombCor;
        private TextMeshProUGUI _iCombTexto;
        private TextMeshProUGUI _iCnh;
        private TextMeshProUGUI _iMultas;
        private TextMeshProUGUI _iPontos;

        private float _speedKmh;
        private float _throttle;
        private float _brake;
        private Action _onMenu;

        public bool HudVisible => _hudRoot.activeSelf;

        // ---------- Montagem ----------

        private void BuildHud(Transform parent, Texture minimap, Action onSkip)
        {
            _hudRoot = UiKit.Rect("Hud", parent).gameObject;
            UiKit.Stretch(_hudRoot.GetComponent<RectTransform>());

            BuildControls(_hudRoot.transform);
            BuildShadeStrip(_hudRoot.transform);
            BuildMinimap(_hudRoot.transform, minimap, onSkip);
            BuildNav(_hudRoot.transform);

            _painelView = new PainelMotoView(_hudRoot.transform);
            _painelView.SetEstado(Painel);

            BuildCornerButtons(_hudRoot.transform);
            BuildOfertasHud(_hudRoot.transform);
        }

        /// <summary>
        /// Controles de toque sem botões: a metade esquerda dirige (arrastar para os lados) e a direita acelera ou freia (arrastar
        /// para cima ou para baixo). As zonas são invisíveis e ficam atrás do resto do HUD. A faixa de cima (12%) é da cortina.
        /// </summary>
        private void BuildControls(Transform parent)
        {
            _controlsRoot = UiKit.Rect("Controles", parent).gameObject;
            UiKit.Stretch(_controlsRoot.GetComponent<RectTransform>());

            NewTouchZone("ZonaDirecao", new Vector2(0f, 0f), new Vector2(0.5f, 1f), TouchZoneMode.Direcao);
            NewTouchZone("ZonaPedal", new Vector2(0.5f, 0f), new Vector2(1f, 1f), TouchZoneMode.Pedal);
        }

        private void NewTouchZone(string name, Vector2 anchorMin, Vector2 anchorMax, TouchZoneMode mode)
        {
            RectTransform rect = UiKit.Rect(name, _controlsRoot.transform);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);

            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0f);
            image.raycastTarget = true;
            rect.gameObject.AddComponent<TouchZone>().Configure(_input, mode);
        }

        /// <summary>Faixa invisível no topo da tela (12%) que recebe o arrasto para baixo da cortina, acima das zonas de direção.</summary>
        private void BuildShadeStrip(Transform parent)
        {
            RectTransform strip = UiKit.Rect("FaixaCortina", parent);
            strip.anchorMin = new Vector2(0f, 0.88f);
            strip.anchorMax = Vector2.one;
            strip.offsetMin = strip.offsetMax = Vector2.zero;
            var image = strip.gameObject.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0f);
            image.raycastTarget = true;
            strip.gameObject.AddComponent<UiShadeDrag>().Ui = this;
        }

        private void BuildMinimap(Transform parent, Texture minimap, Action onSkip)
        {
            RectTransform map = UiKit.Panel(parent, "Minimapa", 28, new Color(0.05f, 0.06f, 0.08f, 0.92f));
            UiKit.Place(map, new Vector2(1f, 1f), new Vector2(-24f, -24f), new Vector2(236f, 236f));
            var holdImage = map.gameObject.AddComponent<Image>();
            holdImage.color = new Color(1f, 1f, 1f, 0f);
            holdImage.raycastTarget = true;
            map.gameObject.AddComponent<UiHoldToShow>().Ui = this;

            RectTransform mask = UiKit.Rect("Mascara", map);
            UiKit.Stretch(mask, 5f);
            var maskImage = mask.gameObject.AddComponent<Image>();
            maskImage.sprite = UiSprites.Fill(23);
            maskImage.type = Image.Type.Sliced;
            maskImage.raycastTarget = false;
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            _minimapMask = mask;

            var mapObject = new GameObject("Mapa", typeof(RectTransform), typeof(RawImage));
            mapObject.transform.SetParent(mask, false);
            UiKit.Stretch(mapObject.GetComponent<RectTransform>());
            _minimapImage = mapObject.GetComponent<RawImage>();
            _minimapImage.texture = minimap;
            _minimapImage.enabled = minimap != null;
            _minimapImage.raycastTarget = false;
            _minimapImage.color = new Color(0.92f, 0.94f, 0.98f, 1f);

            // A moto no centro: seta de direção.
            Image halo = UiKit.Circle(mask, "Halo", new Color(1f, 1f, 1f, 0.9f));
            UiKit.Place(halo.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(30f, 30f));
            UiShape arrow = UiKit.Gfx<UiShape>(halo.transform, "Seta");
            UiKit.Stretch(arrow.rectTransform, 4f);
            arrow.SetPolygons(new[] { new[] { new Vector2(0.5f, 0.98f), new Vector2(0.92f, 0.06f), new Vector2(0.5f, 0.26f), new Vector2(0.08f, 0.06f) } });
            arrow.color = UiTema.Destaque;

            // Norte: marcador na borda que gira com a moto.
            RectTransform north = UiKit.Rect("Norte", mask);
            UiKit.Place(north, new Vector2(0.5f, 0.5f), new Vector2(0f, 88f), new Vector2(24f, 24f));
            Image northBg = UiKit.Circle(north, "Fundo", new Color(0.05f, 0.06f, 0.08f, 0.9f));
            UiKit.Stretch(northBg.rectTransform);
            TextMeshProUGUI n = UiKit.Text(north, "N", "N", 13f, UiTema.Texto, FontStyles.Bold, TextAlignmentOptions.Center);
            UiKit.Stretch(n.rectTransform);
            _northMarker = north;

            // Atribuição obrigatória dos dados do mapa (licença ODbL).
            RectTransform credit = UiKit.Rect("AtribuicaoOSM", mask);
            UiKit.Place(credit, new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(196f, 18f));
            Image creditBg = UiKit.Img(credit, "Fundo", UiSprites.Fill(8), new Color(0f, 0f, 0f, 0.55f), true);
            UiKit.Stretch(creditBg.rectTransform);
            TextMeshProUGUI creditText = UiKit.Text(credit, "Texto", "© OpenStreetMap contributors", 11f, Color.white, FontStyles.Normal, TextAlignmentOptions.Center);
            UiKit.Stretch(creditText.rectTransform);
            creditText.richText = false;

            Image border = UiKit.Img(map, "BordaFina", UiSprites.Outline(28), new Color(1f, 1f, 1f, 0.24f), true);
            UiKit.Stretch(border.rectTransform);

            _skipButton = UiKit.Pill(parent, "Pular", "Pular corrida", UiBotaoEstilo.Secundario, 40f, 16f);
            UiKit.Place((RectTransform)_skipButton.transform, new Vector2(1f, 1f), new Vector2(-24f, -(24f + 236f + 12f)), new Vector2(160f, 40f));
            _skipButton.onClick.AddListener(() => onSkip?.Invoke());

            // Botão de informações (ⓘ) ao lado do minimapa: alterna a cortina.
            _infoButton = UiKit.Pill(parent, "BotaoInfo", "i", UiBotaoEstilo.Secundario, 52f, 26f);
            UiKit.Place((RectTransform)_infoButton.transform, new Vector2(1f, 1f), new Vector2(-(24f + 236f + 12f), -24f), new Vector2(52f, 52f));
            _infoButton.onClick.AddListener(ToggleInfo);
            Glass(_infoButton);
            Glass(_skipButton);
        }

        private void BuildCornerButtons(Transform parent)
        {
            _menuButton = UiKit.Pill(parent, "BotaoMenu", string.Empty, UiBotaoEstilo.Secundario, 60f, 20f);
            UiKit.Place((RectTransform)_menuButton.transform, new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(60f, 60f));
            UiKit.Label(_menuButton).text = string.Empty;
            _menuIcon = UiKit.Gfx<UiShape>(_menuButton.transform, "Celular");
            UiKit.Stretch(_menuIcon.rectTransform, 12f);
            _menuIcon.SetPolygons(UiIcones.Celular());
            _menuIcon.color = UiTema.Texto;
            _menuIconTela = UiKit.Gfx<UiShape>(_menuButton.transform, "CelularTela");
            UiKit.Stretch(_menuIconTela.rectTransform, 12f);
            _menuIconTela.SetPolygons(UiIcones.CelularTela());
            _menuIconTela.color = new Color(0.055f, 0.067f, 0.086f, 1f);
            _pauseIcon = UiKit.Gfx<UiShape>(_menuButton.transform, "Pausa");
            UiKit.Stretch(_pauseIcon.rectTransform, 12f);
            _pauseIcon.SetPolygons(UiIcones.Pausa());
            _pauseIcon.color = UiTema.Texto;
            _pauseIcon.gameObject.SetActive(false);
            _menuButton.onClick.AddListener(() => _onMenu?.Invoke());
            Glass(_menuButton);
        }

        /// <summary>Botão secundário em vidro escuro (legível sobre a rua clara).</summary>
        private static void Glass(Button b)
        {
            var fx = b.GetComponent<UiButtonFx>();
            fx.Normal = new Color(0.055f, 0.067f, 0.086f, 0.82f);
            fx.Hover = new Color(0.12f, 0.14f, 0.17f, 0.9f);
            fx.Pressed = new Color(0.03f, 0.035f, 0.045f, 0.95f);
            fx.Disabled = fx.Normal;
            UiKit.Label(b).color = UiTema.Texto;
        }

        private void BuildNav(Transform parent)
        {
            _navRoot = UiKit.Panel(parent, "Navegacao", 26, UiTema.VidroForte);
            UiKit.Place(_navRoot, new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(480f, 118f));

            RectTransform tile = UiKit.Panel(_navRoot, "Tile", 20, UiTema.Destaque, false, false);
            UiKit.Place(tile, new Vector2(0f, 0.5f), new Vector2(15f, 0f), new Vector2(88f, 88f));
            _navTile = tile.Find("Fundo").GetComponent<Image>();
            _navIcon = UiKit.Gfx<UiShape>(tile, "Icone");
            UiKit.Stretch(_navIcon.rectTransform, 14f);
            _navIcon.color = Color.white;
            _navIcon.SetPolygons(UiIcones.Manobra(0));

            _navDist = UiKit.Text(_navRoot, "Distancia", "200 m", 44f, UiTema.Texto, FontStyles.Bold, TextAlignmentOptions.Left, -1f);
            UiKit.Place(_navDist.rectTransform, new Vector2(0f, 1f), new Vector2(122f, -10f), new Vector2(340f, 54f));
            _navRua = UiKit.Text(_navRoot, "Rua", string.Empty, 21f, UiTema.TextoSuave, FontStyles.Normal, TextAlignmentOptions.Left);
            UiKit.Place(_navRua.rectTransform, new Vector2(0f, 1f), new Vector2(122f, -64f), new Vector2(342f, 28f));
            _navRua.overflowMode = TextOverflowModes.Truncate;
            _navDepois = UiKit.Text(_navRoot, "Depois", string.Empty, 14f, UiTema.TextoFraco, FontStyles.Bold | FontStyles.UpperCase, TextAlignmentOptions.Left, 1.2f);
            UiKit.Place(_navDepois.rectTransform, new Vector2(0f, 1f), new Vector2(122f, -92f), new Vector2(342f, 18f));
            _navDepois.overflowMode = TextOverflowModes.Ellipsis;
            _navRoot.gameObject.SetActive(false);
        }

        private void BuildInfoShade(Transform parent)
        {
            // Fundo que escurece e captura o toque fora da cortina (fecha).
            _shadeCatcher = UiKit.Rect("CortinaFundo", parent).gameObject;
            UiKit.Stretch(_shadeCatcher.GetComponent<RectTransform>());
            var dimImage = _shadeCatcher.AddComponent<Image>();
            dimImage.color = new Color(0f, 0f, 0f, 0.4f);
            dimImage.raycastTarget = true;
            _shadeDim = _shadeCatcher.AddComponent<CanvasGroup>();
            var closeButton = _shadeCatcher.AddComponent<Button>();
            closeButton.transition = Selectable.Transition.None;
            closeButton.onClick.AddListener(CloseInfo);
            _shadeCatcher.AddComponent<UiShadeDrag>().Ui = this;
            _shadeCatcher.SetActive(false);

            _shadeRoot = UiKit.Panel(parent, "Cortina", 30, new Color(0.055f, 0.067f, 0.086f, 0.94f));
            UiKit.Place(_shadeRoot, new Vector2(0.5f, 1f), new Vector2(0f, ShadeHeight + 60f), new Vector2(ShadeWidth, ShadeHeight));
            var catcherImage = _shadeRoot.gameObject.AddComponent<Image>();
            catcherImage.color = new Color(1f, 1f, 1f, 0f);
            catcherImage.raycastTarget = true;
            _shadeRoot.gameObject.AddComponent<UiShadeDrag>().Ui = this;

            const float w = 316f;
            float[] cols = { 40f, 412f, 784f };

            for (int i = 1; i < 3; i++)
            {
                Image divider = UiKit.Img(_shadeRoot, "Divisor" + i, UiSprites.Fill(1), new Color(1f, 1f, 1f, 0.09f), false);
                UiKit.Place(divider.rectTransform, new Vector2(0f, 1f), new Vector2(cols[i] - 22f, -36f), new Vector2(1f, ShadeHeight - 90f));
            }

            RectTransform a = UiKit.Rect("ColunaPedido", _shadeRoot);
            UiKit.Place(a, new Vector2(0f, 1f), new Vector2(cols[0], 0f), new Vector2(w, ShadeHeight));
            Caps(a, "PEDIDO ATUAL", -30f, UiTema.TextoFraco);
            _iEtapa = Field(a, -58f, 14f, UiTema.Destaque, FontStyles.Bold | FontStyles.UpperCase, 4f);
            _iRota = Field(a, -80f, 24f, UiTema.Texto, FontStyles.Bold);
            _iRota.textWrappingMode = TextWrappingModes.Normal;
            _iRota.rectTransform.sizeDelta = new Vector2(w, 72f);
            _iRota.alignment = TextAlignmentOptions.TopLeft;
            _iEndereco = Field(a, -156f, 17f, UiTema.TextoSuave);
            _iDist = Field(a, -190f, 21f, UiTema.Texto);
            Caps(a, "TEMPO DA CORRIDA", -234f, UiTema.TextoFraco);
            _iTempo = Field(a, -254f, 24f, UiTema.Texto, FontStyles.Bold);
            _iTempoFill = UiKit.Bar(a, "BarraTempo", UiTema.Sucesso, 8f, out _iTempoBarra);
            UiKit.PlaceBar(_iTempoFill, new Vector2(0f, 1f), new Vector2(0f, -292f), new Vector2(w, 8f));
            Caps(a, "GANHO", -322f, UiTema.TextoFraco);
            _iPaga = Field(a, -342f, 32f, UiTema.Sucesso, FontStyles.Bold);
            _iGorjeta = Field(a, -382f, 17f, UiTema.TextoSuave);
            _iProximo = Field(a, -408f, 15f, UiTema.Texto);
            _iProximo.textWrappingMode = TextWrappingModes.Normal;
            _iProximo.rectTransform.sizeDelta = new Vector2(w, 34f);

            RectTransform b = UiKit.Rect("ColunaTurno", _shadeRoot);
            UiKit.Place(b, new Vector2(0f, 1f), new Vector2(cols[1], 0f), new Vector2(w, ShadeHeight));
            Caps(b, "TURNO", -30f, UiTema.TextoFraco);
            Caps(b, "SALDO", -62f, UiTema.TextoFraco);
            _iSaldo = Field(b, -82f, 42f, UiTema.Texto, FontStyles.Bold);
            _iDia = Field(b, -146f, 22f, UiTema.Texto, FontStyles.Bold);
            Caps(b, "ENTREGAS", -200f, UiTema.TextoFraco);
            _iEntregas = Field(b, -220f, 24f, UiTema.Texto, FontStyles.Bold);
            _iEntregasFill = UiKit.Bar(b, "BarraEntregas", UiTema.Destaque, 8f, out _);
            UiKit.PlaceBar(_iEntregasFill, new Vector2(0f, 1f), new Vector2(0f, -258f), new Vector2(w, 8f));
            Caps(b, "NOTA", -296f, UiTema.TextoFraco);

            for (int i = 0; i < 5; i++)
            {
                _iStars[i] = UiKit.Star(b, "Estrela" + i, 34f, new Color(1f, 1f, 1f, 0.16f));
                UiKit.Place(_iStars[i].rectTransform, new Vector2(0f, 1f), new Vector2(i * 38f, -320f), new Vector2(34f, 34f));
            }

            _iNota = Field(b, -322f, 22f, UiTema.Texto, FontStyles.Bold);
            _iNota.rectTransform.anchoredPosition = new Vector2(204f, -324f);
            _iNota.rectTransform.sizeDelta = new Vector2(100f, 30f);

            RectTransform c = UiKit.Rect("ColunaMoto", _shadeRoot);
            UiKit.Place(c, new Vector2(0f, 1f), new Vector2(cols[2], 0f), new Vector2(w, ShadeHeight));
            Caps(c, "MOTO E DOCUMENTOS", -30f, UiTema.TextoFraco);
            Caps(c, "MOTO", -62f, UiTema.TextoFraco);
            _iMoto = Field(c, -82f, 18f, UiTema.Texto, FontStyles.Bold);
            _iMoto.textWrappingMode = TextWrappingModes.Normal;
            _iMoto.rectTransform.sizeDelta = new Vector2(316f, 44f);
            Caps(c, "COMBUSTÍVEL", -130f, UiTema.TextoFraco);
            _iCombFill = UiKit.Bar(c, "BarraComb", UiTema.Aviso, 8f, out _iCombCor);
            UiKit.PlaceBar(_iCombFill, new Vector2(0f, 1f), new Vector2(0f, -158f), new Vector2(w - 70f, 8f));
            _iCombTexto = Field(c, -166f, 16f, UiTema.TextoSuave);
            _iCombTexto.rectTransform.anchoredPosition = new Vector2(w - 62f, -150f);
            _iCombTexto.rectTransform.sizeDelta = new Vector2(70f, 22f);
            Caps(c, "CNH", -204f, UiTema.TextoFraco);
            _iCnh = Field(c, -224f, 22f, UiTema.Texto, FontStyles.Bold);
            Caps(c, "MULTAS DE HOJE", -272f, UiTema.TextoFraco);
            _iMultas = Field(c, -292f, 22f, UiTema.Texto, FontStyles.Bold);
            Caps(c, "PONTOS NA CARTEIRA", -340f, UiTema.TextoFraco);
            _iPontos = Field(c, -360f, 22f, UiTema.Texto, FontStyles.Bold);

            Image grabber = UiKit.Img(_shadeRoot, "Alca", UiSprites.Fill(3), new Color(1f, 1f, 1f, 0.3f), true);
            UiKit.Place(grabber.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(64f, 6f));
            _shadeRoot.gameObject.SetActive(false);
        }

        private static void Caps(Transform parent, string text, float y, Color color)
        {
            TextMeshProUGUI t = UiKit.Caps(parent, "Rotulo", text, color, TextAlignmentOptions.Left, UiTema.TamMicro);
            UiKit.Place(t.rectTransform, new Vector2(0f, 1f), new Vector2(0f, y), new Vector2(316f, 18f));
        }

        private static TextMeshProUGUI Field(Transform parent, float y, float size, Color color, FontStyles style = FontStyles.Normal, float spacing = 0f)
        {
            TextMeshProUGUI t = UiKit.Text(parent, "Valor", string.Empty, size, color, style, TextAlignmentOptions.TopLeft, spacing);
            UiKit.Place(t.rectTransform, new Vector2(0f, 1f), new Vector2(0f, y), new Vector2(316f, size + 10f));
            t.overflowMode = TextOverflowModes.Truncate;

            return t;
        }

        // ---------- HUD: API ----------

        public void SetHudVisible(bool visible)
        {
            _hudRoot.SetActive(visible);

            if (!visible)
            {
                _infoPinned = _infoHold = false;
                _shade = 0f;
                ApplyShade();

                if (_input != null)
                {
                    _input.TouchSteer = _input.TouchGas = _input.TouchBrake = 0f;
                }
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

        /// <summary>Define o que o botão do canto de cima à esquerda faz: celular (menu, modo livre) ou pausa (turno).</summary>
        public void SetMenuButton(bool pause, Action onClick)
        {
            _onMenu = onClick;
            _menuIcon.gameObject.SetActive(!pause);
            _menuIconTela.gameObject.SetActive(!pause);
            _pauseIcon.gameObject.SetActive(pause);
        }

        /// <summary>Clica no botão do canto (menu ou pausa), para testes e atalhos de teclado.</summary>
        public void ClickMenuButton() => _onMenu?.Invoke();

        /// <summary>Compatível com a versão antiga: saldo, dia, alvo, tempo, velocidade e comida alimentam a cortina de informações.</summary>
        public void UpdateHud(string balance, string day, string target, float elapsed, float expected, float speedKmh, float food,
            int limitKmh = 0, int gear = 0, float rpm = 0f)
        {
            _speedKmh = speedKmh;
            _painelView.SetSpeed(speedKmh);
            _painelView.SetLimit(limitKmh);
            _painelView.SetData(rpm, gear, _throttle, _brake);

            if (!_infoExterna)
            {
                _info.Saldo = balance;
                _info.Dia = day;
                _info.DecorridoS = elapsed;
                _info.EsperadoS = expected;
                _info.Comida = food;

                int colon = target != null ? target.IndexOf(": ", StringComparison.Ordinal) : -1;

                if (colon > 0)
                {
                    _info.Etapa = target.Substring(0, colon).StartsWith("Retirar", StringComparison.Ordinal) ? "Retirar" : "Entregar";
                    _info.Restaurante = target.Substring(colon + 2);
                }
            }
        }

        private bool _infoExterna;

        /// <summary>Dados completos da cortina de informações (preferidos ao que o UpdateHud sabe).</summary>
        public void SetPainelInfo(InfoPainel info)
        {
            _info = info ?? new InfoPainel();
            _infoExterna = info != null;
        }

        /// <summary>Dados de corrida do painel: giro, marcha, limitador do modelo, acelerador, freio, limite da via e avisos.</summary>
        public void SetRacingData(float rpm, int gear, float limiterRpm, float throttle, float brake, int limitKmh, bool wrongWay = false, bool busLane = false, float speedKmh = -1f, bool cortandoGiro = false, bool noLimitador = false)
        {
            if (speedKmh >= 0f)
            {
                _speedKmh = speedKmh;
                _painelView.SetSpeed(speedKmh);
            }

            _throttle = throttle;
            _brake = brake;
            _painelView.SetLimiter(limiterRpm);
            _painelView.SetData(rpm, gear, throttle, brake);
            _painelView.SetLimit(limitKmh);
            _painelView.SetWarnings(wrongWay, busLane);
            _painelView.SetTransmissao(cortandoGiro, noLimitador);
            Painel.Simular(Time.deltaTime, _speedKmh, throttle, cortandoGiro ? 0 : gear);
        }

        /// <summary>Sinal vermelho à frente (até 60 m) e se o cruzamento tem fotossensor.</summary>
        public void SetSinal(bool vermelho, float metros, bool fotossensor)
        {
            _painelView.SetSinal(vermelho, metros, fotossensor);
        }

        public bool SinalVisivel => _painelView.SinalVisivel;

        public bool CameraSinalVisivel => _painelView.CameraVisivel;

        public float RpmMostrado => _painelView != null ? _painelView.RpmMostrado : 0f;

        /// <summary>Gira o marcador de norte do minimapa (a moto aponta para o alto).</summary>
        public void SetHeading(float yawDegrees)
        {
            float a = (90f + yawDegrees) * Mathf.Deg2Rad;
            _northMarker.anchoredPosition = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 88f;
        }

        // ---------- Navegação ----------

        /// <summary>Cartão de navegação do topo: manobra (0 siga em frente, 1 direita, 2 esquerda, 3 mantenha à direita, 4 mantenha à esquerda, 5 retorno, 6 rotatória, 7 chegada), distância, rua e dica "depois". Manobra negativa esconde.</summary>
        public void SetNavegacao(int manobra, float distanciaM, string rua, string proximaDepois = null)
        {
            if (manobra < 0)
            {
                HideNavegacao();

                return;
            }

            if (!_navAtiva)
            {
                _navAtiva = true;
                _navRoot.gameObject.SetActive(true);
            }

            if (manobra != _navManobra)
            {
                _navManobra = manobra;
                _navIcon.SetPolygons(UiIcones.Manobra(manobra));
            }

            _navDistM = distanciaM;
            _navDist.text = manobra == 7 && distanciaM < 15f ? "Chegou" : FormatDistance(distanciaM);
            _navRua.text = rua ?? string.Empty;
            string depois = string.IsNullOrEmpty(proximaDepois) ? string.Empty : "Depois: " + proximaDepois;
            _navDepois.text = depois;
        }

        public void HideNavegacao()
        {
            _navAtiva = false;
            _navManobra = -1;
            _navRoot.gameObject.SetActive(false);
        }

        public bool NavegacaoVisivel => _navAtiva;

        public string NavegacaoTexto => _navDist.text + " | " + _navRua.text;

        private void TickNav(float time)
        {
            if (!_navAtiva)
            {
                return;
            }

            bool near = _navDistM < 50f;
            float pulse = near ? 0.5f + 0.5f * Mathf.Sin(time * 9f) : 0f;
            _navRoot.localScale = Vector3.one * (1f + 0.045f * pulse);
            _navTile.color = Color.Lerp(UiTema.Destaque, new Color(1f, 0.5f, 0.45f), pulse);
        }

        // ---------- Cortina de informações ----------

        public float ShadeOpen => _shade;

        public bool InfoVisible => _shade > 0.5f;

        public void ToggleInfo()
        {
            _infoPinned = !_infoPinned;
        }

        public void OpenInfo()
        {
            _infoPinned = true;
        }

        public void CloseInfo()
        {
            _infoPinned = false;
            _infoHold = false;
        }

        public void SetInfoHold(bool hold)
        {
            _infoHold = hold;
        }

        /// <summary>Começo do arrasto da cortina (o dedo passa a mandar na posição).</summary>
        public void ShadeBegin()
        {
            _shadeDragging = true;
            _shadeVel = 0f;
        }

        /// <summary>Move a cortina: dy em unidades do canvas, positivo = dedo descendo (abre).</summary>
        public void ShadeDrag(float dy)
        {
            _shade = Mathf.Clamp01(_shade + dy / (ShadeHeight + 40f));
            ApplyShade();
        }

        /// <summary>Solta a cortina: abre ou fecha pela velocidade (unidades por segundo, positivo = descendo) ou pela posição.</summary>
        public void ShadeEnd(float velocity)
        {
            _shadeDragging = false;
            bool open = velocity > 500f || (velocity > -500f && _shade > 0.45f);
            _infoPinned = open;
            _infoHold = false;
            _shadeVel = velocity / (ShadeHeight + 40f);
        }

        private void TickShade(float dt)
        {
            if (_shadeRoot == null)
            {
                return;
            }

            bool keyboardTab = false;
            Keyboard keyboard = Keyboard.current;

            if (keyboard != null && _hudRoot.activeSelf)
            {
                keyboardTab = keyboard.tabKey.isPressed;
            }

            if (!_shadeDragging)
            {
                float goal = _infoPinned || _infoHold || keyboardTab ? 1f : 0f;
                const float k = 220f;
                const float c = 24f;
                _shadeVel += ((goal - _shade) * k - _shadeVel * c) * dt;
                _shade += _shadeVel * dt;

                if (_shade > 1.02f)
                {
                    _shade = 1.02f;
                    _shadeVel = Mathf.Min(_shadeVel, 0f);
                }

                if (_shade < 0f)
                {
                    _shade = 0f;
                    _shadeVel = Mathf.Max(_shadeVel, 0f);
                }

                if (Mathf.Abs(goal - _shade) < 0.001f && Mathf.Abs(_shadeVel) < 0.01f)
                {
                    _shade = goal;
                    _shadeVel = 0f;
                }
            }

            ApplyShade();

            if (_shade > 0.01f && Time.unscaledTime - _lastRefresh > 0.1f)
            {
                _lastRefresh = Time.unscaledTime;
                RefreshInfo();
            }
        }

        private void ApplyShade()
        {
            bool visible = _shade > 0.003f;

            if (_shadeRoot.gameObject.activeSelf != visible)
            {
                _shadeRoot.gameObject.SetActive(visible);
                _shadeCatcher.SetActive(visible);

                if (visible)
                {
                    RefreshInfo();
                }
            }

            float open = Mathf.Min(_shade, 1.04f);
            _shadeRoot.anchoredPosition = new Vector2(0f, Mathf.Lerp(ShadeHeight + 60f, -22f, open));
            _shadeDim.alpha = Mathf.Clamp01(_shade);
        }

        private void RefreshInfo()
        {
            InfoPainel i = _info;
            bool hasOrder = !string.IsNullOrEmpty(i.Etapa);
            _iEtapa.text = hasOrder ? (i.Etapa == "Retirar" ? "Retirar o pedido em" : "Entregar para") : "Sem pedido em andamento";
            _iProximo.text = string.IsNullOrEmpty(i.Proximo) ? string.Empty : "<color=#FFB021><b>PRÓXIMO PEDIDO</b></color>  " + i.Proximo;
            string name = i.Etapa == "Entregar" && !string.IsNullOrEmpty(i.Cliente) ? i.Cliente : i.Restaurante;
            string other = i.Etapa == "Retirar" && !string.IsNullOrEmpty(i.Cliente) ? "<color=#9AA4B2>depois para " + i.Cliente + "</color>" : string.Empty;
            _iRota.text = hasOrder ? name + (other.Length > 0 ? "\n<size=18><b>" + other + "</b></size>" : string.Empty) : (string.IsNullOrEmpty(i.Modo) ? "Modo livre" : i.Modo);
            _iEndereco.text = i.Endereco ?? string.Empty;
            _iDist.text = hasOrder ? FormatDistance(i.DistanciaM) + " restantes" : string.Empty;
            _iTempo.text = hasOrder ? FormatTime(i.DecorridoS) + " de " + FormatTime(i.EsperadoS) : "-";
            float ratio = i.EsperadoS > 0f ? i.DecorridoS / i.EsperadoS : 0f;
            UiKit.SetBar(_iTempoFill, hasOrder ? ratio : 0f);
            _iTempoBarra.color = ratio > 1f ? UiTema.Perigo : ratio > 0.8f ? UiTema.Aviso : UiTema.Sucesso;
            _iPaga.text = hasOrder ? i.Pagamento : "-";
            _iGorjeta.text = hasOrder ? i.Gorjeta : string.Empty;

            _iSaldo.text = i.Saldo;
            _iDia.text = i.Dia;
            _iEntregas.text = i.EntregasMeta > 0 ? i.EntregasFeitas + " de " + i.EntregasMeta + " na semana" : i.EntregasFeitas.ToString();
            UiKit.SetBar(_iEntregasFill, i.EntregasMeta > 0 ? (float)i.EntregasFeitas / i.EntregasMeta : 0f);

            for (int s = 0; s < 5; s++)
            {
                float fill = Mathf.Clamp01(i.Nota - s);
                _iStars[s].color = Color.Lerp(new Color(1f, 1f, 1f, 0.16f), UiTema.Aviso, fill > 0.25f ? 1f : 0f);
            }

            _iNota.text = i.Nota.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');

            _iMoto.text = i.MotoStatus;
            UiKit.SetBar(_iCombFill, Painel.Combustivel);
            _iCombCor.color = Painel.Combustivel <= PainelMotoEstado.LimiteReserva ? UiTema.Perigo : UiTema.Aviso;
            _iCombTexto.text = Mathf.RoundToInt(Painel.Combustivel * 100f) + "%";
            _iCnh.text = i.CnhStatus;
            _iMultas.text = string.IsNullOrEmpty(i.Multas) ? "Nenhuma" : i.Multas;
            _iMultas.color = string.IsNullOrEmpty(i.Multas) || i.Multas.EndsWith("0,00", StringComparison.Ordinal) ? UiTema.Texto : UiTema.Perigo;
            _iPontos.text = string.IsNullOrEmpty(i.PontosCnh) ? "0 pontos" : i.PontosCnh;
        }

        // ---------- Aviso rápido (banner de notificação) ----------

        private RectTransform _toastRoot;
        private TextMeshProUGUI _toastText;
        private float _toastUntil;
        private float _toastShownAt;

        public string ToastMessage => Time.unscaledTime < _toastUntil ? _toastText.text : string.Empty;

        private void BuildToast(Transform parent)
        {
            _toastRoot = UiKit.Panel(parent, "Aviso", 22, UiTema.VidroForte);
            UiKit.Place(_toastRoot, new Vector2(0.5f, 1f), new Vector2(0f, -172f), new Vector2(600f, 64f));

            Image bubble = UiKit.Circle(_toastRoot, "Icone", UiTema.Destaque);
            UiKit.Place(bubble.rectTransform, new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(34f, 34f));
            TextMeshProUGUI mark = UiKit.Text(bubble.transform, "Marca", "!", 22f, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
            UiKit.Stretch(mark.rectTransform);

            TextMeshProUGUI app = UiKit.Caps(_toastRoot, "App", UiTema.NomeApp, UiTema.TextoFraco, TextAlignmentOptions.Left, 11f);
            UiKit.Place(app.rectTransform, new Vector2(0f, 1f), new Vector2(64f, -10f), new Vector2(300f, 14f));
            _toastText = UiKit.Text(_toastRoot, "Texto", string.Empty, 20f, UiTema.Texto, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            UiKit.Place(_toastText.rectTransform, new Vector2(0f, 1f), new Vector2(64f, -28f), new Vector2(510f, 30f));
            _toastText.textWrappingMode = TextWrappingModes.Normal;
            _toastRoot.gameObject.SetActive(false);
        }

        public void Toast(string message, float seconds = 2.5f)
        {
            _toastText.text = message;
            _toastUntil = Time.unscaledTime + seconds;
            _toastShownAt = Time.unscaledTime;
            Vector2 pref = _toastText.GetPreferredValues(message, 510f, 10000f);
            float textHeight = Mathf.Max(26f, pref.y);
            _toastText.rectTransform.sizeDelta = new Vector2(510f, textHeight);
            _toastRoot.sizeDelta = new Vector2(600f, 40f + textHeight);
            _toastRoot.gameObject.SetActive(true);
            TickToast(0f);
        }

        /// <summary>Esconde o aviso na hora (antes de abrir um painel que ocuparia o mesmo lugar).</summary>
        public void ClearToast()
        {
            _toastUntil = 0f;

            if (_toastRoot != null)
            {
                _toastRoot.gameObject.SetActive(false);
            }
        }

        private void TickToast(float dt)
        {
            if (_toastRoot == null || !_toastRoot.gameObject.activeSelf)
            {
                return;
            }

            float now = Time.unscaledTime;

            if (now >= _toastUntil)
            {
                _toastRoot.gameObject.SetActive(false);

                return;
            }

            float slideIn = Ease((now - _toastShownAt) / 0.3f);
            float slideOut = Ease((_toastUntil - now) / 0.25f);
            float shown = Mathf.Min(slideIn, slideOut);
            float height = _toastRoot.sizeDelta.y;
            _toastRoot.anchoredPosition = new Vector2(0f, Mathf.Lerp(height + 20f, -172f, shown));
        }
    }
}
