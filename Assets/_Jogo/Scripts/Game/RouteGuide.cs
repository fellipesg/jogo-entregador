using System;
using System.Collections.Generic;
using Entregador.Core;
using UnityEngine;

namespace Entregador.Game
{
    /// <summary>A instrução que vem aí: manobra, distância até ela (m), rua e texto, para a tela e para a voz.</summary>
    public readonly struct InstrucaoGps
    {
        public InstrucaoGps(Instrucao instrucao, double distancia)
        {
            Instrucao = instrucao;
            Distancia = distancia;
        }

        public Instrucao Instrucao { get; }

        public bool Existe => Instrucao != null;

        public Manobra Manobra => Instrucao != null ? Instrucao.Manobra : Manobra.Nenhuma;

        /// <summary>Metros até a manobra, pela rota.</summary>
        public double Distancia { get; }

        public string Rua => Instrucao != null ? Instrucao.Rua : string.Empty;

        public string Texto => Instrucao != null ? Instrucao.Texto : string.Empty;

        /// <summary>Distância já formatada para a tela ("200 m").</summary>
        public string DistanciaTexto => Navegacao.DistanciaCurta(Distancia);
    }

    /// <summary>
    /// Linha do GPS no chão: do ponto da moto até o alvo, pelas ruas. Recalcula quando o jogador sai do caminho
    /// e vai encurtando a linha conforme ele avança.
    /// </summary>
    public sealed class RouteGuide : MonoBehaviour
    {
        [SerializeField] private RoadGraph _graph;
        [SerializeField] private LineRenderer _line;
        [SerializeField] private ArcadeBikeController _bike;
        [SerializeField] private float _lift = 0.14f;
        [Tooltip("Metros da linha, a partir da moto, que não são desenhados (não cobrem a visão da câmera).")]
        [SerializeField] private float _hideFirstMeters = 7f;
        [Tooltip("Distância (m) da rota a partir da qual conta como desvio.")]
        [SerializeField] private float _offRouteDistance = 20f;
        [Tooltip("Distância (m) que conta como desvio logo depois de passar uma manobra (virou na esquina errada).")]
        [SerializeField] private float _offRouteAfterTurn = 8f;
        [SerializeField] private float _offRouteSeconds = 1.5f;
        [SerializeField] private float _backwardsSeconds = 3f;
        [SerializeField] private float _fasterCheckInterval = 10f;
        [Tooltip("Se a distância que falta cresce tanto (m) em relação ao melhor ponto, o jogador está se afastando do destino (retorno, contramão, rua errada).")]
        [SerializeField] private float _movingAwayMeters = 15f;
        [Tooltip("Intervalo mínimo (s) entre dois recálculos automáticos (fora da rua, o Dijkstra não roda a todo quadro).")]
        [SerializeField] private float _minRecalcInterval = 0.75f;

        private readonly List<Vector3> _route = new List<Vector3>();
        private Vector3 _targetPosition;
        private bool _hasTarget;
        private int _recalculations;
        private float _lastRecalcTime = -100f;
        private float _bestRemaining;
        private bool _routeIsReal;
        private float _baseOffset;
        private Vector3 _segStart;
        private float _offTimer;
        private float _backTimer;
        private float _calmTimer;
        private float _afterManeuverUntil;
        private float _lastFasterCheck;
        private bool _deviationAnnounced;
        private readonly RoadGraph.RotaDetalhada _detail = new RoadGraph.RotaDetalhada();
        private readonly List<NavPonto> _navPoints = new List<NavPonto>();
        private List<Instrucao> _instrucoes = new List<Instrucao>();
        private int _indice;
        private readonly AnunciadorNavegacao _anunciador = new AnunciadorNavegacao();
        private float _lastWrongWayWarning = -100f;
        private float _nextWrongWayCheck;
        private SetasGps _setas;

        /// <summary>A voz ou a tela receberam um aviso (texto em português e a manobra a que se refere; Nenhuma para "Recalculando").</summary>
        public event Action<string, Manobra> Anunciou;

        /// <summary>Último aviso dado.</summary>
        public string UltimoAviso { get; private set; } = string.Empty;

        /// <summary>Todas as instruções da rota atual (a última é a Chegada).</summary>
        public IReadOnlyList<Instrucao> Instrucoes => _instrucoes;

