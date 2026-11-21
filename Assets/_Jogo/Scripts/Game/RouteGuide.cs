using System.Collections.Generic;
using UnityEngine;

namespace Entregador.Game
{
    /// <summary>
    /// Linha do GPS no chão: do ponto da moto até o alvo, pelas ruas. Recalcula quando o jogador sai do caminho
    /// e vai encurtando a linha conforme ele avança.
    /// </summary>
    public sealed class RouteGuide : MonoBehaviour
    {
        [SerializeField] private RoadGraph _graph;
        [SerializeField] private LineRenderer _line;
        [SerializeField] private ArcadeBikeController _bike;
        [SerializeField] private float _lift = 0.35f;
        [SerializeField] private float _offRouteDistance = 14f;

        private readonly List<Vector3> _route = new List<Vector3>();
        private Vector3 _targetPosition;
        private bool _hasTarget;
        private int _recalculations;

        public bool HasTarget => _hasTarget;

        public IReadOnlyList<Vector3> Route => _route;

        /// <summary>Quantas vezes a rota foi calculada (útil em testes).</summary>
        public int Recalculations => _recalculations;

        /// <summary>Comprimento restante da rota, em metros.</summary>
        public float RemainingMeters { get; private set; }

        public void Configure(RoadGraph graph, LineRenderer line, ArcadeBikeController bike)
        {
            _graph = graph;
            _line = line;
            _bike = bike;
        }

        public void SetTarget(Vector3 position)
        {
            _targetPosition = position;
            _hasTarget = true;
            Recalculate();
        }

        public void ClearTarget()
        {
            _hasTarget = false;
            _route.Clear();
            RemainingMeters = 0f;

            if (_line != null)
            {
                _line.positionCount = 0;
            }
        }

        public void Recalculate()
        {
            if (!_hasTarget || _graph == null || _bike == null)
            {
                return;
            }

            _recalculations++;
            RemainingMeters = _graph.FindRoute(_bike.Position, _targetPosition, _route);
            Redraw();
        }

        private void Update()
        {
            if (!_hasTarget || _graph == null || _bike == null || _route.Count < 2)
            {
                return;
            }

            Vector3 position = _bike.Position;
            int segment = NearestSegment(position, out float offRoute);

            if (offRoute > _offRouteDistance)
            {
                Recalculate();

                return;
            }

            // Descarta os pontos que a moto já passou e começa a linha na moto.
            if (segment > 0)
            {
                _route.RemoveRange(0, segment);
            }

            _route[0] = position;
            RemainingMeters = 0f;

            for (int i = 1; i < _route.Count; i++)
            {
                RemainingMeters += Vector3.Distance(_route[i - 1], _route[i]);
            }

            Redraw();
        }

        private int NearestSegment(Vector3 position, out float distance)
        {
            int best = 0;
            distance = float.MaxValue;

            // Começa no segmento 1: o segmento 0 sempre nasce na moto e não diz se ela saiu da rua.
            for (int i = 1; i < _route.Count - 1; i++)
            {
                Vector3 a = _route[i];
                Vector3 b = _route[i + 1];
                Vector3 ab = b - a;
                float lengthSqr = ab.x * ab.x + ab.z * ab.z;
                float t = lengthSqr < 0.0001f ? 0f : Mathf.Clamp01(((position.x - a.x) * ab.x + (position.z - a.z) * ab.z) / lengthSqr);
                Vector3 closest = a + ab * t;
                float d = Mathf.Sqrt((closest.x - position.x) * (closest.x - position.x) + (closest.z - position.z) * (closest.z - position.z));

                if (d < distance)
                {
                    distance = d;
                    best = i;
                }
            }

            if (_route.Count <= 2)
            {
                distance = 0f;

                return 0;
            }

            // O trecho em que a moto está começa no ponto anterior ao mais próximo.
            return Mathf.Max(0, best - 1);
        }

        private void Redraw()
        {
            if (_line == null)
            {
                return;
            }

            _line.positionCount = _route.Count;

            for (int i = 0; i < _route.Count; i++)
            {
                Vector3 p = _route[i];
                _line.SetPosition(i, new Vector3(p.x, _lift, p.z));
            }
        }
    }
}
