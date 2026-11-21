using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Entregador.Game
{
    /// <summary>Botão que vale enquanto está pressionado (direção, acelerador, freio na tela).</summary>
    public sealed class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public Action<bool> Changed;

        public bool IsDown { get; private set; }

        public void OnPointerDown(PointerEventData eventData) => Set(true);

        public void OnPointerUp(PointerEventData eventData) => Set(false);

        public void OnPointerExit(PointerEventData eventData) => Set(false);

        private void OnDisable() => Set(false);

        private void Set(bool down)
        {
            if (IsDown == down)
            {
                return;
            }

            IsDown = down;
            Changed?.Invoke(down);
        }
    }
}
