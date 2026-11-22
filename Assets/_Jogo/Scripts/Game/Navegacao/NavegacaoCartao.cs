using Entregador.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Entregador.Game
{
    /// <summary>
    /// Cartão provisório da navegação (seta, distância, rua e a manobra seguinte), só para ver os dados do RouteGuide funcionando.
    /// O cartão definitivo é do GameUI; este fica desligado por padrão (Visivel = false) e os testes de captura o ligam.
    /// </summary>
    public sealed class NavegacaoCartao : MonoBehaviour
    {
        private RouteGuide _guide;
        private GameObject _root;
        private TMP_Text _arrow;
        private TMP_Text _distance;
        private TMP_Text _street;
        private TMP_Text _hint;

        public bool Visivel { get; set; }

        public string ArrowText => _arrow != null ? _arrow.text : string.Empty;

        public string DistanceText => _distance != null ? _distance.text : string.Empty;

        public string StreetText => _street != null ? _street.text : string.Empty;

        public void Build(RouteGuide guide)
        {
            _guide = guide;
            var canvasObject = new GameObject("CanvasNavegacao", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 19;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;

            _root = new GameObject("Cartao", typeof(RectTransform), typeof(Image));
            _root.transform.SetParent(canvasObject.transform, false);
            _root.GetComponent<Image>().color = new Color(0.05f, 0.06f, 0.09f, 0.85f);
            var rect = _root.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -108f);
            rect.sizeDelta = new Vector2(440f, 96f);

            _arrow = Text("Seta", 64, TextAlignmentOptions.Center, new Color(0.3f, 0.9f, 1f), new Vector2(-170f, 0f), new Vector2(90f, 90f));
            _distance = Text("Distancia", 38, TextAlignmentOptions.Left, Color.white, new Vector2(30f, 20f), new Vector2(300f, 44f));
            _street = Text("Rua", 22, TextAlignmentOptions.Left, new Color(0.85f, 0.9f, 1f), new Vector2(30f, -14f), new Vector2(340f, 30f));
            _hint = Text("Depois", 16, TextAlignmentOptions.Left, new Color(0.65f, 0.72f, 0.85f), new Vector2(30f, -38f), new Vector2(340f, 22f));
            _root.SetActive(false);
        }

        private TMP_Text Text(string name, float size, TextAlignmentOptions alignment, Color color, Vector2 position, Vector2 box)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(_root.transform, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = box;

            return text;
        }

        private void Update()
        {
            if (_root == null || _guide == null)
            {
                return;
            }

            InstrucaoGps next = _guide.ProximaInstrucao;
            bool show = Visivel && next.Existe;
            _root.SetActive(show);

            if (!show)
            {
                return;
            }

            // Uma seta para cima girada pelo ângulo da curva (positivo = direita).
            double angle = next.Manobra == Manobra.Retorno ? 180.0 : next.Manobra == Manobra.Chegada || next.Manobra == Manobra.SigaEmFrente || next.Manobra == Manobra.RotatoriaSaida ? 0.0 : next.Instrucao.Angulo;
            _arrow.text = next.Manobra == Manobra.Chegada ? "●" : "↑";
            _arrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, (float)-angle);
            _distance.text = next.DistanciaTexto;
            _street.text = next.Manobra == Manobra.Chegada ? "Destino" : next.Rua;
            InstrucaoGps then = _guide.SegundaInstrucao;
            _hint.text = then.Existe ? "Depois: " + then.Texto : string.Empty;
        }
    }
}
