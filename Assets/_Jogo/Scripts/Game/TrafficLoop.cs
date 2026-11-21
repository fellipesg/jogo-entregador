using System.Collections.Generic;
using UnityEngine;

namespace Entregador.Game
{
    /// <summary>
    /// Carro de trânsito: dá voltas num circuito fechado de pontos (um laço de ruas reais) numa velocidade fixa.
    /// É cinemático; a moto que bate nele leva dano e derruba comida pelo mecanismo normal de colisão
    /// (ArcadeBikeController.OnCollisionEnter). Para quando a moto, ou outro carro, está logo à frente: carros
    /// do mesmo laço mantêm distância, e nos cruzamentos entre laços o de menor ordem passa primeiro.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class TrafficLoop : MonoBehaviour
    {
        [SerializeField] private Vector3[] _waypoints;
        [SerializeField] private float _speed = 8f;
        [SerializeField] private float _turnRate = 130f;
        [SerializeField] private float _acceleration = 12f;
        [SerializeField] private float _stopDistance = 11f;
        [SerializeField] private float _stopHalfAngle = 35f;
        [SerializeField] private float _reachDistance = 6f;
        [SerializeField] private Transform _player;

        private static readonly List<TrafficLoop> All = new List<TrafficLoop>();

        private Rigidbody _body;
        private int _order;
        [SerializeField] private int _next;
        private float _yaw;
        private float _currentSpeed;

        /// <summary>Velocidade atual em m/s (0 quando parado por causa da moto).</summary>
        public float CurrentSpeed => _currentSpeed;

        public Vector3[] Waypoints => _waypoints;

        public int NextIndex => _next;

        public bool Stopped { get; private set; }

        public float CruiseSpeed => _speed;

        /// <summary>Distância (m) até o próximo ponto da volta; os testes usam para evitar esquinas.</summary>
        public float DistanceToNextWaypoint => _waypoints == null || _waypoints.Length == 0 ? 0f : Vector3.Distance(transform.position, _waypoints[_next]);

        public void Configure(Vector3[] waypoints, float speed, Transform player, int startIndex)
        {
            _waypoints = waypoints;
            _speed = speed;
            _player = player;
            _next = waypoints.Length == 0 ? 0 : (startIndex + 1) % waypoints.Length;
        }

        private void OnEnable()
        {
            _order = All.Count;
            All.Add(this);
        }

        private void OnDisable()
        {
            All.Remove(this);
        }

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _body.isKinematic = true;
            _body.interpolation = RigidbodyInterpolation.Interpolate;
            _yaw = transform.eulerAngles.y;
            _currentSpeed = _speed;
        }

        private void FixedUpdate()
        {
            if (_waypoints == null || _waypoints.Length < 2)
            {
                return;
            }

            float dt = Time.fixedDeltaTime;
            Vector3 position = _body.position;
            Vector3 toTarget = _waypoints[_next] - position;
            toTarget.y = 0f;

            // Chegou (ou já passou do ponto): segue para o próximo. O raio de chegada é maior que o raio de curva
            // (velocidade / taxa de giro), senão o carro ficaria dando voltas em torno do ponto.
            Vector3 heading = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;

            if (toTarget.magnitude < _reachDistance || (Vector3.Dot(heading, toTarget) < 0f && toTarget.magnitude < 14f))
            {
                _next = (_next + 1) % _waypoints.Length;
                toTarget = _waypoints[_next] - position;
                toTarget.y = 0f;
            }

            float desiredYaw = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
            _yaw = Mathf.MoveTowardsAngle(_yaw, desiredYaw, _turnRate * dt);

            Vector3 forward = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
            Stopped = PlayerAhead(position, forward) || CarAhead(position, forward);
            _currentSpeed = Mathf.MoveTowards(_currentSpeed, Stopped ? 0f : _speed, _acceleration * dt);

            _body.MovePosition(position + forward * (_currentSpeed * dt));
            _body.MoveRotation(Quaternion.Euler(0f, _yaw, 0f));
        }

        /// <summary>
        /// Outro carro logo à frente na mesma faixa (a menos de 10 m, quase em cima da minha linha), ou um carro de ordem menor que
        /// cruza muito perto: esse passa primeiro, e assim dois carros nunca ficam esperando um pelo outro.
        /// </summary>
        private bool CarAhead(Vector3 position, Vector3 forward)
        {
            foreach (TrafficLoop other in All)
            {
                if (other == this)
                {
                    continue;
                }

                Vector3 offset = other.transform.position - position;
                offset.y = 0f;
                float along = Vector3.Dot(offset, forward);
                float lateral = Mathf.Abs(offset.x * forward.z - offset.z * forward.x);

                if (along > 0f && along < 10f && lateral < 2.2f)
                {
                    return true;
                }

                if (offset.magnitude < 6f && along > -1f && lateral < 4f && other._order < _order)
                {
                    return true;
                }
            }

            return false;
        }

        private bool PlayerAhead(Vector3 position, Vector3 forward)
        {
            if (_player == null)
            {
                return false;
            }

            Vector3 offset = _player.position - position;
            offset.y = 0f;
            float distance = offset.magnitude;

            if (distance > _stopDistance)
            {
                return false;
            }

            if (distance < 3.5f)
            {
                return true;
            }

            return Vector3.Angle(forward, offset) < _stopHalfAngle;
        }
    }
}
