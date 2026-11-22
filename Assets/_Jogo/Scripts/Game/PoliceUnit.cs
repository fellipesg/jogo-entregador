using System.Collections.Generic;
using UnityEngine;

namespace Entregador.Game
{
    public enum PoliceMode
    {
        /// <summary>Parada na blitz, com a barra de luz piscando.</summary>
        Parada,

        /// <summary>Rondando um circuito de ruas, luzes apagadas.</summary>
        Patrulha,

        /// <summary>Atrás da moto, com sirene (luzes piscando).</summary>
        Perseguicao,

        /// <summary>Fora de serviço (some da cena).</summary>
        Inativa,
    }

    /// <summary>
    /// Viatura da polícia: fica parada numa blitz, ronda um circuito de ruas ou persegue a moto pelo grafo de ruas.
    /// A perseguição é só apresentação: quem decide se a moto escapa é a GameSession (o sorteio de Fugir); a viatura recebe esse
    /// resultado (willCatch) e se comporta de acordo: se vai alcançar, acelera até a moto; se não, mantém distância.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class PoliceUnit : MonoBehaviour
    {
        private static readonly List<PoliceUnit> All = new List<PoliceUnit>();

        [SerializeField] private Renderer[] _redLamps = new Renderer[0];
        [SerializeField] private Renderer[] _blueLamps = new Renderer[0];
        [SerializeField] private Light _lightRed;
        [SerializeField] private Light _lightBlue;
        [SerializeField] private float _patrolSpeed = 8f;
        [SerializeField] private float _chaseSpeed = 24f;
        [SerializeField] private Vector3[] _waypoints = new Vector3[0];
        [SerializeField] private int _next;

        private Rigidbody _body;
        [SerializeField] private RoadGraph _graph;
        private Transform _target;
        private ArcadeBikeController _bike;
        private bool _willCatch;
        private bool _lightsOn;
        private PoliceMode _mode = PoliceMode.Inativa;
        private Vector3 _homePosition;
        private Quaternion _homeRotation;
        private readonly List<Vector3> _route = new List<Vector3>();
        private float _routeTimer;
        private float _yaw;
        private float _speed;
        private PoliceMode _modeBeforeChase;
        private ArcadeBikeController _watchedBike;
        private float _watchedAt = -10f;
        private bool _braking;

        /// <summary>Distância (m) que a viatura mantém atrás da moto numa abordagem (centro a centro).</summary>
        public const float StandoffMeters = 7f;

        /// <summary>Menor distância (m) entre a viatura e a moto, em qualquer modo: a viatura nunca avança para dentro disso.</summary>
        public const float MinBikeDistance = 4.6f;

        /// <summary>A viatura chegou no ponto de parada atrás da moto (ou já está parada perto dela).</summary>
        public bool ReachedStandoff => _target != null && GapToTarget <= StandoffMeters + 1.5f;

        /// <summary>Todas as viaturas ativas (a monitoria de trânsito procura quem está vendo a moto).</summary>
        public static IReadOnlyList<PoliceUnit> Active => All;

        public PoliceMode Mode => _mode;

        public bool LightsOn => _lightsOn;

        /// <summary>A sirene só soa em perseguição ativa (blitz tem luz mas não tem sirene; parada atrás da moto também cala).</summary>
        public bool SirenActive => _mode == PoliceMode.Perseguicao;

        /// <summary>A viatura tem circuito de ronda (é a patrulha, não a de uma blitz).</summary>
        public bool HasPatrolRoute => _waypoints != null && _waypoints.Length >= 2;

        public float Speed => _speed;

        /// <summary>Distância (plano, m) até a moto da perseguição; infinito se não há perseguição.</summary>
        public float GapToTarget => _target == null ? float.MaxValue : Flat(_target.position - transform.position).magnitude;

        public void Configure(Renderer[] redLamps, Renderer[] blueLamps, Light lightRed, Light lightBlue, RoadGraph graph)
        {
            _redLamps = redLamps;
            _blueLamps = blueLamps;
            _lightRed = lightRed;
            _lightBlue = lightBlue;
            _graph = graph;
        }

        /// <summary>Circuito da ronda: pontos de ruas, em volta fechada.</summary>
        public void SetPatrol(Vector3[] waypoints, float speed, int startIndex)
        {
            _waypoints = waypoints;
            _patrolSpeed = speed;
            _next = waypoints.Length == 0 ? 0 : (startIndex + 1) % waypoints.Length;
        }

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _body.isKinematic = true;
            _body.interpolation = RigidbodyInterpolation.Interpolate;
            _homePosition = transform.position;
            _homeRotation = transform.rotation;
            _yaw = transform.eulerAngles.y;
            SetLights(_lightsOn);
        }