        /// <summary>A próxima manobra (a que a moto ainda não passou) e a distância até ela.</summary>
        public InstrucaoGps ProximaInstrucao => InstrucaoAdiante(0);

        /// <summary>A manobra depois da próxima (para a dica pequena do cartão); distância é a contada desde a moto.</summary>
        public InstrucaoGps SegundaInstrucao => InstrucaoAdiante(1);

        public AnunciadorNavegacao Anunciador => _anunciador;

        private InstrucaoGps InstrucaoAdiante(int offset)
        {
            int i = _indice + offset;

            if (!_hasTarget || i < 0 || i >= _instrucoes.Count)
            {
                return default;
            }

            Instrucao inst = _instrucoes[i];

            return new InstrucaoGps(inst, Math.Max(0.0, RemainingMeters - inst.MetrosAteOFim));
        }

        private void Awake()
        {
            if (GetComponent<VozGps>() == null)
            {
                gameObject.AddComponent<VozGps>();
            }

            _setas = new SetasGps();
        }

        private void OnDestroy()
        {
            _setas?.Destruir();
        }

        public bool HasTarget => _hasTarget;

        public IReadOnlyList<Vector3> Route => _route;

        /// <summary>Quantas vezes a rota foi calculada (útil em testes).</summary>
        public int Recalculations => _recalculations;

        /// <summary>Quantas vezes a checagem periódica trocou a rota por uma bem mais curta.</summary>
        public int FasterRoutes { get; private set; }

        /// <summary>Tempo mínimo (s) entre recálculos por desvio.</summary>
        public float MinRecalcInterval => _minRecalcInterval;

        /// <summary>A rota atual segue as ruas (false = o grafo não achou caminho e a linha é reta até o destino).</summary>
        public bool RouteIsReal => _routeIsReal;

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
            _anunciador.Limpar();
            _deviationAnnounced = false;
            _offTimer = 0f;
            _backTimer = 0f;
            _lastFasterCheck = Time.unscaledTime;
            Recalculate();
        }

        public void ClearTarget()
        {
            _hasTarget = false;
            _route.Clear();
            _instrucoes = new List<Instrucao>();
            _indice = 0;
            _setas?.Esconder();
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
            _lastRecalcTime = Time.unscaledTime;
            _routeIsReal = _graph.TryFindRoute(_bike.Position, _targetPosition, _route, out float length, _detail, CurrentHeading());
            RemainingMeters = length;
            _bestRemaining = length;
            BuildInstructions();
            _segStart = _route.Count > 1 ? _route[1] : _route[0];
            _baseOffset = _route.Count > 1 ? Vector3.Distance(new Vector3(_route[0].x, 0f, _route[0].z), new Vector3(_route[1].x, 0f, _route[1].z)) : 0f;
            Redraw();
        }

        /// <summary>Rumo da moto (plano) quando ela anda de verdade; parada não tem rumo (o GPS pode mandar virar para qualquer lado).</summary>
        private Vector3? CurrentHeading()
        {
            if (_bike == null || _bike.Speed < 1.5f)
            {
                return null;
            }

            Vector3 forward = _bike.transform.forward;
            forward.y = 0f;

            return forward.sqrMagnitude < 0.01f ? (Vector3?)null : forward.normalized;
        }

        /// <summary>Monta as manobras da rota recém-calculada (a partir do ponto colado na rua; o ponto 0 é a própria moto).</summary>
        private void BuildInstructions()
        {
            _navPoints.Clear();
            bool detailed = _routeIsReal && _detail.Count == _route.Count;

            for (int i = detailed ? 1 : 0; i < _route.Count; i++)
            {
                Vector3 p = _route[i];
                _navPoints.Add(detailed
                    ? new NavPonto(p.x, p.z, _detail.RuaSaida[i], _detail.Cruzamento[i], _detail.Rotatoria[i])
                    : new NavPonto(p.x, p.z));
            }

            _instrucoes = Navegacao.Calcular(_navPoints);
            _indice = 0;
        }

