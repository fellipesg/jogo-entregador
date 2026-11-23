using System;
using UnityEngine;

namespace Entregador.Game
{
    /// <summary>
    /// Moto arcade sobre um Rigidbody. A física fica sempre em pé (só gira no eixo Y); a inclinação é só visual.
    /// Moto de verdade: motor com câmbio automático de 5 marchas, giro, freio-motor, arrasto e freio combinado (ver MotoFisica), sem teto
    /// artificial de velocidade. A direção comanda a inclinação (ver Pilotagem): ela demora mais para subir em alta velocidade, a guinada sai
    /// dela pela curva coordenada, a aderência limita a curva (chuva e freio tiram atrito) e abaixo de ~15 km/h o guidão gira direto.
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

        [Header("Direção (o resto está no perfil de cada moto, MotoFisica)")]
        [SerializeField] private float _rampaEntradaLenta = 8f;
        [SerializeField] private float _rampaEntradaRapida = 3.5f;
        [SerializeField] private float _tempoRetornoFator = 0.6f;

        [Header("Aderência")]
        [SerializeField] private float _aderenciaLateralSeca = 12f;
        [SerializeField] private float _aderenciaLateralMolhada = 4.5f;
        [SerializeField] private float _aderenciaMolhado = 0.6f;
        [SerializeField] private float _freioMolhado = 0.75f;
        [SerializeField] private float _atritoDerrapando = 0.8f;

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

        private Rigidbody _body;
        private BikeInput _input;
        private MotoFisicaPerfil _perfil = MotoFisicaCatalogo.Titan;
        private Transmissao _transmissao = new Transmissao(MotoFisicaCatalogo.Titan);
        private float _yaw;
        private float _steer;
        private float _throttle;
        private bool _revving;
        private float _launchTimer;
        private float _lean;
        private float _leanTaxa;
        private float _freioSuave;
        private float _derrapagem;
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

        /// <summary>Parado, com freio e acelerador juntos: embreagem puxada, giro livre ("cortando giro"). O painel pode piscar com isto.</summary>
        public bool CortandoGiro => _transmissao.Embreagem;

        /// <summary>O limitador de giro está cortando (parado ou rodando no giro máximo).</summary>
        public bool NoLimitador => _transmissao.NoLimitador;

        /// <summary>Marcha engatada pelo câmbio automático, de 1 a 5.</summary>
        public int Gear => _transmissao.Marcha;

        public float RpmLimit => _perfil.RpmLimite;

        /// <summary>A frente escapou (a curva passou do atrito): pneu cantando. O áudio usa isto.</summary>
        public bool Derrapando => _derrapagem > 0.3f;

        /// <summary>Quanto a moto está escapando de frente (0 a 1), para o volume do pneu cantando.</summary>
        public float Derrapagem => _derrapagem;

        /// <summary>Aceleração lateral atual da curva (m/s²), vinda da inclinação.</summary>
        public float AceleracaoLateralAtual => Pilotagem.AceleracaoLateral(_lean);

        public MotoModelo Modelo => _modelo;

        public MotoFisicaPerfil Perfil => _perfil;

        /// <summary>Inclinação física atual em graus (sinal do eixo Z; positivo = para a esquerda). A guinada sai dela.</summary>
        public float Lean => -_lean;

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
            _lean = 0f;
            _leanTaxa = 0f;
            _derrapagem = 0f;
            _freioSuave = 0f;
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

            // Entrada de direção: rampa (nunca de 0 a 1 de uma vez; mais lenta em alta velocidade) e curva mais fina perto do centro em alta.
            bool voltando = Mathf.Abs(steerInput) < Mathf.Abs(_steer) || steerInput * _steer < 0f;
            float rampa = Pilotagem.TaxaRampaEntrada(absTurn, voltando, _rampaEntradaLenta, _rampaEntradaRapida);
            _steer = Mathf.MoveTowards(_steer, steerInput, rampa * dt);
            float comando = Pilotagem.FormarEntrada(_steer, absTurn, _perfil.ExpoenteDirecaoRapida);

            // Aderência: o freio usa parte do atrito do pneu (círculo de atrito), a chuva reduz o atrito todo.
            float aderencia = IsWet ? _aderenciaMolhado : 1f;
            float freioAlvo = brake > 0.05f && turnSpeed > 0.4f ? brake : 0f;
            _freioSuave = Mathf.MoveTowards(_freioSuave, freioAlvo, 6f * dt);
            float freioUsado = _freioSuave * _perfil.Freio * (IsWet ? _freioMolhado : 1f);
            float incMax = Pilotagem.InclinacaoMaxima(_perfil.InclinacaoMaxima, _perfil.AceleracaoLateral, aderencia, freioUsado);
            float aLatMax = Pilotagem.AceleracaoLateralDisponivel(_perfil.AceleracaoLateral, aderencia, freioUsado);

            // Inclinação: o comando pede um ângulo (até o fundo de curso, limitado pela aderência); ela sobe com tempo que cresce com a
            // velocidade e volta ao centro mais rápido (a moto se levanta sozinha). Amortecimento crítico: sem oscilar.
            float pesoInclinacao = Pilotagem.PesoInclinacao(absTurn);
            float fundo = Mathf.Min(incMax, _perfil.InclinacaoMaxima * Pilotagem.FatorInclinacaoRapida(absTurn) * 0.97f);
            float parada = Mathf.Clamp01(absTurn / 2.5f);
            float visual = Mathf.Lerp(0.3f, 1f, pesoInclinacao);
            float alvoInclinacao = comando * fundo * visual * parada;
            float tempo = Pilotagem.TempoInclinacao(absTurn, _perfil.TempoInclinacaoLento, _perfil.TempoInclinacaoRapido);