        private void OnEnable()
        {
            if (!All.Contains(this))
            {
                All.Add(this);
            }
        }

        private void OnDisable()
        {
            All.Remove(this);
        }

        // ---------- Modos ----------

        /// <summary>Volta para a pose original (a de quando a cena foi gerada) e fica parada com as luzes piscando.</summary>
        public void BeginCheckpoint()
        {
            gameObject.SetActive(true);
            _target = null;
            _bike = null;
            TeleportTo(_homePosition, _homeRotation);
            _mode = PoliceMode.Parada;
            _braking = false;
            _speed = 0f;
            SetLights(true);
        }

        /// <summary>Começa a rondar o circuito configurado, de luzes apagadas.</summary>
        public void BeginPatrol()
        {
            gameObject.SetActive(true);
            _target = null;
            _bike = null;
            _mode = _waypoints.Length >= 2 ? PoliceMode.Patrulha : PoliceMode.Parada;
            SetLights(false);
        }

        /// <summary>Liga a sirene e vai atrás da moto. willCatch: o resultado já sorteado (alcança ou não).</summary>
        public void StartChase(ArcadeBikeController bike, bool willCatch)
        {
            gameObject.SetActive(true);

            if (_mode != PoliceMode.Perseguicao)
            {
                _modeBeforeChase = _mode;
            }

            _bike = bike;
            _target = bike.transform;
            _willCatch = willCatch;
            _mode = PoliceMode.Perseguicao;
            _braking = false;
            _routeTimer = 0f;
            SetLights(true);
        }

        /// <summary>Muda o resultado da perseguição em andamento (a escolha de Fugir sai depois que a viatura já veio atrás).</summary>
        public void SetWillCatch(bool willCatch)
        {
            _willCatch = willCatch;
        }

        /// <summary>Acaba o serviço: a viatura de ronda volta a rondar; a da blitz some.</summary>
        public void Dismiss()
        {
            _target = null;
            _bike = null;
            _braking = false;
            _speed = 0f;

            if (_modeBeforeChase == PoliceMode.Patrulha && _waypoints.Length >= 2)
            {
                _mode = PoliceMode.Patrulha;
                SetLights(false);

                return;
            }

            _mode = PoliceMode.Inativa;
            SetLights(false);
            gameObject.SetActive(false);
        }

        /// <summary>A viatura alcançou a moto e para junto dela (apresentação do "pego").</summary>
        public void HaltBehind()
        {
            _mode = PoliceMode.Parada;
            _braking = true;
        }

        private void TeleportTo(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);

            if (_body != null)
            {
                _body.position = position;
                _body.rotation = rotation;
            }