        /// <summary>Avança a manobra passada, fala o aviso que cabe e atualiza as setas no chão.</summary>
        private void TickNavigation()
        {
            while (_indice < _instrucoes.Count - 1 && RemainingMeters - _instrucoes[_indice].MetrosAteOFim < -4.0)
            {
                _indice++;
                _afterManeuverUntil = Time.unscaledTime + 4f;
            }

            if (_indice >= _instrucoes.Count)
            {
                return;
            }

            Instrucao atual = _instrucoes[_indice];
            double distance = RemainingMeters - atual.MetrosAteOFim;
            Aviso aviso = _anunciador.Avaliar(atual, distance, _bike.Speed);

            if (aviso != null)
            {
                Announce(aviso.Texto, aviso.Manobra);
            }

            _setas?.Atualizar(atual, distance, _line != null ? _line.sharedMaterial : null);

            if (Time.unscaledTime >= _nextWrongWayCheck)
            {
                _nextWrongWayCheck = Time.unscaledTime + 0.5f;

                if (Time.unscaledTime - _lastWrongWayWarning > 20f && _bike.Speed > 2f && _graph.Sample(_bike.Position, _bike.transform.forward).WrongWay)
                {
                    _lastWrongWayWarning = Time.unscaledTime;
                    Announce("Você está na contramão.", Manobra.Nenhuma);
                }
            }
        }

        private void Announce(string text, Manobra manobra)
        {
            UltimoAviso = text;
            Anunciou?.Invoke(text, manobra);
        }

        /// <summary>Recálculo por sair da rota ou se afastar do destino: avisa "Recalculando".</summary>
        private bool RecalculateAnnounced()
        {
            if (!RecalculateThrottled())
            {
                return false;
            }

            if (!_deviationAnnounced)
            {
                _deviationAnnounced = true;
                Announce("Recalculando rota.", Manobra.Nenhuma);
            }

            return true;
        }

        /// <summary>Recalcula já se passou o intervalo mínimo desde o último cálculo.</summary>
        private bool RecalculateThrottled()
        {
            if (Time.unscaledTime - _lastRecalcTime < _minRecalcInterval)
            {
                return false;
            }

            Recalculate();

            return true;
        }

        private void Update()
        {
            if (!_hasTarget || _graph == null || _bike == null)
            {
                return;
            }

            if (_route.Count < 2)
            {
                RecalculateThrottled();

                return;
            }

            float now = Time.unscaledTime;
            float dt = Time.unscaledDeltaTime;
            Vector3 position = _bike.Position;
            int segment = NearestSegment(position, out float offRoute);

            // Quem parte longe de qualquer rua (praça, estacionamento) já nasce "fora": o desvio conta só o que passa disso.
            if (_bike.Speed > 1f)
            {
                _baseOffset = Mathf.Min(_baseOffset, offRoute);
            }

            offRoute -= _baseOffset;

            // Desvio como no Waze: fora da rota por mais de 1,5 s, ou logo depois de uma manobra (outra esquina) por 0,8 s.
            bool afterManeuver = now < _afterManeuverUntil;
            _offTimer = offRoute > (afterManeuver ? _offRouteAfterTurn : _offRouteDistance) ? _offTimer + dt : 0f;

            // Andando contra o sentido da rota (retorno, contramão, rua errada) por mais de 3 s.
            bool backwards = false;

            if (_bike.Speed > 2f && _route.Count > segment + 1)
            {
                Vector3 along = _route[Mathf.Min(segment + 1, _route.Count - 1)] - position;
                along.y = 0f;
                Vector3 forward = _bike.transform.forward;
                forward.y = 0f;
                backwards = along.sqrMagnitude > 25f && Vector3.Dot(forward.normalized, along.normalized) < -0.3f;
            }

            _backTimer = backwards || RemainingMeters > _bestRemaining + _movingAwayMeters ? _backTimer + dt : 0f;

            if (_offTimer > (afterManeuver ? 0.8f : _offRouteSeconds) || _backTimer > _backwardsSeconds)
            {
                if (RecalculateAnnounced())
                {
                    _offTimer = 0f;
                    _backTimer = 0f;

                    return;
                }
            }

            if (_offTimer <= 0f && _backTimer <= 0f)
            {
                _calmTimer += dt;

                if (_calmTimer > 3f)
                {
                    _deviationAnnounced = false;
                }
            }
            else
            {
                _calmTimer = 0f;
            }

            // Fora da rota (esperando o recálculo) a linha antiga fica como está: cortar pontos agora a encolheria até o destino.
            if (offRoute > (afterManeuver ? _offRouteAfterTurn : _offRouteDistance))
            {
                return;
            }

            // Descarta os pontos que a moto já passou e começa a linha na moto.
            if (segment > 0)
            {
                _segStart = _route[segment];
                _route.RemoveRange(0, segment);
            }

            _route[0] = position;
            RemainingMeters = 0f;

            for (int i = 1; i < _route.Count; i++)
            {
                RemainingMeters += Vector3.Distance(_route[i - 1], _route[i]);
            }

            _bestRemaining = Mathf.Min(_bestRemaining, RemainingMeters);

            // De tempos em tempos (10 s) vê se, de onde a moto está agora, existe um caminho bem mais curto.
            if (now - _lastFasterCheck >= _fasterCheckInterval)
            {
                _lastFasterCheck = now;
                TryFasterRoute();
            }

            Redraw();
            TickNavigation();
        }

