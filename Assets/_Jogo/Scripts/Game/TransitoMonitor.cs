using System;
using Entregador.Core;
using UnityEngine;

namespace Entregador.Game
{
    /// <summary>
    /// Olha o que a moto está fazendo na rua: limite da via, contramão em rua de mão única, corredor de ônibus e excesso de velocidade.
    /// Nada disso vira multa por si só: só vira infração se a polícia vê (flagrante). Quando uma viatura de ronda enxerga a infração a
    /// menos de PoliceSightMeters, com linha de visão livre, dispara FlagranteVisto. Quem anda na contramão também corre o risco de
    /// dar de cara com um carro vindo de frente (ver SpawnOncoming).
    ///
    /// Semáforos: o avanço do sinal vermelho é detectado pelo SemaforoFiscal, que chama ReportarInfracao (a polícia só autua se vê).
    /// Contramão em avenida de mão dupla (lado errado) também não: só ruas de sentido único.
    /// </summary>
    public sealed class TransitoMonitor : MonoBehaviour
    {
        private const float SampleInterval = 0.1f;
        private const float CommitSeconds = 1.0f;

        /// <summary>Depois de um flagrante (ou de uma abordagem) a polícia espera este tempo (s) antes de autuar de novo.</summary>
        public const float FlagranteCooldownSeconds = 60f;

        [SerializeField] private ArcadeBikeController _bike;
        [SerializeField] private CityMap _map;
        [SerializeField] private GameTuning _tuning;

        private float _timer;
        private float _wrongWayTime;
        private float _busLaneTime;
        private float _speedingTime;
        private float _cooldownUntil;
        private float _oncomingTimer;

        /// <summary>A polícia viu uma infração. O GameFlow abre a abordagem.</summary>
        public event Action<Infracao, PoliceUnit> FlagranteVisto;

        /// <summary>Só mede (e dá flagrante) com a moto numa corrida.</summary>
        public bool Active { get; set; }

        /// <summary>Limite de velocidade (km/h) da rua onde a moto está.</summary>
        public int LimitKmh { get; private set; } = RoadGraph.DefaultLimitKmh;

        public bool WrongWay { get; private set; }

        public bool InBusLane { get; private set; }

        public bool Speeding { get; private set; }

        public int OncomingSpawned { get; private set; }

        /// <summary>Infração que a polícia vê agora (se houver), ignorando se há uma viatura por perto. Para o HUD e os testes.</summary>
        public Infracao? CurrentInfraction { get; private set; }

        public void Configure(ArcadeBikeController bike, CityMap map, GameTuning tuning)
        {
            _bike = bike;
            _map = map;
            _tuning = tuning;
        }

        /// <summary>Depois de uma abordagem, a polícia espera um pouco antes de autuar de novo.</summary>
        public void Cooldown(float seconds)
        {
            _cooldownUntil = Time.time + seconds;
            _wrongWayTime = _busLaneTime = _speedingTime = 0f;
        }

        public void ResetState()
        {
            _wrongWayTime = _busLaneTime = _speedingTime = 0f;
            WrongWay = InBusLane = Speeding = false;
            CurrentInfraction = null;
        }

        private void FixedUpdate()
        {
            if (_bike == null || _map == null)
            {
                return;
            }

            _timer += Time.fixedDeltaTime;

            if (_timer < SampleInterval)
            {
                return;
            }

            float dt = _timer;
            _timer = 0f;
            Sample(dt);
        }

        /// <summary>Uma amostra da situação da moto (a cada 0,1 s). Público para os testes chamarem sem esperar.</summary>
        public void Sample(float dt)
        {
            Vector3 position = _bike.Position;
            Vector3 heading = _bike.Forward;
            float speedKmh = _bike.SpeedKmh;
            bool moving = speedKmh > 8f && _bike.ForwardSpeed > 0f;

            RoadGraph graph = _map.Graph;

            if (graph != null)
            {
                RoadGraph.RoadSample sample = graph.Sample(position, heading);
                LimitKmh = sample.LimitKmh;
                WrongWay = moving && sample.WrongWay;
            }

            InBusLane = moving && IsInBusLane(position, heading);
            float tolerance = _tuning != null ? _tuning.PoliceSpeedTolerance : 0.2f;
            Speeding = speedKmh > LimitKmh * (1f + tolerance);

            _wrongWayTime = WrongWay ? _wrongWayTime + dt : 0f;
            _busLaneTime = InBusLane ? _busLaneTime + dt : 0f;
            _speedingTime = Speeding ? _speedingTime + dt : 0f;

            CurrentInfraction = Flagrantes.Avaliar(
                _wrongWayTime >= CommitSeconds,
                _busLaneTime >= CommitSeconds,
                _speedingTime >= 0.5f ? speedKmh : 0f,
                LimitKmh,
                tolerance);

            if (!Active)
            {
                return;
            }

            RiskOfOncoming(dt);

            if (CurrentInfraction.HasValue && Time.time >= _cooldownUntil)
            {
                PoliceUnit watcher = FindWatcher(position);

                if (watcher != null)
                {
                    Cooldown(FlagranteCooldownSeconds);
                    FlagranteVisto?.Invoke(CurrentInfraction.Value, watcher);
                }
            }
        }

