using Entregador.Core;
using UnityEngine;

namespace Entregador.Game
{
    /// <summary>Mede uma corrida: metros rodados, tempo, estado da comida, dano na moto e foto. Monta o RunReport.</summary>
    public sealed class DeliveryRunTracker : MonoBehaviour
    {
        [SerializeField] private ArcadeBikeController _bike;

        private readonly RunReport _radar = new RunReport();
        private Vector3 _lastPosition;
        private bool _hasLast;

        public bool Running { get; private set; }

        /// <summary>Metros de mundo percorridos desde o começo da corrida.</summary>
        public float MetersDriven { get; private set; }

        public float ElapsedSeconds { get; private set; }

        public float ExpectedSeconds { get; private set; }

        /// <summary>Comprimento planejado da corrida (moto até restaurante até cliente), em metros.</summary>
        public float PlannedMeters { get; private set; }

        /// <summary>Estado da comida: 1 perfeita, 0 estragada.</summary>
        public float FoodCondition { get; private set; } = 1f;

        public float DamageAdded { get; private set; }

        public bool PhotoTaken { get; set; }

        public int BumpsOrHits { get; private set; }

        /// <summary>Infrações que radares fixos capturaram (o jogador não é avisado; a multa chega dias depois, pelo Core).</summary>
        public int RadarCaptures => _radar.Capturas.Count;

        /// <summary>Maior velocidade (km/h) da corrida, para o relatório.</summary>
        public float TopSpeedKmh { get; private set; }

        public float Progress => PlannedMeters <= 0f ? 0f : Mathf.Clamp01(MetersDriven / PlannedMeters);

        public void Configure(ArcadeBikeController bike)
        {
            _bike = bike;
        }

        private void OnEnable()
        {
            if (_bike == null)
            {
                return;
            }

            _bike.FoodShock += OnFoodShock;
            _bike.DamageTaken += OnDamage;
            _bike.Teleported += OnTeleported;
        }

        private void OnDisable()
        {
            if (_bike == null)
            {
                return;
            }

            _bike.FoodShock -= OnFoodShock;
            _bike.DamageTaken -= OnDamage;
            _bike.Teleported -= OnTeleported;
        }

        /// <summary>Zera tudo e começa a contar.</summary>
        public void Begin(float expectedSeconds, float plannedMeters)
        {
            MetersDriven = 0f;
            ElapsedSeconds = 0f;
            ExpectedSeconds = Mathf.Max(1f, expectedSeconds);
            PlannedMeters = plannedMeters;
            FoodCondition = 1f;
            DamageAdded = 0f;
            PhotoTaken = false;
            BumpsOrHits = 0;
            TopSpeedKmh = 0f;
            _hasLast = false;
            Running = true;
        }

        /// <summary>Um radar mediu a moto. Só vale com a corrida em andamento. Devolve true se a velocidade gerou infração (com a tolerância do CTB).</summary>
        public bool RegisterRadar(double measuredKmh, double limitKmh)
        {
            return Running && _radar.AddRadar(measuredKmh, limitKmh);
        }

        /// <summary>Um fotossensor flagrou o avanço do sinal vermelho. Só vale com a corrida em andamento.</summary>
        public bool RegisterRedLight()
        {
            if (!Running)
            {
                return false;
            }

            _radar.AddAvancoSinal();

            return true;
        }

        /// <summary>Entrega as capturas à sessão (FinishRun) e zera a lista. Capturas de uma corrida cancelada ficam para a próxima.</summary>
        public void ClearRadar()
        {
            _radar.Capturas.Clear();
        }

        public void Pause() => Running = false;

        public void Resume()
        {
            Running = true;
            _hasLast = false;
        }

        public void Stop() => Running = false;

        /// <summary>Aumenta o tempo previsto (o app tolera parte da demora do restaurante ou do cliente).</summary>
        public void ExtendExpected(float seconds)
        {
            ExpectedSeconds += Mathf.Max(0f, seconds);
        }

        public void AddDelaySeconds(float seconds)
        {
            ElapsedSeconds += Mathf.Max(0f, seconds);
        }

        private void Update()
        {
            if (Running)
            {
                ElapsedSeconds += Time.deltaTime;
            }
        }

        private void FixedUpdate()
        {
            if (!Running || _bike == null)
            {
                return;
            }

            Vector3 position = _bike.Position;
            TopSpeedKmh = Mathf.Max(TopSpeedKmh, _bike.SpeedKmh);

            if (_hasLast)
            {
                Vector3 delta = position - _lastPosition;
                MetersDriven += Mathf.Sqrt(delta.x * delta.x + delta.z * delta.z);
            }

            _lastPosition = position;
            _hasLast = true;
        }

        private void OnTeleported()
        {
            _hasLast = false;
        }

        private void OnFoodShock(float amount)
        {
            if (!Running)
            {
                return;
            }

            if (amount > 0.02f)
            {
                BumpsOrHits++;
            }

            FoodCondition = Mathf.Clamp01(FoodCondition - amount);
        }

        private void OnDamage(float amount)
        {
            if (Running)
            {
                DamageAdded = Mathf.Clamp01(DamageAdded + amount);
            }
        }

        public RunReport BuildReport(float worldToGameKm)
        {
            var report = new RunReport
            {
                KmDriven = MetersDriven * worldToGameKm,
                TimeRatio = ElapsedSeconds / ExpectedSeconds,
                FoodCondition = FoodCondition,
                DamageAdded = DamageAdded,
                Fines = 0,
                PhotoTaken = PhotoTaken,
            };

            report.Capturas.AddRange(_radar.Capturas);

            return report;
        }
    }
}