        /// <summary>Procura uma rota claramente melhor (15% e 60 m a menos) a partir da posição e do rumo atuais e troca em silêncio.</summary>
        private void TryFasterRoute()
        {
            var candidate = new List<Vector3>();
            var detail = new RoadGraph.RotaDetalhada();
            bool real = _graph.TryFindRoute(_bike.Position, _targetPosition, candidate, out float length, detail, CurrentHeading());

            if (!real || length > RemainingMeters * 0.85f || RemainingMeters - length < 60f)
            {
                return;
            }

            _route.Clear();
            _route.AddRange(candidate);
            _detail.Clear();

            for (int i = 0; i < detail.Count; i++)
            {
                _detail.Add(detail.RuaSaida[i], detail.Cruzamento[i], detail.Rotatoria[i]);
            }

            _routeIsReal = true;
            _segStart = _route.Count > 1 ? _route[1] : _route[0];
            _recalculations++;
            FasterRoutes++;
            RemainingMeters = length;
            _bestRemaining = length;
            BuildInstructions();
            _anunciador.Limpar();
        }

        /// <summary>
        /// Segmento da rota em que a moto está e a distância até a rota. 0 é o trecho atual (do último vértice que a moto passou, _segStart,
        /// até a rota[1]); i maior que 0 é o trecho rota[i] até rota[i+1]. (A rota[0] é sempre a própria moto: o trecho dela com a rota[1]
        /// não diz se a moto saiu da rua.)
        /// </summary>
        private int NearestSegment(Vector3 position, out float distance)
        {
            int best = 0;
            distance = SegmentDistance(_segStart, _route[1], position);

            for (int i = 1; i < _route.Count - 1; i++)
            {
                float d = SegmentDistance(_route[i], _route[i + 1], position);

                if (d < distance)
                {
                    distance = d;
                    best = i;
                }
            }

            return best;
        }

        private static float SegmentDistance(Vector3 a, Vector3 b, Vector3 position)
        {
            Vector3 ab = b - a;
            float lengthSqr = ab.x * ab.x + ab.z * ab.z;
            float t = lengthSqr < 0.0001f ? 0f : Mathf.Clamp01(((position.x - a.x) * ab.x + (position.z - a.z) * ab.z) / lengthSqr);
            Vector3 closest = a + ab * t;

            return Mathf.Sqrt((closest.x - position.x) * (closest.x - position.x) + (closest.z - position.z) * (closest.z - position.z));
        }

        private readonly List<Vector3> _drawn = new List<Vector3>();

        private void Redraw()
        {
            if (_line == null)
            {
                return;
            }

            // Pula os primeiros metros: a linha começa à frente da moto, como uma fita estreita no asfalto.
            _drawn.Clear();
            float toSkip = _hideFirstMeters;

            for (int i = 0; i + 1 < _route.Count; i++)
            {
                Vector3 a = _route[i];
                Vector3 b = _route[i + 1];
                float length = Vector3.Distance(a, b);

                if (_drawn.Count == 0)
                {
                    if (length <= toSkip)
                    {
                        toSkip -= length;

                        continue;
                    }

                    _drawn.Add(Vector3.Lerp(a, b, toSkip / length));
                }

                _drawn.Add(b);
            }

            _line.positionCount = _drawn.Count < 2 ? 0 : _drawn.Count;

            for (int i = 0; i < _line.positionCount; i++)
            {
                Vector3 p = _drawn[i];
                _line.SetPosition(i, new Vector3(p.x, _lift, p.z));
            }
        }
    }
}