        /// <summary>
        /// Uma infração instantânea (avançar o sinal vermelho, vista pelo SemaforoFiscal) que só vale se uma viatura de ronda enxerga a moto.
        /// Respeita o tempo de espera depois de uma abordagem. Devolve true se a polícia viu e o flagrante foi disparado.
        /// </summary>
        public bool ReportarInfracao(Infracao infracao)
        {
            if (!Active || _bike == null || Time.time < _cooldownUntil)
            {
                return false;
            }

            PoliceUnit watcher = FindWatcher(_bike.Position);

            if (watcher == null)
            {
                return false;
            }

            Cooldown(FlagranteCooldownSeconds);
            FlagranteVisto?.Invoke(infracao, watcher);

            return true;
        }

        private bool IsInBusLane(Vector3 position, Vector3 heading)
        {
            foreach (BusLane lane in _map.BusLanes)
            {
                if (lane.DistanceTo(position) >= lane.HalfWidth)
                {
                    continue;
                }

                // Atravessar o corredor numa esquina não conta: só andar ao longo dele.
                Vector3 axis = lane.B - lane.A;
                axis.y = 0f;

                if (axis.sqrMagnitude > 0.01f && Mathf.Abs(Vector3.Dot(heading, axis.normalized)) > 0.8f)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Viatura de ronda que enxerga a moto (perto e sem obstáculo). A da blitz já abordou pela própria zona.</summary>
        private PoliceUnit FindWatcher(Vector3 position)
        {
            float range = _tuning != null ? _tuning.PoliceSightMeters : 40f;

            foreach (PoliceUnit unit in PoliceUnit.Active)
            {
                if (unit.Mode == PoliceMode.Patrulha && Time.time >= unit.NoFlagUntil && unit.CanSee(position, range))
                {
                    return unit;
                }
            }

            return null;
        }

        // ---------- Carro vindo de frente ----------

        /// <summary>Na contramão por alguns segundos há uma chance pequena de vir um carro de frente.</summary>
        private void RiskOfOncoming(float dt)
        {
            if (_wrongWayTime < 2.5f)
            {
                _oncomingTimer = 0f;

                return;
            }

            _oncomingTimer += dt;

            if (_oncomingTimer >= 4f)
            {
                _oncomingTimer = 0f;

                if (UnityEngine.Random.value < 0.35f)
                {
                    SpawnOncoming();
                }
            }
        }

        /// <summary>
        /// Faz aparecer, 70 m à frente da moto, um carro (cópia visual de um carro do trânsito) vindo na mão certa, ou seja, de frente.
        /// Só em trecho de sentido único contrário ao da moto. Devolve false se não achou lugar. Público para os testes.
        /// </summary>
        public bool SpawnOncoming()
        {
            RoadGraph graph = _map.Graph;
            TrafficLoop template = FindFirstObjectByType<TrafficLoop>();

            if (graph == null || template == null)
            {
                return false;
            }

            Vector3 heading = _bike.Forward;
            Vector3 ahead = _bike.Position + heading * 70f;
            int edge = graph.NearestEdge(ahead, out Vector3 snapped);

            if (edge < 0 || !graph.IsOneway(edge) || Vector3.Dot(graph.EdgeDirection(edge), -heading) < 0.8f
                || Vector3.Distance(snapped, ahead) > 6f)
            {
                return false;
            }

            Vector3 travel = graph.EdgeDirection(edge);
            var car = new GameObject("CarroDeFrente");
            car.transform.SetPositionAndRotation(new Vector3(snapped.x, 0.05f, snapped.z), Quaternion.LookRotation(travel, Vector3.up));

            Transform visual = template.transform.Find("Carro");

            if (visual == null && template.transform.childCount > 0)
            {
                visual = template.transform.GetChild(0);
            }

            if (visual != null)
            {
                Transform copy = Instantiate(visual, car.transform);
                copy.localPosition = Vector3.zero;
                copy.localRotation = Quaternion.identity;
            }

            BoxCollider source = template.GetComponent<BoxCollider>();
            BoxCollider box = car.AddComponent<BoxCollider>();
            box.center = source != null ? source.center : new Vector3(0f, 0.7f, 0f);
            box.size = source != null ? source.size : new Vector3(1.9f, 1.4f, 4.4f);
            car.AddComponent<Rigidbody>().isKinematic = true;
            car.AddComponent<OncomingCar>().Begin(travel, 9f, 14f);
            OncomingSpawned++;

            return true;
        }
    }

    /// <summary>Carro que anda reto numa direção por alguns segundos e some (o carro de frente de quem vai na contramão).</summary>
    public sealed class OncomingCar : MonoBehaviour
    {
        private Vector3 _direction;
        private float _speed;
        private float _until;
        private Rigidbody _body;

        public void Begin(Vector3 direction, float speed, float lifetime)
        {
            _direction = direction;
            _speed = speed;
            _until = Time.time + lifetime;
            _body = GetComponent<Rigidbody>();
        }

        private void FixedUpdate()
        {
            if (Time.time >= _until)
            {
                Destroy(gameObject);

                return;
            }

            _body.MovePosition(_body.position + _direction * (_speed * Time.fixedDeltaTime));
        }
    }
}