            // O retorno rápido só vale quando o piloto alivia a direção; o freio que tira inclinação disponível a levanta devagar (a frente escapa).
            float fundoLivre = Mathf.Min(Pilotagem.InclinacaoMaxima(_perfil.InclinacaoMaxima, _perfil.AceleracaoLateral, aderencia, 0f), _perfil.InclinacaoMaxima * Pilotagem.FatorInclinacaoRapida(absTurn) * 0.97f);
            float alvoLivre = comando * fundoLivre * visual * parada;

            if (Mathf.Abs(alvoLivre) < Mathf.Abs(_lean) - 0.5f || alvoLivre * _lean < 0f)
            {
                tempo *= _tempoRetornoFator;
            }

            // _lean guarda o sinal da direção (direita positiva); Lean (visual) inverte para o eixo Z.
            float inclinacao = _lean;
            Pilotagem.PassoCritico(ref inclinacao, ref _leanTaxa, alvoInclinacao, tempo, dt);
            _lean = inclinacao;

            // Derrapagem: se a inclinação passa da que o atrito ainda dá (freada forte inclinado, chão molhado), a frente escapa:
            // a moto vira menos que o pedido e a velocidade demora a seguir o rumo (linha alargada).
            float alvoDerrapagem = Pilotagem.AlvoDerrapagem(_lean, incMax) * Mathf.Clamp01((absTurn - 4f) / 4f);
            _derrapagem = Mathf.MoveTowards(_derrapagem, alvoDerrapagem, (alvoDerrapagem > _derrapagem ? 5f : 1.2f) * dt);

            // Guinada: mistura do guidão direto (baixa velocidade) com a curva coordenada g·tan(inclinação)/v.
            float yawGuidao = Pilotagem.TaxaGuinadaGuidao(comando, absTurn, _perfil.RaioMinimo, _perfil.GiroBaixa, aLatMax);
            float yawInclinado = Pilotagem.TaxaGuinada(_lean, absTurn);
            float yawRate = Mathf.Lerp(yawGuidao, yawInclinado, pesoInclinacao) * (1f - 0.5f * _derrapagem);

            // Primeiro gira (com a velocidade da física anterior), depois decompõe a velocidade no novo eixo:
            // com pouca aderência a parte lateral demora a sumir e a moto escorrega.
            _yaw += yawRate * (turnSpeed < 0f ? -1f : 1f) * dt;

            Vector3 forward = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
            Vector3 right = Quaternion.Euler(0f, _yaw, 0f) * Vector3.right;

            float forwardSpeed = Vector3.Dot(velocity, forward);
            float lateralSpeed = Vector3.Dot(velocity, right);
            float previousForward = forwardSpeed;
            float wetGrip = IsWet ? _freioMolhado : 1f;

            // Cortando giro: parado (menos de ~3 km/h), freio e acelerador juntos = embreagem puxada.
            // TODO: a perturbação do sossego (polícia por perto) poderia punir o giro cortado em excesso.
            bool clutch = free && gas > 0.05f && brake > 0.05f && Mathf.Abs(forwardSpeed) < 0.83f;

            if (clutch)
            {
                _throttle = gas;
                _transmissao.StepNeutro(gas, dt);
                _revving = true;
                forwardSpeed = Mathf.MoveTowards(forwardSpeed, 0f, _perfil.Freio * dt);
            }
            else if (brake > 0.05f)
            {
                _revving = false;
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
                if (_revving)
                {
                    // Soltou o freio girando alto: arrancada com a roda patinando um pouco.
                    _revving = false;
                    _transmissao.IniciarArrancada();
                    _launchTimer = 0.6f;
                }

                // Motor, freio-motor, arrasto e rolagem. Sem limitador artificial: a velocidade final sai do equilíbrio.
                _throttle = gas > 0.05f ? gas : 0f;
                float engine = _transmissao.Step(forwardSpeed, _throttle, dt);

                if (IsWet && engine > 0f)
                {
                    engine *= 0.8f;
                }

                if (_launchTimer > 0f)
                {
                    _launchTimer -= dt;

                    if (engine > 0f)
                    {
                        engine *= 1f + 0.3f * Mathf.Clamp01(_launchTimer / 0.6f);
                    }
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

            float grip = (IsWet ? _aderenciaLateralMolhada : _aderenciaLateralSeca) * (1f - _atritoDerrapando * _derrapagem);
            lateralSpeed *= Mathf.Exp(-grip * dt);

            if (_derrapagem > 0f && forwardSpeed > 0f)
            {
                forwardSpeed = Mathf.Max(0f, forwardSpeed - 2.5f * _derrapagem * dt);
            }

            Vector3 planar = forward * forwardSpeed + right * lateralSpeed;
            _body.linearVelocity = new Vector3(planar.x, velocity.y, planar.z);
            _body.MoveRotation(Quaternion.Euler(0f, _yaw, 0f));

            ForwardSpeed = forwardSpeed;

            if (_leanRoot != null)
            {
                _leanRoot.localRotation = Quaternion.Euler(0f, 0f, -_lean);
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
