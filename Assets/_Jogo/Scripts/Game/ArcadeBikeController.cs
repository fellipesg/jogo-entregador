using System;
using UnityEngine;

namespace Entregador.Game
{
    /// <summary>
    /// Moto arcade sobre um Rigidbody. A física fica sempre em pé (só gira no eixo Y); a inclinação é só visual.
    /// Moto de verdade: motor com câmbio automático de 5 marchas, giro, freio-motor, arrasto e freio combinado (ver MotoFisica), sem teto
    /// artificial de velocidade; a direção fica mais firme em alta velocidade.
    /// Avisa por eventos quando a comida leva um tranco (batida, freada forte, buraco) e quando a moto se danifica.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class ArcadeBikeController : MonoBehaviour
    {
        [Header("Movimento")]
        [SerializeField] private MotoModelo _modelo = MotoModelo.Titan;
        [SerializeField] private float _reverseAcceleration = 3f;
        [SerializeField] private float _reverseTopSpeed = 4f;
        [SerializeField] private float _lockedBrakeFactor = 1.25f;

        [Header("Direção")]
        [SerializeField] private float _steerResponseLow = 12f;
        [SerializeField] private float _steerResponseHigh = 4f;

        [Header("Aderência")]
        [SerializeField] private float _gripDry = 9f;
        [SerializeField] private float _gripWet = 3.2f;
        [SerializeField] private float _wetSteerFactor = 0.85f;

        [Header("Impactos")]
        [SerializeField] private float _impactMinSpeed = 3f;
        [SerializeField] private float _impactFoodPerMps = 0.04f;
        [SerializeField] private float _impactDamagePerMps = 0.012f;
        [SerializeField] private float _hardBrakeMinSpeed = 7f;
        [SerializeField] private float _hardBrakeMinDecel = 5.5f;
        [SerializeField] private float _hardBrakeFoodPerSecond = 0.05f;
        [SerializeField] private float _bumpFood = 0.06f;
        [SerializeField] private float _hardCornerFoodPerSecond = 0.03f;

        [Header("Visual")]
        [SerializeField] private Transform _leanRoot;
        [SerializeField] private float _maxLeanDegrees = 26f;

        private Rigidbody _body;
        private BikeInput _input;
        private MotoFisicaPerfil _perfil = MotoFisicaCatalogo.Titan;
        private Transmissao _transmissao = new Transmissao(MotoFisicaCatalogo.Titan);
        private float _yaw;
        private float _steer;
        private float _throttle;
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

        /// <summary>Velocidade final do modelo atual, em m/s.</summary>
        public float TopSpeed => _perfil.VelocidadeFinal;

        /// <summary>Velocidade para frente em m/s (negativa na ré).</summary>
        public float ForwardSpeed { get; private set; }

        /// <summary>Direção aplicada neste quadro de física (-1 a 1; 0 com a moto travada), já suavizada. Só leitura, para o visual.</summary>
        public float SteerInput => _steer;

        /// <summary>Freio do jogador (0 a 1; 0 com a moto travada). Só leitura, para a lanterna.</summary>
        public float BrakeInput { get; private set; }

        /// <summary>Acelerador aplicado (0 a 1). Só leitura, para o HUD e o som.</summary>
        public float Throttle => _throttle;

        /// <summary>Freio aplicado (0 a 1). Igual a BrakeInput, com o nome que o HUD usa.</summary>
        public float Brake => BrakeInput;

        /// <summary>Giro do motor em rpm (marcha lenta ~1.300 até o corte de giro).</summary>
        public float Rpm => _transmissao.Rpm;

        /// <summary>Marcha engatada pelo câmbio automático, de 1 a 5.</summary>
        public int Gear => _transmissao.Marcha;

        public float RpmLimit => _perfil.RpmLimite;

        public MotoModelo Modelo => _modelo;

        public MotoFisicaPerfil Perfil => _perfil;

        /// <summary>Inclinação visual atual em graus (sinal do eixo Z; positivo = para a esquerda).</summary>
        public float Lean => _lean;

        /// <summary>Velocidade no plano em m/s.</summary>
        public float Speed => new Vector3(_body.linearVelocity.x, 0f, _body.linearVelocity.z).magnitude;

        /// <summary>Velocidade no plano em km/h (o mundo está em metros reais).</summary>
        public float SpeedKmh => Speed * 3.6f;

        /// <summary>Troca o modelo da moto (e a física dele). A parte visual é do MotoVisual.</summary>
        public void SetModelo(MotoModelo modelo)
        {
            _modelo = modelo;
            _perfil = MotoFisicaCatalogo.Para(modelo);
            _transmissao = new Transmissao(_perfil);
        }

        public void Configure(BikeInput input, Transform leanRoot)
        {
            _input = input;
            _leanRoot = leanRoot;
        }

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            SetModelo(_modelo);

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
            _steer = 0f;
            Teleported?.Invoke();
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            bool free = !Locked && _input != null;
            float steerInput = free ? _input.Steer : 0f;
            float gas = free ? _input.Gas : 0f;
            float brake = free ? _input.Brake : 1f;
            BrakeInput = free ? _input.Brake : 0f;

            Vector3 velocity = _body.linearVelocity;
            float turnSpeed = Vector3.Dot(velocity, Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward);
            float absTurn = Mathf.Abs(turnSpeed);

            // Direção com resposta mais lenta em alta velocidade (estável), e giro limitado pela aceleração lateral:
            // em baixa velocidade gira rápido; a 100 km/h só faz curvas abertas.
            float speedRatio01 = Mathf.Clamp01(absTurn / 30f);
            float response = Mathf.Lerp(_steerResponseLow, _steerResponseHigh, speedRatio01);
            _steer = Mathf.MoveTowards(_steer, steerInput, response * dt);
            float yawRate = Mathf.Min(_perfil.GiroBaixa, _perfil.AceleracaoLateral * Mathf.Rad2Deg / Mathf.Max(absTurn, 1f));

            if (IsWet)
            {
                yawRate *= _wetSteerFactor;
            }

            // Primeiro gira (com a velocidade da física anterior), depois decompõe a velocidade no novo eixo:
            // com pouca aderência a parte lateral demora a sumir e a moto escorrega.
            float lowSpeedFade = Mathf.Clamp01(absTurn / 3f);
            _yaw += _steer * yawRate * lowSpeedFade * (turnSpeed < 0f ? -1f : 1f) * dt;

            Vector3 forward = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
            Vector3 right = Quaternion.Euler(0f, _yaw, 0f) * Vector3.right;

            float forwardSpeed = Vector3.Dot(velocity, forward);
            float lateralSpeed = Vector3.Dot(velocity, right);
            float previousForward = forwardSpeed;
            float wetGrip = IsWet ? 0.75f : 1f;

            if (brake > 0.05f)
            {
                // Freio combinado: reduz até parar; parado e segurando (sem estar travada), engata a ré devagar.
                _throttle = 0f;
                _transmissao.Step(Mathf.Max(0f, forwardSpeed), 0f, dt);

                if (forwardSpeed > 0.4f)
                {
                    float factor = Locked ? _lockedBrakeFactor : brake;
                    forwardSpeed = Mathf.Max(0f, forwardSpeed - _perfil.Freio * factor * wetGrip * dt);
                }
                else if (Locked)
                {
                    forwardSpeed = 0f;
                }
                else
                {
                    forwardSpeed = Mathf.Max(-_reverseTopSpeed, forwardSpeed - _reverseAcceleration * dt);
                }
            }
            else if (gas > 0.05f && forwardSpeed < 0f)
            {
                // Estava de ré: o acelerador primeiro para a moto.
                _throttle = gas;
                _transmissao.Step(0f, gas, dt);
                forwardSpeed = Mathf.Min(0f, forwardSpeed + _perfil.Freio * dt);
            }
            else
            {
                // Motor, freio-motor, arrasto e rolagem. Sem limitador artificial: a velocidade final sai do equilíbrio.
                _throttle = gas > 0.05f ? gas : 0f;
                float engine = _transmissao.Step(forwardSpeed, _throttle, dt);

                if (IsWet && engine > 0f)
                {
                    engine *= 0.8f;
                }

                float resistance = _perfil.Arrasto * forwardSpeed * forwardSpeed + (forwardSpeed > 0.05f ? _perfil.Rolagem : 0f);
                float next = forwardSpeed + (engine - resistance) * dt;
                forwardSpeed = _throttle <= 0f && forwardSpeed > 0f ? Mathf.Max(0f, next) : next;

                if (forwardSpeed < 0f)
                {
                    forwardSpeed = Mathf.Min(0f, forwardSpeed + _perfil.Freio * dt);
                }
            }

            // Freada forte em velocidade abala a comida; quanto mais rápido, mais.
            float decel = (previousForward - forwardSpeed) / dt;

            if (brake > 0.05f && previousForward > _hardBrakeMinSpeed && decel > _hardBrakeMinDecel)
            {
                float speedFactor = Mathf.Clamp(previousForward / 12f, 0.6f, 2.5f);
                FoodShock?.Invoke(_hardBrakeFoodPerSecond * speedFactor * dt * brake);
            }

            // Curva fechada em velocidade também balança a comida.
            if (Mathf.Abs(_steer) > 0.6f && forwardSpeed > 15f)
            {
                FoodShock?.Invoke(_hardCornerFoodPerSecond * (forwardSpeed / 15f) * dt);
            }

            float grip = (IsWet ? _gripWet : _gripDry);
            lateralSpeed *= Mathf.Exp(-grip * dt);

            Vector3 planar = forward * forwardSpeed + right * lateralSpeed;
            _body.linearVelocity = new Vector3(planar.x, velocity.y, planar.z);
            _body.MoveRotation(Quaternion.Euler(0f, _yaw, 0f));

            ForwardSpeed = forwardSpeed;

            float leanSpeed = Mathf.Clamp01(Mathf.Abs(forwardSpeed) / 13f);
            float targetLean = -_steer * leanSpeed * _maxLeanDegrees;
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

        /// <summary>Buraco ou lombada: tranco na comida e um pulinho. Quanto mais rápido, pior o tranco (e o pulo).</summary>
        public void RegisterBump(float severity = 1f)
        {
            if (Time.time - _lastBumpTime < 1f)
            {
                return;
            }

            _lastBumpTime = Time.time;
            float speedFactor = 0.6f + Mathf.Max(0f, Speed) / 14f;
            Vector3 velocity = _body.linearVelocity;
            _body.linearVelocity = new Vector3(velocity.x, Mathf.Max(velocity.y, 1.6f * severity * Mathf.Min(speedFactor, 2f)), velocity.z);
            FoodShock?.Invoke(_bumpFood * severity * speedFactor);
        }

        /// <summary>Entrada de dano externa (usada em testes).</summary>
        public void ApplyDamage(float amount)
        {
            DamageTaken?.Invoke(amount);
        }
    }
}
