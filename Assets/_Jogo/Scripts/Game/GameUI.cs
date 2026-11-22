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
    /// Toda a interface, montada por código: HUD de corrida (painel da moto, minimapa, navegação), a cortina de informações,
    /// o app de entrega (painéis em forma de celular), telas de menu e avisos. Os arquivos da pasta UI completam esta classe.
    /// </summary>
    public sealed partial class GameUI : MonoBehaviour
    {
        private Canvas _canvas;
        private CanvasScaler _scaler;
        private RectTransform _safe;
        private Rect _lastSafe;
        private Vector2 _lastScreen;
        private BikeInput _input;

        public Canvas Canvas => _canvas;

        /// <summary>Estado das luzes de aviso da moto (simulação simples; os testes acendem pelos campos).</summary>
        public PainelMotoEstado Painel { get; } = new PainelMotoEstado();

        public void Build(BikeInput input, Texture minimap, Action onSkip)
        {
            _input = input;

            EnsureEventSystem();

            var canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            _canvas = canvasObject.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            _scaler = canvasObject.GetComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = new Vector2(1920f, 1080f);
            _scaler.matchWidthOrHeight = 0.5f;

            _safe = UiKit.Rect("AreaSegura", canvasObject.transform);
            UiKit.Stretch(_safe);

            BuildHud(_safe, minimap, onSkip);
            BuildInfoShade(_safe);
            BuildToast(_safe);
            BuildModal(canvasObject.transform);
            BuildScreens(canvasObject.transform);
            BuildDayTitle(canvasObject.transform);

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

        /// <summary>Respeita a área segura do aparelho (notch, cantos arredondados).</summary>
        private void ApplySafeArea()
        {
            Rect safe = Screen.safeArea;
            var screen = new Vector2(Screen.width, Screen.height);

            if (safe == _lastSafe && screen == _lastScreen || screen.x < 1f || screen.y < 1f)
            {
                return;
            }

            _lastSafe = safe;
            _lastScreen = screen;
            _safe.anchorMin = new Vector2(safe.xMin / screen.x, safe.yMin / screen.y);
            _safe.anchorMax = new Vector2(safe.xMax / screen.x, safe.yMax / screen.y);
            _safe.offsetMin = _safe.offsetMax = Vector2.zero;
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            float time = Time.unscaledTime;
            ApplySafeArea();

            if (_hudRoot != null && _hudRoot.activeInHierarchy)
            {
                _painelView.Tick(dt, time);
                TickNav(time);
            }

            TickShade(dt);
            TickModal(dt, time);
            TickToast(dt);
            TickDayTitle();
            TickOffer(time);
            TickOfertasUi(time);
            TickScreens(dt);
            TickRadioPilula();
        }

        // ---------- Utilidades ----------

        /// <summary>Formato "m:ss".</summary>
        public static string FormatTime(float seconds)
        {
            int total = Mathf.Max(0, Mathf.RoundToInt(seconds));

            return $"{total / 60}:{total % 60:00}";
        }

        /// <summary>"200 m" ou "1,2 km".</summary>
        public static string FormatDistance(float meters)
        {
            if (meters >= 1000f)
            {
                return (meters / 1000f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',') + " km";
            }

            float step = meters >= 100f ? 10f : 5f;

            return (Mathf.Round(meters / step) * step).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " m";
        }

        private static float Ease(float t)
        {
            t = Mathf.Clamp01(t);

            return 1f - (1f - t) * (1f - t) * (1f - t);
        }
    }

    /// <summary>Arrasto de um elemento (a cortina de informações): repassa o gesto ao GameUI.</summary>
    public sealed class UiShadeDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public GameUI Ui;
        private float _lastY;
        private float _lastTime;
        private float _velocity;

        public void OnBeginDrag(PointerEventData e)
        {
            _lastY = e.position.y;
            _lastTime = Time.unscaledTime;
            _velocity = 0f;
            Ui.ShadeBegin();
        }

        public void OnDrag(PointerEventData e)
        {
            float scale = Ui.Canvas != null ? Ui.Canvas.scaleFactor : 1f;
            float dy = (_lastY - e.position.y) / scale;
            float dt = Mathf.Max(0.001f, Time.unscaledTime - _lastTime);
            _velocity = Mathf.Lerp(_velocity, dy / dt, 0.5f);
            _lastY = e.position.y;
            _lastTime = Time.unscaledTime;
            Ui.ShadeDrag(dy);
        }

        public void OnEndDrag(PointerEventData e)
        {
            Ui.ShadeEnd(_velocity);
        }
    }

    /// <summary>Segurar o dedo (ou o mouse) no minimapa mostra a cortina enquanto estiver pressionado.</summary>
    public sealed class UiHoldToShow : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public GameUI Ui;

        public void OnPointerDown(PointerEventData e) => Ui.SetInfoHold(true);

        public void OnPointerUp(PointerEventData e) => Ui.SetInfoHold(false);

        public void OnPointerExit(PointerEventData e) => Ui.SetInfoHold(false);
    }
}
