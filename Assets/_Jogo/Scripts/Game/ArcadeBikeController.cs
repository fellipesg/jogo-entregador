using System;
using UnityEngine;

namespace Entregador.Game
{
    /// <summary>
    /// Moto arcade sobre um Rigidbody. A física fica sempre em pé (só gira no eixo Y); a inclinação é só visual.
    /// Avisa por eventos quando a comida leva um tranco (batida, freada forte, buraco) e quando a moto se danifica.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class ArcadeBikeController : MonoBehaviour
    {
        [Header("Movimento")]
        [SerializeField] private float _topSpeed = 13f;
        [SerializeField] private float _acceleration = 8f;
        [SerializeField] private float _brakeDeceleration = 22f;
        [SerializeField] private float _coastDeceleration = 3f;
        [SerializeField] private float _reverseTopSpeed = 4f;

        [Header("Direção")]
        [SerializeField] private float _yawRateLow = 120f;
        [SerializeField] private float _yawRateHigh = 62f;

        [Header("Aderência")]
        [SerializeField] private float _gripDry = 9f;
        [SerializeField] private float _gripWet = 3.2f;
        [SerializeField] private float _wetSteerFactor = 0.85f;

        [Header("Impactos")]
        [SerializeField] private float _impactMinSpeed = 3f;
        [SerializeField] private float _impactFoodPerMps = 0.04f;
        [SerializeField] private float _impactDamagePerMps = 0.012f;
        [SerializeField] private float _hardBrakeMinSpeed = 7f;
        [SerializeField] private float _hardBrakeFoodPerSecond = 0.05f;
        [SerializeField] private float _bumpFood = 0.06f;

        [Header("Visual")]
        [SerializeField] private Transform _leanRoot;
        [SerializeField] private float _maxLeanDegrees = 22f;

        private Rigidbody _body;
        private BikeInput _input;
        private float _yaw;
        private float _lean;
        private float _lastImpactTime = -10f;
        private float _lastBumpTime = -10f;

        public event Action<float> FoodShock;
        public event Action<float> DamageTaken;
        public event Action Teleported;

        /// <summary>Com a moto travada a entrada é ignorada e ela freia até parar (telas, painéis).</summary>
        public bool Locked { get; private set; } = true;

        public bool IsWet { get; private set; }

        public Vector3 Position => _body.position;

        public Vector3 Forward => Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;

        public float Yaw => _yaw;

        public float TopSpeed => _topSpeed;

        /// <summary>Velocidade para frente em m/s (negativa na ré).</summary>
        public float ForwardSpeed { get; private set; }

        /// <summary>Direção aplicada neste quadro de física (-1 a 1; 0 com a moto travada). Só leitura, para o visual.</summary>
        public float SteerInput { get; private set; }

        /// <summary>Freio do jogador (0 a 1; 0 com a moto travada). Só leitura, para a lanterna.</summary>
        public float BrakeInput { get; private set; }

        /// <summary>Inclinação visual atual em graus (sinal do eixo Z; positivo = para a esquerda).</summary>
        public float Lean => _lean;

        public float Speed => new Vector3(_body.linearVelocity.x, 0f, _body.linearVelocity.z).magnitude;

        public void Configure(BikeInput input, Transform leanRoot)
        {
            _input = input;
            _leanRoot = leanRoot;
        }

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();

            if (_input == null)
            {
                _input = GetComponent<BikeInput>();
            }

            _body.freezeRotation = true;
            _body.interpolation = RigidbodyInterpolation.Interpolate;
            _body.collisionDetectionMode = CollisionDetectionMode.Continuous;
            _yaw = transform.eulerAngles.y;
        }

        public void SetLocked(bool locked)
        {
            Locked = locked;
        }

        /// <summary>Chuva: menos aderência e direção mais mole.</summary>
        public void SetWet(bool wet)
        {
            IsWet = wet;
        }

