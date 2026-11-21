using UnityEngine;
using UnityEngine.InputSystem;

namespace Entregador.Game
{
    /// <summary>
    /// Junta as entradas da moto em três valores: direção (-1 a 1), acelerador (0 a 1) e freio (0 a 1).
    /// Fontes: teclado (W/S/A/D, setas, espaço), controle (analógico esquerdo e gatilhos), zonas de toque invisíveis
    /// (TouchZone: metade esquerda dirige, metade direita acelera/freia), inclinação do celular (opcional) e entrada virtual (testes).
    /// </summary>
    public sealed class BikeInput : MonoBehaviour
    {
        /// <summary>Inclinação (em g, na horizontal do aparelho) que equivale a esterçar tudo.</summary>
        private const float TiltFullScale = 0.4f;

        private const float TiltDeadZone = 0.05f;

        private float _keySteer;
        private float _keyGas;
        private float _keyBrake;
        private float _padSteer;
        private float _padGas;
        private float _padBrake;
        private float _tilt;

        private float _virtualSteer;
        private float _virtualGas;
        private float _virtualBrake;

        /// <summary>Direção vinda da zona de toque esquerda (-1 a 1).</summary>
        public float TouchSteer { get; set; }

        /// <summary>Acelerador vindo da zona de toque direita, arrastando para cima (0 a 1).</summary>
        public float TouchGas { get; set; }

        /// <summary>Freio vindo da zona de toque direita, arrastando para baixo (0 a 1).</summary>
        public float TouchBrake { get; set; }

        /// <summary>Esterçar inclinando o celular (acelerômetro). Desligado por padrão; o GameFlow copia de GameTuning.</summary>
        public bool TiltSteering { get; set; }

        public float Steer =>
            Mathf.Clamp(_keySteer + _padSteer + TouchSteer + (TiltSteering ? _tilt : 0f) + _virtualSteer, -1f, 1f);

        public float Gas => Mathf.Clamp01(Mathf.Max(_keyGas, _padGas, TouchGas, _virtualGas));

        public float Brake => Mathf.Clamp01(Mathf.Max(_keyBrake, _padBrake, TouchBrake, _virtualBrake));

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
            TouchSteer = TouchGas = TouchBrake = 0f;
        }

        private void Update()
        {
            ReadKeyboard();
            ReadGamepad();
            ReadTilt();
        }

        private void ReadKeyboard()
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

        private void ReadGamepad()
        {
            Gamepad pad = Gamepad.current;

            if (pad == null)
            {
                _padSteer = _padGas = _padBrake = 0f;

                return;
            }

            float stick = pad.leftStick.x.ReadValue();
            _padSteer = Mathf.Abs(stick) < 0.12f ? 0f : stick;
            _padGas = pad.rightTrigger.ReadValue();
            _padBrake = Mathf.Max(pad.leftTrigger.ReadValue(), pad.buttonSouth.isPressed ? 1f : 0f);
        }

        private void ReadTilt()
        {
            if (!TiltSteering || Accelerometer.current == null)
            {
                _tilt = 0f;

                return;
            }

            if (!Accelerometer.current.enabled)
            {
                InputSystem.EnableDevice(Accelerometer.current);
            }

            // Celular na horizontal, segurado como um guidão: a inclinação aparece no eixo curto do aparelho (y).
            Vector3 g = Accelerometer.current.acceleration.ReadValue();
            float side = Screen.orientation == ScreenOrientation.LandscapeRight ? -g.y : g.y;
            float value = Mathf.Abs(side) < TiltDeadZone ? 0f : side;
            _tilt = Mathf.Clamp(value / TiltFullScale, -1f, 1f);
        }
    }
}
