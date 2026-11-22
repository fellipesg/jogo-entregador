using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Entregador.Game
{
    public sealed partial class GameUI
    {
        private const float PhoneWidth = 600f;
        private const string HexDim = "#9AA4B2";
        private const string HexGreen = "#38D17F";
        private const string HexRed = "#FF4D52";
        private const string HexText = "#F2F5F8";

        private static readonly Regex MoneyRegex = new Regex(@"-?R\$\s?\d[\d\.]*(?:,\d{2})?", RegexOptions.Compiled);

        private static readonly string[] RedWords =
        {
            "multa", "custo", "parcela", "aluguel", "guincho", "diária", "estorn", "perde", "desconto", "taxa", "prejuízo",
        };

        private GameObject _modalRoot;
        private Image _modalDim;
        private CanvasGroup _modalGroup;
        private RectTransform _phone;
        private TextMeshProUGUI _modalTitleText;
        private RectTransform _bodyCard;
        private LayoutElement _bodyLayout;
        private TextMeshProUGUI _bodyText;
        private RectTransform _bodyContent;
        private ScrollRect _bodyScroll;
        private RectTransform _offerCard;
        private RectTransform _buttonsRoot;
        private readonly List<Button> _modalButtons = new List<Button>();
        private readonly List<TMP_Text> _modalButtonLabels = new List<TMP_Text>();
        private readonly List<string> _currentLabels = new List<string>();
        private readonly List<PanelButton> _virtualButtons = new List<PanelButton>();

        private string _titleRaw = string.Empty;
        private string _bodyRaw = string.Empty;
        private float _modalShownAt;
        private bool _modalSlide;
        private bool _offerMode;

        // Oferta.
        private TextMeshProUGUI _offerRest;
        private TextMeshProUGUI _offerCliente;
        private TextMeshProUGUI _offerDist;
        private TextMeshProUGUI _offerGanho;
        private TextMeshProUGUI _offerExtra;
        private UiArc _offerRing;
        private TextMeshProUGUI _offerSeconds;
        private RectTransform _offerTimerRoot;
        private float _offerRemaining;
        private float _offerTotal = 15f;

        public string ModalTitle => _titleRaw;

        public string ModalBody => _bodyRaw;

        public bool ModalVisible => _modalRoot.activeSelf || ScreenVisible;

        public IReadOnlyList<string> ModalButtonLabels => _currentLabels;

        // ---------- Montagem ----------

        private void BuildModal(Transform parent)
        {
            _modalRoot = UiKit.Rect("AppFundo", parent).gameObject;
            UiKit.Stretch(_modalRoot.GetComponent<RectTransform>());
            _modalDim = _modalRoot.AddComponent<Image>();
            _modalDim.color = new Color(0f, 0f, 0f, 0.5f);
            _modalDim.raycastTarget = true;
            _modalGroup = _modalRoot.AddComponent<CanvasGroup>();

            _phone = UiKit.Panel(_modalRoot.transform, "Telefone", UiTema.RaioTelefone, new Color(0.07f, 0.08f, 0.10f, 0.97f));
            UiKit.Place(_phone, new Vector2(1f, 0.5f), new Vector2(-64f, 0f), new Vector2(PhoneWidth, 200f));
            _phone.Find("Borda").GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.2f);
            var layout = _phone.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 10, 12);
            layout.spacing = 12f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            _phone.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Alto-falante do celular.
            RectTransform top = UiKit.Rect("Topo", _phone);
            top.gameObject.AddComponent<LayoutElement>().preferredHeight = 12f;
            Image speaker = UiKit.Img(top, "Alto", UiSprites.Fill(3), new Color(1f, 1f, 1f, 0.16f), true);
            UiKit.Place(speaker.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(64f, 6f));

            // Cabeçalho do app, na cor do app.
            RectTransform header = UiKit.Panel(_phone, "Cabecalho", UiTema.RaioCartao + 6, UiTema.Destaque, false, false);
            var headerLayout = header.gameObject.AddComponent<VerticalLayoutGroup>();
            headerLayout.padding = new RectOffset(22, 22, 16, 18);
            headerLayout.spacing = 8f;
            headerLayout.childControlHeight = true;
            headerLayout.childControlWidth = true;
            headerLayout.childForceExpandHeight = false;
            headerLayout.childForceExpandWidth = true;

            RectTransform line = UiKit.Rect("Linha", header);
            line.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;
            Image logo = UiKit.Circle(line, "Logo", Color.white);
            UiKit.Place(logo.rectTransform, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(26f, 26f));
            TextMeshProUGUI logoText = UiKit.Text(logo.transform, "E", "E", 16f, UiTema.Destaque, FontStyles.Bold, TextAlignmentOptions.Center);
            UiKit.Stretch(logoText.rectTransform);
            TextMeshProUGUI appName = UiKit.Caps(line, "App", UiTema.NomeApp, new Color(1f, 1f, 1f, 0.9f), TextAlignmentOptions.Left, 13f);
            UiKit.Place(appName.rectTransform, new Vector2(0f, 0.5f), new Vector2(36f, 0f), new Vector2(300f, 20f));

            _modalTitleText = UiKit.Text(header, "Titulo", string.Empty, UiTema.TamTitulo, Color.white, FontStyles.Bold, TextAlignmentOptions.TopLeft);
            _modalTitleText.textWrappingMode = TextWrappingModes.Normal;

            // Anel do tempo de resposta (só nas ofertas), à direita do cabeçalho.
            _offerTimerRoot = UiKit.Rect("Tempo", header);
            _offerTimerRoot.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            UiKit.Place(_offerTimerRoot, new Vector2(1f, 0.5f), new Vector2(-20f, 0f), new Vector2(58f, 58f));
            Image ringBack = UiKit.Img(_offerTimerRoot, "Trilho", UiSprites.Ring(), new Color(1f, 1f, 1f, 0.3f), false);
            UiKit.Stretch(ringBack.rectTransform);
            _offerRing = UiKit.Gfx<UiArc>(_offerTimerRoot, "Arco");
            UiKit.Stretch(_offerRing.rectTransform);
            _offerRing.SetRadii(24f, 29f);
            _offerRing.color = Color.white;
            _offerSeconds = UiKit.Text(_offerTimerRoot, "Segundos", "15", 24f, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
            UiKit.Stretch(_offerSeconds.rectTransform);
            _offerTimerRoot.gameObject.SetActive(false);

            BuildBodyCard();
            BuildOfferCard();

            _buttonsRoot = UiKit.Rect("Botoes", _phone);
            var buttonLayout = _buttonsRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            buttonLayout.spacing = 10f;
            buttonLayout.padding = new RectOffset(0, 0, 4, 0);
            buttonLayout.childControlHeight = true;
            buttonLayout.childControlWidth = true;
            buttonLayout.childForceExpandHeight = false;
            buttonLayout.childForceExpandWidth = true;

            for (int i = 0; i < 6; i++)
            {
                Button button = UiKit.Pill(_buttonsRoot, "Botao" + i, "-", UiBotaoEstilo.Primario, 56f, 20f);
                button.gameObject.AddComponent<LayoutElement>().preferredHeight = 56f;
                _modalButtons.Add(button);
                _modalButtonLabels.Add(UiKit.Label(button));
            }

            RectTransform home = UiKit.Rect("Indicador", _phone);
            home.gameObject.AddComponent<LayoutElement>().preferredHeight = 14f;
            Image bar = UiKit.Img(home, "Barra", UiSprites.Fill(3), new Color(1f, 1f, 1f, 0.28f), true);
            UiKit.Place(bar.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -2f), new Vector2(120f, 5f));
        }

        private void BuildBodyCard()
        {
            _bodyCard = UiKit.Panel(_phone, "Corpo", UiTema.RaioCartao, UiTema.Superficie, false, true);
            _bodyLayout = _bodyCard.gameObject.AddComponent<LayoutElement>();
            _bodyLayout.preferredHeight = 120f;

            RectTransform viewport = UiKit.Rect("Janela", _bodyCard);
            UiKit.Stretch(viewport, 0f);
            viewport.offsetMin = new Vector2(18f, 12f);
            viewport.offsetMax = new Vector2(-18f, -12f);
            viewport.gameObject.AddComponent<RectMask2D>();

            _bodyText = UiKit.Text(viewport, "Texto", string.Empty, UiTema.TamCorpo, UiTema.Texto, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            _bodyContent = _bodyText.rectTransform;
            _bodyContent.anchorMin = new Vector2(0f, 1f);
            _bodyContent.anchorMax = new Vector2(1f, 1f);
            _bodyContent.pivot = new Vector2(0.5f, 1f);
            _bodyContent.offsetMin = new Vector2(0f, -100f);
            _bodyContent.offsetMax = Vector2.zero;
            _bodyText.textWrappingMode = TextWrappingModes.Normal;
            _bodyText.lineSpacing = 12f;
            _bodyText.paragraphSpacing = 4f;

            _bodyScroll = _bodyCard.gameObject.AddComponent<ScrollRect>();
            _bodyScroll.viewport = viewport;
            _bodyScroll.content = _bodyContent;
            _bodyScroll.horizontal = false;
            _bodyScroll.vertical = true;
            _bodyScroll.movementType = ScrollRect.MovementType.Clamped;
            _bodyScroll.scrollSensitivity = 30f;
        }

        private void BuildOfferCard()
        {
            _offerCard = UiKit.Panel(_phone, "Oferta", UiTema.RaioCartao, UiTema.Superficie, false, true);
            var layout = _offerCard.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 18, 18);
            layout.spacing = 16f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            RectTransform route = UiKit.Rect("Rota", _offerCard);
            route.gameObject.AddComponent<LayoutElement>().preferredHeight = 126f;
            Image dot1 = UiKit.Circle(route, "Ponto1", UiTema.Sucesso);
            UiKit.Place(dot1.rectTransform, new Vector2(0f, 1f), new Vector2(2f, -6f), new Vector2(16f, 16f));
            Image line = UiKit.Img(route, "Ligacao", UiSprites.Fill(1), new Color(1f, 1f, 1f, 0.22f), false);
            UiKit.Place(line.rectTransform, new Vector2(0f, 1f), new Vector2(9f, -26f), new Vector2(2f, 48f));
            Image dot2 = UiKit.Circle(route, "Ponto2", UiTema.Destaque);
            UiKit.Place(dot2.rectTransform, new Vector2(0f, 1f), new Vector2(2f, -78f), new Vector2(16f, 16f));

            TextMeshProUGUI c1 = UiKit.Caps(route, "Rot1", "Retirar em", UiTema.TextoFraco, TextAlignmentOptions.Left);
            UiKit.Place(c1.rectTransform, new Vector2(0f, 1f), new Vector2(36f, -2f), new Vector2(480f, 16f));
            _offerRest = UiKit.Text(route, "Restaurante", string.Empty, 24f, UiTema.Texto, FontStyles.Bold, TextAlignmentOptions.Left);
            UiKit.Place(_offerRest.rectTransform, new Vector2(0f, 1f), new Vector2(36f, -20f), new Vector2(490f, 32f));
            _offerRest.overflowMode = TextOverflowModes.Truncate;
            TextMeshProUGUI c2 = UiKit.Caps(route, "Rot2", "Entregar para", UiTema.TextoFraco, TextAlignmentOptions.Left);
            UiKit.Place(c2.rectTransform, new Vector2(0f, 1f), new Vector2(36f, -72f), new Vector2(480f, 16f));
            _offerCliente = UiKit.Text(route, "Cliente", string.Empty, 24f, UiTema.Texto, FontStyles.Bold, TextAlignmentOptions.Left);
            UiKit.Place(_offerCliente.rectTransform, new Vector2(0f, 1f), new Vector2(36f, -90f), new Vector2(490f, 32f));
            _offerCliente.overflowMode = TextOverflowModes.Truncate;

            RectTransform stats = UiKit.Rect("Numeros", _offerCard);
            stats.gameObject.AddComponent<LayoutElement>().preferredHeight = 82f;
            var row = stats.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 10f;
            row.childControlHeight = true;
            row.childControlWidth = true;
            row.childForceExpandHeight = true;
            row.childForceExpandWidth = true;
            _offerDist = Tile(stats, "Distância", UiTema.Texto);
            _offerGanho = Tile(stats, "Ganho", UiTema.Sucesso);
            _offerExtra = Tile(stats, "Adicional", UiTema.TextoSuave);
            _offerCard.gameObject.SetActive(false);
        }

        private static TextMeshProUGUI Tile(Transform parent, string label, Color valueColor)
        {
            RectTransform tile = UiKit.Panel(parent, "Tile", 14, new Color(1f, 1f, 1f, 0.06f), false, false);
            TextMeshProUGUI caps = UiKit.Caps(tile, "Rotulo", label, UiTema.TextoFraco, TextAlignmentOptions.Left, 12f);
            UiKit.Place(caps.rectTransform, new Vector2(0f, 1f), new Vector2(14f, -12f), new Vector2(140f, 16f));
            TextMeshProUGUI value = UiKit.Text(tile, "Valor", string.Empty, 24f, valueColor, FontStyles.Bold, TextAlignmentOptions.Left);
            UiKit.Place(value.rectTransform, new Vector2(0f, 1f), new Vector2(14f, -34f), new Vector2(160f, 32f));
            value.overflowMode = TextOverflowModes.Truncate;

            return value;
        }

        // ---------- API ----------

        public void ShowModal(string title, string body, params PanelButton[] buttons)
        {
            OpenModal(title, body, buttons, false);
        }

        private void OpenModal(string title, string body, PanelButton[] buttons, bool offer)
        {
            HideScreens();
            bool wasVisible = _modalRoot.activeSelf;

            if (!wasVisible)
            {
                _modalShownAt = Time.unscaledTime;
                _modalSlide = true;
            }
            else
            {
                _modalShownAt = Time.unscaledTime - 0.16f;
                _modalSlide = false;
            }

            _modalRoot.SetActive(true);
            _offerMode = offer;
            _titleRaw = title;
            _bodyRaw = body ?? string.Empty;
            _modalTitleText.text = title;
            _offerTimerRoot.gameObject.SetActive(offer);
            _offerCard.gameObject.SetActive(offer);
            _modalDim.color = new Color(0f, 0f, 0f, offer ? 0.18f : 0.5f);
            _currentLabels.Clear();
            _virtualButtons.Clear();

            for (int i = 0; i < _modalButtons.Count; i++)
            {
                bool used = i < buttons.Length;
                Button button = _modalButtons[i];
                button.gameObject.SetActive(used);
                button.onClick.RemoveAllListeners();
                button.interactable = true;

                if (used)
                {
                    Action action = buttons[i].OnClick;
                    _modalButtonLabels[i].text = buttons[i].Label;
                    button.onClick.AddListener(() => action?.Invoke());
                    _currentLabels.Add(buttons[i].Label);
                    StyleButton(button, i == 0 ? UiBotaoEstilo.Primario : UiBotaoEstilo.Secundario);
                }
            }

            ApplyBody();
        }

        private static void StyleButton(Button button, UiBotaoEstilo style)
        {
            var fx = button.GetComponent<UiButtonFx>();
            Transform outline = button.transform.Find("Contorno");
            bool primary = style != UiBotaoEstilo.Secundario;
            Color accent = UiTema.Destaque;
            Image fill = button.targetGraphic as Image;

            if (outline == null && !primary)
            {
                Image o = UiKit.Img(button.transform, "Contorno", UiSprites.Outline(26), new Color(1f, 1f, 1f, 0.28f), true);
                UiKit.Stretch(o.rectTransform);
                o.transform.SetSiblingIndex(1);
                outline = o.transform;
            }

            if (outline != null)
            {
                outline.gameObject.SetActive(!primary);
            }

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

            UiKit.Label(button).color = primary ? Color.white : UiTema.Texto;
            fill.color = fx.Normal;
        }

        /// <summary>Troca só o texto do corpo (contagem regressiva, por exemplo).</summary>
        public void SetModalBody(string body)
        {
            _bodyRaw = body ?? string.Empty;
            ApplyBody();
        }

        public void SetModalButtonInteractable(int index, bool interactable)
        {
            _modalButtons[index].interactable = interactable;
        }

        public void HideModal()
        {
            _modalRoot.SetActive(false);
            _offerMode = false;
            _currentLabels.Clear();
            _virtualButtons.Clear();
            HideScreens();
        }

        /// <summary>Clica no botão pelo texto. Devolve false se não existe (testes e automação).</summary>
        public bool ClickModalButton(string label)
        {
            for (int i = 0; i < _modalButtons.Count; i++)
            {
                if (_modalRoot.activeSelf && _modalButtons[i].gameObject.activeSelf && _modalButtons[i].interactable && _modalButtonLabels[i].text.Contains(label))
                {
                    _modalButtons[i].onClick.Invoke();

                    return true;
                }
            }

            foreach (PanelButton virtualButton in _virtualButtons)
            {
                if (virtualButton.Label.Contains(label))
                {
                    virtualButton.OnClick?.Invoke();

                    return true;
                }
            }

            return false;
        }

        /// <summary>Notificação de pedido no estilo do app: restaurante, cliente, distância, ganho e anel de tempo para aceitar.</summary>
        public void ShowOffer(string restaurant, string customer, string distance, string pay, bool rain, float remaining, float total, string rawBody, params PanelButton[] buttons)
        {
            _offerRest.text = restaurant;
            _offerCliente.text = customer;
            _offerDist.text = distance;
            _offerGanho.text = pay;
            _offerExtra.text = rain ? "Chuva +30%" : "Nenhum";
            _offerExtra.color = rain ? UiTema.Aviso : UiTema.TextoFraco;
            _offerTotal = Mathf.Max(1f, total);
            _offerRemaining = remaining;
            OpenModal("Novo pedido", rawBody, buttons, true);
            TickOffer(Time.unscaledTime);
        }

        /// <summary>Atualiza o anel de tempo da oferta (segundos que faltam e o total).</summary>
        public void SetOfferTimer(float remaining, float total)
        {
            _offerRemaining = remaining;
            _offerTotal = Mathf.Max(1f, total);
        }

        private void TickOffer(float time)
        {
            if (!_offerMode || _offerRing == null)
            {
                return;
            }

            float frac = Mathf.Clamp01(_offerRemaining / _offerTotal);
            _offerRing.Set(90f, 90f - 360f * frac);
            _offerSeconds.text = Mathf.CeilToInt(Mathf.Max(0f, _offerRemaining)).ToString();
            bool low = _offerRemaining <= 5f;
            _offerSeconds.color = low ? Color.Lerp(Color.white, new Color(1f, 0.85f, 0.2f), 0.5f + 0.5f * Mathf.Sin(time * 10f)) : Color.white;
        }

        // ---------- Texto do corpo ----------

        private void ApplyBody()
        {
            bool show = !_offerMode && !string.IsNullOrEmpty(_bodyRaw);
            _bodyCard.gameObject.SetActive(show);

            if (!show)
            {
                return;
            }

            string rich = FormatBody(_bodyRaw);
            _bodyText.text = rich;
            float width = PhoneWidth - 32f - 36f;
            float height = _bodyText.GetPreferredValues(rich, width, 10000f).y + 4f;
            int buttonCount = _currentLabels.Count;
            float canvasHeight = _canvas != null ? ((RectTransform)_canvas.transform).rect.height : 1080f;

            canvasHeight = Mathf.Clamp(canvasHeight, 900f, 1500f);

            float chrome = 12f + 120f + buttonCount * 66f + 60f + 100f;
            float maxHeight = Mathf.Max(160f, canvasHeight - chrome);
            _bodyContent.sizeDelta = new Vector2(0f, height);
            _bodyContent.anchoredPosition = Vector2.zero;
            _bodyLayout.preferredHeight = Mathf.Min(height + 28f, maxHeight);
        }

        /// <summary>Formata o texto cru para o app: chave suave e valor em destaque, dinheiro em verde e despesas em vermelho.</summary>
        public static string FormatBody(string raw)
        {
            var sb = new StringBuilder();
            string[] lines = raw.Replace("\r", string.Empty).Split('\n');

            for (int n = 0; n < lines.Length; n++)
            {
                string line = lines[n].Replace("<", "‹");
                string lower = line.ToLowerInvariant();
                bool red = false;

                foreach (string word in RedWords)
                {
                    red |= lower.Contains(word);
                }

                int colon = line.IndexOf(": ", StringComparison.Ordinal);
                bool pair = colon > 1 && colon <= 26 && !line.Substring(0, colon).Contains(".");

                if (pair)
                {
                    string key = line.Substring(0, colon);
                    string value = line.Substring(colon + 2);
                    bool shortValue = value.Length <= 26;
                    sb.Append("<color=").Append(HexDim).Append('>').Append(key).Append("</color>");
                    sb.Append(shortValue ? "<pos=52%>" : "  ");
                    sb.Append("<b>").Append(Money(value, red)).Append("</b>");
                }
                else
                {
                    sb.Append(Money(line, red));
                }

                if (n < lines.Length - 1)
                {
                    sb.Append('\n');
                }
            }

            return sb.ToString();
        }

        private static string Money(string text, bool red)
        {
            return MoneyRegex.Replace(text, m =>
            {
                bool negative = m.Value.StartsWith("-", StringComparison.Ordinal);

                return "<color=" + (red || negative ? HexRed : HexGreen) + "><b>" + m.Value + "</b></color>";
            });
        }

        // ---------- Animação ----------

        private void TickModal(float dt, float time)
        {
            if (_modalRoot == null || !_modalRoot.activeSelf)
            {
                return;
            }

            float age = time - _modalShownAt;
            _modalGroup.alpha = Ease(age / 0.22f);
            float e = Ease(age / 0.38f);
            _phone.anchoredPosition = new Vector2(-64f + (_modalSlide ? (1f - e) * 760f : 0f), 0f);
        }
    }
}