        public void Teleport(Vector3 position, float yawDegrees)
        {
            _yaw = yawDegrees;
            _body.position = position;
            _body.rotation = Quaternion.Euler(0f, yawDegrees, 0f);
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yawDegrees, 0f));
            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;
            ForwardSpeed = 0f;
            Teleported?.Invoke();
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            float steerInput = Locked || _input == null ? 0f : _input.Steer;
            float gas = Locked || _input == null ? 0f : _input.Gas;
            float brake = Locked || _input == null ? 1f : _input.Brake;
            SteerInput = steerInput;
            BrakeInput = Locked || _input == null ? 0f : _input.Brake;

            Vector3 velocity = _body.linearVelocity;

            // Primeiro gira (com a velocidade da física anterior), depois decompõe a velocidade no novo eixo:
            // com pouca aderência a parte lateral demora a sumir e a moto escorrega.
            float turnSpeed = Vector3.Dot(velocity, Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward);
            float turnRatio = Mathf.Clamp01(Mathf.Abs(turnSpeed) / _topSpeed);
            float yawRate = Mathf.Lerp(_yawRateLow, _yawRateHigh, turnRatio);

            if (IsWet)
            {
                yawRate *= _wetSteerFactor;
            }

            float lowSpeedFade = Mathf.Clamp01(Mathf.Abs(turnSpeed) / 3f);
            _yaw += steerInput * yawRate * lowSpeedFade * (turnSpeed < 0f ? -1f : 1f) * dt;

            Vector3 forward = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
            Vector3 right = Quaternion.Euler(0f, _yaw, 0f) * Vector3.right;

            float forwardSpeed = Vector3.Dot(velocity, forward);
            float lateralSpeed = Vector3.Dot(velocity, right);
            float previousForward = forwardSpeed;

            if (brake > 0.05f)
            {
                // Freio: reduz até parar; parado e segurando, engata a ré devagar.
                if (forwardSpeed > 0.4f)
                {
                    forwardSpeed = Mathf.Max(0f, forwardSpeed - _brakeDeceleration * brake * dt);
                }
                else if (Locked)
                {
                    forwardSpeed = 0f;
                }
                else
                {
                    forwardSpeed = Mathf.Max(-_reverseTopSpeed, forwardSpeed - _acceleration * 0.5f * dt);
                }
            }
            else if (gas > 0.05f)
            {
                float room = Mathf.Clamp01(1f - Mathf.Max(0f, forwardSpeed) / _topSpeed);
                float push = _acceleration * Mathf.Lerp(0.35f, 1f, room) * gas;

                forwardSpeed = forwardSpeed < 0f
                    ? Mathf.Min(0f, forwardSpeed + _brakeDeceleration * dt)
                    : Mathf.Min(_topSpeed, forwardSpeed + push * dt);
            }
            else
            {
                forwardSpeed = Mathf.MoveTowards(forwardSpeed, 0f, _coastDeceleration * dt);
            }

            // Freada forte em velocidade abala a comida.
            if (brake > 0.05f && previousForward > _hardBrakeMinSpeed)
            {
                FoodShock?.Invoke(_hardBrakeFoodPerSecond * dt * brake);
            }

            float grip = IsWet ? _gripWet : _gripDry;
            lateralSpeed *= Mathf.Exp(-grip * dt);

            float speedRatio = Mathf.Clamp01(Mathf.Abs(forwardSpeed) / _topSpeed);

            Vector3 planar = forward * forwardSpeed + right * lateralSpeed;
            _body.linearVelocity = new Vector3(planar.x, velocity.y, planar.z);
            _body.MoveRotation(Quaternion.Euler(0f, _yaw, 0f));

            ForwardSpeed = forwardSpeed;

            float targetLean = -steerInput * speedRatio * _maxLeanDegrees;
            _lean = Mathf.Lerp(_lean, targetLean, 1f - Mathf.Exp(-8f * dt));

            if (_leanRoot != null)
            {
                _leanRoot.localRotation = Quaternion.Euler(0f, 0f, _lean);
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            // Chão e rampas suaves não contam como batida.
            bool wall = false;

            for (int i = 0; i < collision.contactCount; i++)
            {
                if (collision.GetContact(i).normal.y < 0.5f)
                {
                    wall = true;

                    break;
                }
            }

            if (!wall || Time.time - _lastImpactTime < 0.4f)
            {
                return;
            }

            float impact = collision.relativeVelocity.magnitude;

            if (impact < _impactMinSpeed)
            {
                return;
            }

            _lastImpactTime = Time.time;
            float over = impact - _impactMinSpeed;

            FoodShock?.Invoke(over * _impactFoodPerMps + 0.03f);
            DamageTaken?.Invoke(over * _impactDamagePerMps + 0.01f);
        }

        /// <summary>Buraco ou lombada: tranco na comida e um pulinho.</summary>
        public void RegisterBump(float severity = 1f)
        {
            if (Time.time - _lastBumpTime < 1f)
            {
                return;
            }

            _lastBumpTime = Time.time;
            Vector3 velocity = _body.linearVelocity;
            _body.linearVelocity = new Vector3(velocity.x, Mathf.Max(velocity.y, 1.6f * severity), velocity.z);
            FoodShock?.Invoke(_bumpFood * severity);
        }

        /// <summary>Entrada de dano externa (usada em testes).</summary>
        public void ApplyDamage(float amount)
        {
            DamageTaken?.Invoke(amount);
        }
    }
}
