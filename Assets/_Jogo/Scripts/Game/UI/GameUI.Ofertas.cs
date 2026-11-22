using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Entregador.Game
{
    /// <summary>
    /// Interface do ritmo dos pedidos: a pílula "Procurando pedidos…", as zonas quentes no minimapa, o cartão de oferta compacto (no
    /// meio da corrida: não pausa, não bloqueia os controles, entra deslizando pela direita) e a pílula "Começar turno" da exploração livre.
    /// </summary>
    public sealed partial class GameUI
    {
        private const float CompactW = 400f;
        private const float CompactH = 262f;
        private const float MinimapMetersAcross = 240f;
        private const int MaxZonas = 3;

        private RectTransform _minimapMask;

        // Procurando pedidos.
        private RectTransform _procurandoRoot;
        private Image _procurandoDot;
        private TextMeshProUGUI _procurandoTexto;
        private TextMeshProUGUI _procurandoHora;
        private bool _procurandoOn;
        private bool _procurandoZona;
        private string _procurandoHoraTexto = string.Empty;

        // Zonas quentes no minimapa.
        private readonly Image[] _zonaFill = new Image[MaxZonas];
        private readonly Image[] _zonaRing = new Image[MaxZonas];
        private int _zonasVisiveis;

        // Cartão compacto.
        private RectTransform _compactRoot;
        private TextMeshProUGUI _compactTag;
        private TextMeshProUGUI _compactRest;
        private TextMeshProUGUI _compactCliente;
        private TextMeshProUGUI _compactDist;
        private TextMeshProUGUI _compactPay;
        private TextMeshProUGUI _compactExtra;
        private UiArc _compactRing;
        private TextMeshProUGUI _compactSeconds;
        private Button _compactAccept;
        private Button _compactReject;
        private Action _compactOnAccept;
        private Action _compactOnReject;
        private float _compactShownAt;
        private float _compactRemaining;
        private float _compactTotal = 12f;

        // Começar turno.
        private Button _comecarPill;
        private Button _comecarDismiss;
        private Action _comecarClick;
        private Action _comecarDismissClick;
        private bool _comecarOn;

        public bool ProcurandoVisivel => _procurandoOn;

        public string ProcurandoTexto => _procurandoTexto != null ? _procurandoTexto.text : string.Empty;

        public bool ProcurandoZonaQuente => _procurandoZona;

        /// <summary>Quantas zonas quentes estão desenhadas no minimapa.</summary>
        public int ZonasNoMinimapa => _zonasVisiveis;

        public bool OfferCompactVisible => _compactRoot != null && _compactRoot.gameObject.activeSelf;

        public string OfferCompactRestaurant => _compactRest != null ? _compactRest.text : string.Empty;

        public string OfferCompactTag => _compactTag != null ? _compactTag.text : string.Empty;

        public RectTransform OfferCompactRect => _compactRoot;

        public bool ComecarPillVisible => _comecarPill != null && _comecarPill.gameObject.activeSelf;

        // ---------- Montagem ----------

        private void BuildOfertasHud(Transform hud)
        {
            BuildProcurando(hud);
            BuildZonas();
            BuildOfertaCompacta(hud);
            BuildComecarPill(hud);
        }

        private void BuildProcurando(Transform hud)
        {
            _procurandoRoot = UiKit.Panel(hud, "Procurando", UiTema.RaioPilula, UiTema.Vidro);
            UiKit.Place(_procurandoRoot, new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(340f, 48f));
            _procurandoDot = UiKit.Circle(_procurandoRoot, "Ponto", UiTema.Sucesso);
            UiKit.Place(_procurandoDot.rectTransform, new Vector2(0f, 0.5f), new Vector2(20f, 0f), new Vector2(14f, 14f));
            _procurandoTexto = UiKit.Text(_procurandoRoot, "Texto", "Procurando pedidos…", 19f, UiTema.Texto, FontStyles.Bold, TextAlignmentOptions.Left);
            UiKit.Place(_procurandoTexto.rectTransform, new Vector2(0f, 0.5f), new Vector2(44f, 0f), new Vector2(220f, 28f));
            _procurandoHora = UiKit.Text(_procurandoRoot, "Hora", "08:00", 16f, UiTema.TextoSuave, FontStyles.Bold, TextAlignmentOptions.Right);
            UiKit.Place(_procurandoHora.rectTransform, new Vector2(1f, 0.5f), new Vector2(-20f, 0f), new Vector2(70f, 24f));
            _procurandoRoot.gameObject.SetActive(false);
        }

        /// <summary>Círculos de zona quente sobre o mapa, dentro da máscara do minimapa (atrás da moto).</summary>
        private void BuildZonas()
        {
            for (int i = 0; i < MaxZonas; i++)
            {
                _zonaFill[i] = UiKit.Circle(_minimapMask, "ZonaQuente" + i, UiTema.Alfa(UiTema.Aviso, 0.26f));
                _zonaRing[i] = UiKit.Img(_minimapMask, "ZonaQuenteBorda" + i, UiSprites.Ring(), UiTema.Alfa(UiTema.Aviso, 0.85f), false);
                _zonaFill[i].transform.SetSiblingIndex(1 + i * 2);
                _zonaRing[i].transform.SetSiblingIndex(2 + i * 2);
                _zonaFill[i].gameObject.SetActive(false);
                _zonaRing[i].gameObject.SetActive(false);
            }
        }

        private void BuildOfertaCompacta(Transform hud)
        {
            _compactRoot = UiKit.Panel(hud, "OfertaCompacta", UiTema.RaioCartao + 6, UiTema.VidroForte);
            UiKit.Place(_compactRoot, new Vector2(1f, 1f), new Vector2(-24f, -332f), new Vector2(CompactW, CompactH));

            _compactTag = UiKit.Caps(_compactRoot, "Etiqueta", "Próximo pedido", UiTema.Destaque, TextAlignmentOptions.Left, 13f);
            UiKit.Place(_compactTag.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -18f), new Vector2(260f, 18f));

            RectTransform timer = UiKit.Rect("Tempo", _compactRoot);
            UiKit.Place(timer, new Vector2(1f, 1f), new Vector2(-16f, -12f), new Vector2(48f, 48f));
            _compactRing = UiKit.Gfx<UiArc>(timer, "Arco");
            UiKit.Stretch(_compactRing.rectTransform);
            _compactRing.SetRadii(19f, 23f);
            _compactRing.color = Color.white;
            _compactSeconds = UiKit.Text(timer, "Segundos", "12", 19f, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
            UiKit.Stretch(_compactSeconds.rectTransform);

            _compactRest = UiKit.Text(_compactRoot, "Restaurante", string.Empty, 22f, UiTema.Texto, FontStyles.Bold, TextAlignmentOptions.Left);
            UiKit.Place(_compactRest.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -48f), new Vector2(300f, 28f));
            _compactRest.overflowMode = TextOverflowModes.Truncate;
            _compactCliente = UiKit.Text(_compactRoot, "Cliente", string.Empty, 16f, UiTema.TextoSuave, FontStyles.Normal, TextAlignmentOptions.Left);
            UiKit.Place(_compactCliente.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -78f), new Vector2(360f, 22f));
            _compactCliente.overflowMode = TextOverflowModes.Truncate;

            _compactPay = UiKit.Text(_compactRoot, "Ganho", string.Empty, 30f, UiTema.Sucesso, FontStyles.Bold, TextAlignmentOptions.Left);
            UiKit.Place(_compactPay.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -110f), new Vector2(210f, 38f));
            _compactDist = UiKit.Text(_compactRoot, "Distancia", string.Empty, 18f, UiTema.Texto, FontStyles.Bold, TextAlignmentOptions.Right);
            UiKit.Place(_compactDist.rectTransform, new Vector2(1f, 1f), new Vector2(-20f, -118f), new Vector2(150f, 24f));
            _compactExtra = UiKit.Text(_compactRoot, "Extra", string.Empty, 14f, UiTema.Aviso, FontStyles.Bold, TextAlignmentOptions.Right);
            UiKit.Place(_compactExtra.rectTransform, new Vector2(1f, 1f), new Vector2(-20f, -142f), new Vector2(180f, 20f));

            _compactReject = UiKit.Pill(_compactRoot, "Recusar", "Recusar", UiBotaoEstilo.Secundario, 58f, 19f);
            UiKit.Place((RectTransform)_compactReject.transform, new Vector2(0f, 0f), new Vector2(16f, 36f), new Vector2(172f, 58f));
            _compactAccept = UiKit.Pill(_compactRoot, "Aceitar", "Aceitar", UiBotaoEstilo.Sucesso, 58f, 19f);
            UiKit.Place((RectTransform)_compactAccept.transform, new Vector2(1f, 0f), new Vector2(-16f, 36f), new Vector2(188f, 58f));
            _compactReject.onClick.AddListener(() => _compactOnReject?.Invoke());
            _compactAccept.onClick.AddListener(() => _compactOnAccept?.Invoke());

            TextMeshProUGUI hint = UiKit.Text(_compactRoot, "Atalhos", "N recusar    Y aceitar", 12f, UiTema.TextoFraco, FontStyles.Normal, TextAlignmentOptions.Center);
            UiKit.Place(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(CompactW - 32f, 18f));
            _compactRoot.gameObject.SetActive(false);
        }

        private void BuildComecarPill(Transform hud)
        {
            _comecarPill = UiKit.Pill(hud, "ComecarTurno", "Começar turno", UiBotaoEstilo.Primario, 60f, 18f);
            UiKit.Place((RectTransform)_comecarPill.transform, new Vector2(0f, 1f), new Vector2(96f, -24f), new Vector2(210f, 60f));
            _comecarPill.onClick.AddListener(() => _comecarClick?.Invoke());

            _comecarDismiss = UiKit.Pill(hud, "ComecarTurnoFechar", "×", UiBotaoEstilo.Secundario, 44f, 22f);
            UiKit.Place((RectTransform)_comecarDismiss.transform, new Vector2(0f, 1f), new Vector2(96f + 210f + 8f, -32f), new Vector2(44f, 44f));
            _comecarDismiss.onClick.AddListener(() => _comecarDismissClick?.Invoke());
            Glass(_comecarDismiss);
            _comecarPill.gameObject.SetActive(false);
            _comecarDismiss.gameObject.SetActive(false);
        }

        // ---------- API ----------

        /// <summary>Pílula "Procurando pedidos…" no topo: ponto que pulsa, hora do jogo e, em zona quente, a cor laranja.</summary>
        public void SetProcurando(bool visible, string hora, bool zonaQuente)
        {
            _procurandoOn = visible;
            _procurandoZona = zonaQuente;
            _procurandoHoraTexto = hora ?? string.Empty;

            if (_procurandoRoot.gameObject.activeSelf != visible)
            {
                _procurandoRoot.gameObject.SetActive(visible);
            }

            if (!visible)
            {
                return;
            }

            _procurandoTexto.text = zonaQuente ? "Zona quente" : "Procurando pedidos…";
            _procurandoHora.text = _procurandoHoraTexto;
        }

        /// <summary>Desenha as zonas quentes no minimapa (centros e raios em metros do mundo; o mapa gira com a moto). Null esconde.</summary>
        public void SetZonasMinimapa(Vector2[] centers, float[] radii, Vector2 bike, float yawDegrees)
        {
            int count = centers == null ? 0 : Mathf.Min(MaxZonas, centers.Length);
            _zonasVisiveis = 0;
            float scale = (((RectTransform)_minimapMask).rect.width > 1f ? ((RectTransform)_minimapMask).rect.width : 226f) / MinimapMetersAcross;
            float yaw = yawDegrees * Mathf.Deg2Rad;
            var forward = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw));
            var right = new Vector2(Mathf.Cos(yaw), -Mathf.Sin(yaw));

            for (int i = 0; i < MaxZonas; i++)
            {
                bool on = i < count;
                _zonaFill[i].gameObject.SetActive(on);
                _zonaRing[i].gameObject.SetActive(on);

                if (!on)
                {
                    continue;
                }

                _zonasVisiveis++;
                Vector2 d = centers[i] - bike;
                var position = new Vector2(Vector2.Dot(d, right), Vector2.Dot(d, forward)) * scale;
                float size = radii[i] * 2f * scale;

                foreach (Image image in new[] { _zonaFill[i], _zonaRing[i] })
                {
                    image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                    image.rectTransform.anchoredPosition = position;
                    image.rectTransform.sizeDelta = new Vector2(size, size);
                }
            }
        }

        /// <summary>
        /// Cartão de oferta compacto, não modal: entra deslizando pela direita, abaixo do minimapa, sem pausar nem cobrir os controles.
        /// Anel de tempo, Aceitar e Recusar (botões de 58 px) e atalhos Y/N tratados pelo GameFlow.
        /// </summary>
        public void ShowOfferCompact(string tag, string restaurant, string customer, string distance, string pay, bool rain, float remaining, float total, bool duplo,
            Action onAccept, Action onReject)
        {
            _compactTag.text = tag;
            _compactTag.color = duplo ? UiTema.Aviso : UiTema.Destaque;
            _compactRest.text = restaurant;
            _compactCliente.text = "para " + customer;
            _compactPay.text = pay;
            _compactDist.text = distance;
            _compactExtra.text = rain ? "Chuva +30%" : (duplo ? "Mesmo restaurante" : string.Empty);
            _compactOnAccept = onAccept;
            _compactOnReject = onReject;
            _compactTotal = Mathf.Max(1f, total);
            _compactRemaining = remaining;
            _compactShownAt = Time.unscaledTime;
            _compactRoot.gameObject.SetActive(true);
            TickOfertasUi(Time.unscaledTime);
        }

        public void SetOfferCompactTimer(float remaining, float total)
        {
            _compactRemaining = remaining;
            _compactTotal = Mathf.Max(1f, total);
        }

        public void HideOfferCompact()
        {
            if (_compactRoot != null)
            {
                _compactRoot.gameObject.SetActive(false);
            }
        }

        /// <summary>Clica em Aceitar ou Recusar do cartão compacto (testes).</summary>
        public bool ClickOfferCompact(bool accept)
        {
            if (!OfferCompactVisible)
            {
                return false;
            }

            (accept ? _compactAccept : _compactReject).onClick.Invoke();

            return true;
        }

        /// <summary>Mostra a pílula "Começar turno" (exploração livre) ao lado do ícone do celular, com um × para dispensar.</summary>
        public void SetComecarPill(bool visible, Action onClick, Action onDismiss)
        {
            _comecarClick = onClick;
            _comecarDismissClick = onDismiss;

            if (_comecarOn == visible)
            {
                return;
            }

            _comecarOn = visible;
            _comecarPill.gameObject.SetActive(visible);
            _comecarDismiss.gameObject.SetActive(visible);
        }

        public bool ClickComecarPill()
        {
            if (!ComecarPillVisible)
            {
                return false;
            }

            _comecarPill.onClick.Invoke();

            return true;
        }

        public bool ClickComecarDismiss()
        {
            if (!ComecarPillVisible)
            {
                return false;
            }

            _comecarDismiss.onClick.Invoke();

            return true;
        }

        // ---------- Animação ----------

        private void TickOfertasUi(float time)
        {
            if (_procurandoOn && _procurandoDot != null)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(time * 3.2f);
                Color baseColor = _procurandoZona ? UiTema.Aviso : UiTema.Sucesso;
                _procurandoDot.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0.35f + 0.65f * pulse);
                _procurandoDot.rectTransform.localScale = Vector3.one * (0.85f + 0.3f * pulse);
            }

            if (_compactRoot != null && _compactRoot.gameObject.activeSelf)
            {
                float e = Ease((time - _compactShownAt) / 0.35f);
                _compactRoot.anchoredPosition = new Vector2(-24f + (1f - e) * (CompactW + 60f), -332f);

                float frac = Mathf.Clamp01(_compactRemaining / _compactTotal);
                _compactRing.Set(90f, 90f - 360f * frac);
                _compactSeconds.text = Mathf.CeilToInt(Mathf.Max(0f, _compactRemaining)).ToString();
                bool low = _compactRemaining <= 4f;
                _compactSeconds.color = low ? Color.Lerp(Color.white, new Color(1f, 0.85f, 0.2f), 0.5f + 0.5f * Mathf.Sin(time * 10f)) : Color.white;
            }
        }
    }
}