            _yaw = rotation.eulerAngles.y;
        }

        // ---------- Visão ----------

        /// <summary>A viatura vê o ponto: perto o bastante e sem prédio ou carro no meio (a própria moto não bloqueia).</summary>
        public bool CanSee(Vector3 point, float maxDistance)
        {
            Vector3 eye = transform.position + Vector3.up * 1.6f;
            Vector3 flat = Flat(point - transform.position);

            if (flat.magnitude > maxDistance)
            {
                return false;
            }

            Vector3 target = point + Vector3.up * 1.0f;

            if (Physics.Linecast(eye, target, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore))
            {
                Rigidbody body = hit.collider.attachedRigidbody;

                if (body != null && body.GetComponent<ArcadeBikeController>() != null)
                {
                    return true;
                }

                // Acerta a própria viatura ou outro carro: esses não escondem a moto de verdade (a viatura olha pelo para-brisa).
                return hit.collider.GetComponentInParent<PoliceUnit>() == this;
            }

            return true;
        }

        // ---------- Movimento ----------

        private void Update()
        {
            if (_lightsOn)
            {
                bool red = Mathf.FloorToInt(Time.time * 6f) % 2 == 0;
                SetLamps(_redLamps, red);
                SetLamps(_blueLamps, !red);

                if (_lightRed != null)
                {
                    _lightRed.enabled = red;
                }

                if (_lightBlue != null)
                {
                    _lightBlue.enabled = !red;
                }
            }
        }

        private void FixedUpdate()
        {
            if (_mode == PoliceMode.Patrulha)
            {
                Patrol(Time.fixedDeltaTime);
            }
            else if (_mode == PoliceMode.Perseguicao)
            {
                Chase(Time.fixedDeltaTime);
            }
            else if (_mode == PoliceMode.Parada && _braking)
            {
                // Freia até parar (sem teletransporte) e fica parada até a abordagem acabar.
                Drive(Vector3.zero, 0f, 0f, 16f, Time.fixedDeltaTime);

                if (_speed < 0.01f)
                {
                    _braking = false;
                }
            }
        }

        /// <summary>A moto do jogador, para a distância de segurança (a viatura de blitz e a de ronda também não a empurram).</summary>
        private ArcadeBikeController WatchedBike()
        {
            if (_bike != null)
            {
                return _bike;
            }

            if (_watchedBike == null && Time.time - _watchedAt > 1f)
            {
                _watchedAt = Time.time;
                _watchedBike = FindAnyObjectByType<ArcadeBikeController>();
            }

            return _watchedBike;
        }

        private void Patrol(float dt)
        {
            if (_waypoints.Length < 2)
            {
                return;
            }

            Vector3 toTarget = Flat(_waypoints[_next] - _body.position);

            if (toTarget.magnitude < 6f)
            {
                _next = (_next + 1) % _waypoints.Length;
                toTarget = Flat(_waypoints[_next] - _body.position);
            }

            // Outro carro logo à frente: espera (a ronda não atravessa o trânsito).
            float wanted = BlockedAhead() ? 0f : _patrolSpeed;
            Drive(toTarget, wanted, 130f, 10f, dt);
        }

        private bool BlockedAhead()
        {
            Vector3 forward = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
            ArcadeBikeController bike = WatchedBike();

            if (bike != null)
            {
                Vector3 toBike = Flat(bike.transform.position - _body.position);
                float alongBike = Vector3.Dot(toBike, forward);
                float lateralBike = Mathf.Abs(toBike.x * forward.z - toBike.z * forward.x);

                if (alongBike > 0f && alongBike < 12f && lateralBike < 2.6f)
                {
                    return true;
                }
            }

            foreach (TrafficLoop car in TrafficLoopRegistry())
            {
                Vector3 offset = Flat(car.transform.position - _body.position);
                float along = Vector3.Dot(offset, forward);
                float lateral = Mathf.Abs(offset.x * forward.z - offset.z * forward.x);

                if (along > 0f && along < 9f && lateral < 2.4f)
                {
                    return true;
                }
            }

            return false;
        }

        private TrafficLoop[] _cars;
        private float _carsAt = -10f;

        private TrafficLoop[] TrafficLoopRegistry()
        {
            if (_cars == null || Time.time - _carsAt > 2f)
            {
                _cars = FindObjectsByType<TrafficLoop>();
                _carsAt = Time.time;
            }

            return _cars;
        }

        private void Chase(float dt)
        {
            if (_target == null)
            {
                return;
            }

            Vector3 position = _body.position;
            Vector3 toBike = Flat(_target.position - position);
            float gap = toBike.magnitude;
            float playerSpeed = _bike != null ? _bike.Speed : 0f;

            // Rota pelas ruas até a moto (refeita a cada 0,4 s); sem grafo, vai em linha reta.
            _routeTimer -= dt;

            if (_routeTimer <= 0f && _graph != null)
            {
                _routeTimer = 0.4f;
                _graph.FindRoute(position, _target.position, _route);
            }

            Vector3 aim = _target.position;

            if (gap > 12f)
            {
                for (int i = 1; i < _route.Count; i++)
                {
                    if (Flat(_route[i] - position).magnitude > 4f)
                    {
                        aim = _route[i];

                        break;
                    }
                }
            }

            float wanted;

            if (_willCatch)
            {
                // Vai alcançar: some até a moto, casa a velocidade e para a StandoffMeters atrás dela, de lado para o meio-fio.
                Vector3 bikeForward = Flat(_target.forward).normalized;
                Vector3 bikeRight = new Vector3(bikeForward.z, 0f, -bikeForward.x);
                Vector3 stopPoint = _target.position - bikeForward * StandoffMeters + bikeRight * 1.8f;

                if (gap < 22f)
                {
                    aim = stopPoint;
                }

                float room = Mathf.Max(0f, gap - StandoffMeters);
                wanted = Mathf.Min(34f, playerSpeed + Mathf.Sqrt(2f * 11f * room));

                if (gap <= StandoffMeters + 0.5f && playerSpeed < 1.5f)
                {
                    wanted = 0f;
                }

                // Parada ao lado: a frente aponta um pouco para o meio-fio, como numa abordagem de verdade.
                if (_speed < 0.4f && gap < StandoffMeters + 3f)
                {
                    float bikeYaw = Mathf.Atan2(bikeForward.x, bikeForward.z) * Mathf.Rad2Deg;
                    _yaw = Mathf.MoveTowardsAngle(_yaw, bikeYaw + 14f, 40f * dt);
                    aim = _body.position;
                }
            }
            else
            {
                // Vai perder a moto: segue sem nunca chegar perto (fica 25 m ou mais atrás).
                wanted = gap < 25f ? 0f : Mathf.Min(_chaseSpeed, Mathf.Max(6f, playerSpeed * 0.9f));
            }

            Drive(Flat(aim - position), wanted, 160f, 18f, dt);
        }

        private void Drive(Vector3 toAim, float wantedSpeed, float turnRate, float acceleration, float dt)
        {
            if (toAim.sqrMagnitude > 0.01f)
            {
                float desired = Mathf.Atan2(toAim.x, toAim.z) * Mathf.Rad2Deg;
                _yaw = Mathf.MoveTowardsAngle(_yaw, desired, turnRate * dt);
            }

            _speed = Mathf.MoveTowards(_speed, wantedSpeed, acceleration * dt);
            Vector3 forward = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
            Vector3 next = _body.position + forward * (_speed * dt);

            // Nunca encosta na moto: se o passo a deixaria mais perto que MinBikeDistance, a viatura não anda (nada de empurrar).
            ArcadeBikeController bike = WatchedBike();

            if (bike != null)
            {
                Vector3 bikeAt = bike.transform.position;
                float now = Flat(bikeAt - _body.position).magnitude;
                float after = Flat(bikeAt - next).magnitude;

                if (after < MinBikeDistance && after < now)
                {
                    next = _body.position;
                    _speed = 0f;
                }
            }

            _body.MovePosition(next);
            _body.MoveRotation(Quaternion.Euler(0f, _yaw, 0f));
        }

        // ---------- Luzes ----------

        public void SetLights(bool on)
        {
            _lightsOn = on;

            if (on)
            {
                return;
            }

            SetLamps(_redLamps, false);
            SetLamps(_blueLamps, false);

            if (_lightRed != null)
            {
                _lightRed.enabled = false;
            }

            if (_lightBlue != null)
            {
                _lightBlue.enabled = false;
            }
        }

        private static void SetLamps(Renderer[] lamps, bool on)
        {
            foreach (Renderer lamp in lamps)
            {
                if (lamp != null)
                {
                    lamp.enabled = on;
                }
            }
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;

            return v;
        }
    }
}
