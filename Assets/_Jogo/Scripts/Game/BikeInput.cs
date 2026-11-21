using UnityEngine;
using UnityEngine.InputSystem;

namespace Entregador.Game
{
    /// <summary>
    /// Junta teclado (WASD/setas), botões da tela (toque) e entrada virtual (testes) em três valores:
    /// direção (-1 a 1), acelerador (0 a 1) e freio (0 a 1).
    /// </summary>
    public sealed class BikeInput : MonoBehaviour
    {
        private float _keySteer;
        private float _keyGas;
        private float _keyBrake;

        private float _virtualSteer;
        private float _virtualGas;
        private float _virtualBrake;

        public bool TouchLeft { get; set; }
        public bool TouchRight { get; set; }
        public bool TouchGas { get; set; }
        public bool TouchBrake { get; set; }

        public float Steer =>
            Mathf.Clamp(_keySteer + (TouchRight ? 1f : 0f) - (TouchLeft ? 1f : 0f) + _virtualSteer, -1f, 1f);

        public float Gas => Mathf.Clamp01(Mathf.Max(_keyGas, TouchGas ? 1f : 0f, _virtualGas));

        public float Brake => Mathf.Clamp01(Mathf.Max(_keyBrake, TouchBrake ? 1f : 0f, _virtualBrake));

        /// <summary>Entrada fixa, usada pelos testes e por ferramentas.</summary>
        public void SetVirtual(float steer, float gas, float brake)
        {
            _virtualSteer = steer;
            _virtualGas = gas;
            _virtualBrake = brake;
        }

        public void ClearAll()
        {
            SetVirtual(0, 0, 0);
            TouchLeft = TouchRight = TouchGas = TouchBrake = false;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;

            if (keyboard == null)
            {
                _keySteer = _keyGas = _keyBrake = 0f;

                return;
            }

            _keySteer = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1f : 0f)
                      - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1f : 0f);
            _keyGas = keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1f : 0f;
            _keyBrake = keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed || keyboard.spaceKey.isPressed ? 1f : 0f;
        }
    }
}
