using System;
using System.Collections.Generic;
using Entregador.Core;
using TMPro;
using UnityEngine;

namespace Entregador.Game
{
    /// <summary>
    /// Cria e cuida dos pontos de entregadores da cidade (criado em runtime pelo GameFlow, sem mexer na cena): escolhe os restaurantes mais
    /// movimentados, acha vagas de calçada para as motos, liga e desliga os pontos pela distância do jogador, define quantos entregadores
    /// ficam em cada um (hora do dia e chuva) e conduz a conversa: quando a moto para perto de um ponto aparece "Trocar ideia [E]";
    /// a roda de conversa (Core: ConversaEntregadores) mistura papo e informação verdadeira do jogo, gasta minutos de jogo e pode marcar
    /// um "?" no minimapa.
    /// </summary>
    public sealed class PontosDirector : MonoBehaviour
    {
        /// <summary>Distância (m) do ponto em que a moto parada oferece a conversa.</summary>
        public const float DistanciaConversa = 10f;

        private const int CamadaMinimapa = 30;
        private const float AfastamentoDoPonto = 11f;

        /// <summary>Hora do dia (0 a 24) vinda de um relógio de jogo, se existir (RelogioJogo liga isto). Null = sem relógio.</summary>
        public static Func<float> HoraDoDia;

        private GameFlow _flow;
        private RodaDeConversaUI _ui;
        private ConversaEntregadores _gerador;
        private System.Random _rng;
        private readonly List<PontoEntregadores> _pontos = new List<PontoEntregadores>();
        private readonly List<SpeedCamera> _radares = new List<SpeedCamera>();
        private readonly List<GameObject> _marcadores = new List<GameObject>();
        private WeatherLook _look;
        private PersonagensCatalogo _catalogo;
        private MotoVisual _molde;
        private Conversa _conversa;
        private PontoEntregadores _pontoAtual;
        private bool _trackerPausado;
        private int _reveladasAplicadas;
        private float _proxVerificacao;
        private float _proxAlvo;
        private int _diaDosMarcadores = -1;
        private bool _pronto;
        private bool _falhou;

        public bool Pronto => _pronto;

        public IReadOnlyList<PontoEntregadores> Pontos => _pontos;

        public RodaDeConversaUI Ui => _ui;

        public ConversaEntregadores Gerador => _gerador;

        public Conversa ConversaAtual => _conversa;

        public bool ConversaAberta => _conversa != null;

        /// <summary>Centro do ponto onde a conversa acontece (foco da câmera).</summary>
        public bool FocoConversa(out Vector3 foco)
        {
            foco = _pontoAtual != null ? _pontoAtual.Centro : default;

            return _pontoAtual != null;
        }

        /// <summary>Minutos de jogo gastos na última conversa encerrada (testes).</summary>
        public int UltimosMinutos { get; private set; }

        public int MarcadoresDeBlitz => _marcadores.Count;

        /// <summary>Cria o diretor como filho do GameFlow (chamado no Start dele).</summary>
        public static PontosDirector Criar(GameFlow flow)
        {
            var go = new GameObject("PontosDirector");
            go.transform.SetParent(flow.transform, false);
            var diretor = go.AddComponent<PontosDirector>();
            diretor._flow = flow;
            HoraDoDia = () => flow != null && flow.Relogio != null ? (float)flow.Relogio.Hora : -1f;

            return diretor;
        }

        private void OnDestroy()
        {
            HoraDoDia = null;
        }

        private void Start()
        {
            _rng = new System.Random(Environment.TickCount);
            _gerador = new ConversaEntregadores(new SeededRandom(Environment.TickCount ^ 0x5f3759df));
            _ui = new GameObject("RodaDeConversaUI").AddComponent<RodaDeConversaUI>();
            _ui.transform.SetParent(transform, false);
            _ui.Build();
        }

        // ---------- Montagem dos pontos ----------

        private void Tentar()
        {
            if (_falhou || _flow == null || _flow.Map == null || _flow.Map.Graph == null || _flow.Session == null || _flow.Bike == null)
            {
                return;
            }

            _molde = FindFirstObjectByType<MotoVisual>();

            if (_molde == null || _molde.Catalogo == null)
            {
                _falhou = _molde != null;

                return;
            }

            _catalogo = _molde.Catalogo;
            _look = FindFirstObjectByType<WeatherLook>();
            Personagem evitar = _flow.Perfil != null ? _flow.Perfil.Personagem : _molde.Personagem;

            foreach (SpeedCamera camera in FindObjectsByType<SpeedCamera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                _radares.Add(camera);
            }

            List<Place> escolhidos = EscolherRestaurantes(_flow.Map.Restaurants);
            int indice = 0;

            foreach (Place restaurante in escolhidos)
            {
                List<VagaPonto> vagas = CalcularVagas(restaurante, _flow.Map.Graph, PontoPopulacao.Maximo);

                if (vagas.Count < 2)
                {
                    continue;
                }

                var go = new GameObject("Ponto_" + restaurante.Name);
                go.transform.SetParent(transform, false);
                var ponto = go.AddComponent<PontoEntregadores>();
                ponto.Configurar("ponto" + indice, restaurante, vagas, _catalogo, _molde, evitar, _flow.Map.Graph, 9100 + indice * 37);
                _pontos.Add(ponto);
                indice++;
            }

            ConfigurarMinimapa();
            _pronto = true;
        }

        /// <summary>Os restaurantes com mais vizinhos por perto (aglomerados), do mais perto do Largo para o mais longe; até 5.</summary>
        public static List<Place> EscolherRestaurantes(Place[] restaurantes)
        {
            var todos = new List<Place>();

            foreach (Place place in restaurantes)
            {
                if (place != null)
                {
                    todos.Add(place);
                }
            }

            todos.Sort((a, b) =>
            {
                int va = Vizinhos(a, todos);
                int vb = Vizinhos(b, todos);

                return va != vb ? vb.CompareTo(va) : a.Position.sqrMagnitude.CompareTo(b.Position.sqrMagnitude);
            });

            return todos.Count > 5 ? todos.GetRange(0, 5) : todos;
        }

        private static int Vizinhos(Place place, List<Place> todos)
        {
            int n = 0;

            foreach (Place outro in todos)
            {
                if (outro != place && Vector3.Distance(place.Position, outro.Position) < 160f)
                {
                    n++;
                }
            }

            return n;
        }

        /// <summary>
        /// Vagas na calçada perto do restaurante (mas fora da zona do balcão): acha o meio-fio andando da rua para o balcão, e a cada 2,4 m
        /// ao longo da guia testa se cabe uma moto e uma pessoa (sem carro, árvore, poste ou parede).
        /// </summary>
        public static List<VagaPonto> CalcularVagas(Place restaurante, RoadGraph grafo, int maximo)
        {
            var resultado = new List<VagaPonto>();
            Vector3 balcao = restaurante.TemBalcao ? restaurante.Balcao : restaurante.Position;
            int aresta = grafo.NearestRoad(balcao, out Vector3 naRua);

            if (aresta < 0)
            {
                return resultado;
            }

            Vector3 paraRua = naRua - balcao;
            paraRua.y = 0f;
            float distancia = paraRua.magnitude;

            if (distancia < 0.5f)
            {
                return resultado;
            }

            paraRua /= distancia;
            Vector3 meioFio = AcharMeioFio(naRua, balcao, paraRua, distancia);
            Vector3 tangente = Vector3.Cross(Vector3.up, paraRua);
            Vector3 frente = Vector3.Cross(paraRua, Vector3.up);
            float yaw = Mathf.Atan2(frente.x, frente.z) * Mathf.Rad2Deg;
            var lados = new List<VagaPonto>[2] { new List<VagaPonto>(), new List<VagaPonto>() };

            for (int lado = 0; lado < 2; lado++)
            {
                float sinal = lado == 0 ? 1f : -1f;

                for (int k = 0; k < 10 && lados[lado].Count < maximo; k++)
                {
                    float deslocamento = sinal * (5f + k * 2.4f);
                    Vector3 ponto = meioFio + tangente * deslocamento - paraRua * 0.8f;
                    ponto = Chao(ponto);
                    Vector3 emPe = Chao(ponto - paraRua * 1.05f);
                    Vector3 achatado = ponto;
                    achatado.y = restaurante.Position.y;

                    if (Vector3.Distance(achatado, restaurante.Position) < AfastamentoDoPonto)
                    {
                        continue;
                    }

                    if (!Livre(ponto, emPe, yaw))
                    {
                        continue;
                    }

                    lados[lado].Add(new VagaPonto { Moto = ponto, MotoYaw = yaw, EmPe = emPe, ParaRua = paraRua, Rua = naRua });
                }
            }

            int melhor = lados[0].Count >= lados[1].Count ? 0 : 1;
            resultado.AddRange(lados[melhor]);

            // Se o lado melhor não enche, completa com o outro (as vagas mais perto do balcão primeiro).
            foreach (VagaPonto vaga in lados[1 - melhor])
            {
                if (resultado.Count >= maximo)
                {
                    break;
                }

                resultado.Add(vaga);
            }

            return resultado;
        }

        /// <summary>Anda da rua em direção ao balcão e acha onde o chão sobe (calçada, 15 cm). Sem achar, supõe 4,5 m do eixo.</summary>
        private static Vector3 AcharMeioFio(Vector3 naRua, Vector3 balcao, Vector3 paraRua, float distancia)
        {
            for (float s = 1.5f; s < distancia; s += 0.3f)
            {
                Vector3 p = naRua - paraRua * s;

                if (Physics.Raycast(new Vector3(p.x, 1.4f, p.z), Vector3.down, out RaycastHit hit, 4f, ~0, QueryTriggerInteraction.Ignore) && hit.point.y > 0.08f)
                {
                    return hit.point;
                }
            }

            Vector3 reserva = naRua - paraRua * Mathf.Min(4.5f, distancia * 0.8f);
            reserva.y = 0.15f;

            return reserva;
        }

        private static Vector3 Chao(Vector3 p)
        {
            if (Physics.Raycast(new Vector3(p.x, 1.4f, p.z), Vector3.down, out RaycastHit hit, 4f, ~0, QueryTriggerInteraction.Ignore))
            {
                return hit.point;
            }

            return new Vector3(p.x, 0.15f, p.z);
        }

        private static bool Livre(Vector3 moto, Vector3 emPe, float yaw)
        {
            Quaternion giro = Quaternion.Euler(0f, yaw, 0f);

            if (Physics.CheckBox(moto + Vector3.up * 0.8f, new Vector3(0.5f, 0.5f, 1.1f), giro, ~0, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            return !Physics.CheckSphere(emPe + Vector3.up * 1.0f, 0.38f, ~0, QueryTriggerInteraction.Ignore);
        }

        private void ConfigurarMinimapa()
        {
            MinimapView minimapa = FindFirstObjectByType<MinimapView>();

            if (minimapa != null)
            {
                Camera camera = minimapa.GetComponent<Camera>();
                camera.cullingMask |= 1 << CamadaMinimapa;
            }

            if (_flow != null)
            {
                Camera principal = Camera.main;

                if (principal != null)
                {
                    principal.cullingMask &= ~(1 << CamadaMinimapa);
                }
            }
        }

        // ---------- Quadro a quadro ----------

        private void Update()
        {
            if (!_pronto)
            {
                Tentar();

                return;
            }

            Vector3 jogador = _flow.Bike.Position;
            float dt = Time.deltaTime;
            float kmh = _flow.Bike.SpeedKmh;
            _proxVerificacao -= dt;
            _proxAlvo -= dt;

            if (_proxVerificacao <= 0f)
            {
                _proxVerificacao = 0.4f;
                Distancias(jogador);
            }

            if (_proxAlvo <= 0f)
            {
                _proxAlvo = 4f;
                AtualizarAlvos();
                LimparMarcadoresVelhos();
            }

            foreach (PontoEntregadores ponto in _pontos)
            {
                if (!ponto.gameObject.activeSelf)
                {
                    continue;
                }

                if (!ponto.Montado)
                {
                    ponto.MontarProximo();

                    continue;
                }

                ponto.Tick(dt, jogador, kmh);
            }

            if (_conversa != null)
            {
                TickConversa();
            }
            else
            {
                TickPrompt(jogador, kmh);
            }
        }

        private void Distancias(Vector3 jogador)
        {
            foreach (PontoEntregadores ponto in _pontos)
            {
                Vector3 centro = ponto.Centro;
                centro.y = jogador.y;
                float d = Vector3.Distance(centro, jogador);
                bool ativo = ponto.gameObject.activeSelf;

                if (!ativo && d < PontoEntregadores.DistanciaAtiva - 15f)
                {
                    ponto.gameObject.SetActive(true);
                }
                else if (ativo && d > PontoEntregadores.DistanciaAtiva + 15f && ponto != _pontoAtual)
                {
                    ponto.gameObject.SetActive(false);
                }
            }
        }

        private void AtualizarAlvos()
        {
            if (_flow.Session == null)
            {
                return;
            }

            float hora = Hora();
            bool chuva = _flow.Session.IsRainingToday;

            foreach (PontoEntregadores ponto in _pontos)
            {
                ponto.DefinirAlvo(PontoPopulacao.Quantidade(hora, chuva, ((_flow.Session.Day * 31 + ponto.Id.GetHashCode()) & 0xff) / 255.0));
            }
        }

        /// <summary>Hora do dia: do relógio do jogo, se existir; senão uma estimativa pelo visual (fim de tarde = jantar).</summary>
        public float Hora()
        {
            if (HoraDoDia != null)
            {
                float hora = HoraDoDia();

                if (hora >= 0f)
                {
                    return hora;
                }
            }

            if (_look != null)
            {
                return _look.Current == LookKind.Sunset ? 19.5f : (_look.Current == LookKind.Rain ? 15f : 12.5f);
            }

            return -1f;
        }

        // ---------- Botão e conversa ----------

        /// <summary>Pode abrir a roda agora (em movimento livre ou em corrida, sem outro diálogo, fora da zona do balcão).</summary>
        public bool PodeConversar()
        {
            if (_flow.Session == null || !_flow.IsRoaming || _flow.InCounterZone)
            {
                return false;
            }

            if (_flow.InDeliveryZone || _flow.State == FlowState.AtDoor)
            {
                return false;
            }

            if (_flow.Balcao != null && (_flow.Balcao.DialogueVisible || _flow.Balcao.PromptVisible))
            {
                return false;
            }

            return _flow.Ui == null || !_flow.Ui.ModalVisible;
        }

        /// <summary>O ponto montado mais perto da moto parada, ou null.</summary>
        public PontoEntregadores PontoPerto(Vector3 jogador, float kmh)
        {
            if (kmh >= GameFlow.StopKmh)
            {
                return null;
            }

            PontoEntregadores melhor = null;
            float menor = DistanciaConversa;

            foreach (PontoEntregadores ponto in _pontos)
            {
                if (!ponto.gameObject.activeSelf || !ponto.Montado || ponto.Quantidade == 0)
                {
                    continue;
                }

                float d = ponto.DistanciaMinima(jogador);

                if (d <= menor)
                {
                    menor = d;
                    melhor = ponto;
                }
            }

            return melhor;
        }

        private void TickPrompt(Vector3 jogador, float kmh)
        {
            PontoEntregadores ponto = PodeConversar() ? PontoPerto(jogador, kmh) : null;

            if (ponto == null)
            {
                if (_ui.PromptVisible)
                {
                    _ui.HidePrompt();
                }

                return;
            }

            if (!_ui.PromptVisible)
            {
                _ui.ShowPrompt("Trocar ideia  [E]", () => Abrir(PontoPerto(_flow.Bike.Position, _flow.Bike.SpeedKmh)));
            }

            if (RodaDeConversaUI.InteractPressed())
            {
                Abrir(ponto);
            }
        }

        /// <summary>Abre a roda de conversa do ponto (a moto trava, os entregadores olham para o jogador).</summary>
        public void Abrir(PontoEntregadores ponto)
        {
            if (ponto == null || _conversa != null || !PodeConversar())
            {
                return;
            }

            _pontoAtual = ponto;
            string[] nomes = ponto.Nomes();
            _conversa = _gerador.Iniciar(ponto.Id, _flow.Session.Day, nomes, MontarContexto());
            _reveladasAplicadas = 0;
            _flow.Bike.SetLocked(true);
            _trackerPausado = _flow.Tracker != null && _flow.Tracker.Running;

            if (_trackerPausado)
            {
                _flow.Tracker.Pause();
            }

            ponto.IniciarConversa();
            _ui.ShowDialogue(nomes);
            Atualizar();
        }

        private void Atualizar()
        {
            var opcoes = new List<string>();

            if (_conversa.Terminada)
            {
                opcoes.Add("Seguir viagem");
            }
            else
            {
                foreach (OpcaoConversa opcao in _conversa.Opcoes)
                {
                    opcoes.Add(opcao.Rotulo);
                }
            }

            _ui.Mostrar(_conversa.Falas, _conversa.Nomes, opcoes, Escolher);
            AplicarReveladas();

            // Quem fala por último recebe os olhares e o balão.
            for (int i = _conversa.Falas.Count - 1; i >= 0; i--)
            {
                FalaConversa fala = _conversa.Falas[i];

                if (fala.Autor >= 0)
                {
                    _pontoAtual.Falar(fala.Nome, fala.Texto.Length <= 46 ? fala.Texto : string.Empty);

                    break;
                }
            }
        }

        private void Escolher(int indice)
        {
            if (_conversa == null)
            {
                return;
            }

            if (_conversa.Terminada)
            {
                Fechar();

                return;
            }

            _conversa.Escolher(indice);
            Atualizar();
        }

        /// <summary>Fecha a roda: libera a moto e cobra os minutos de jogo (em corrida, atrasa o relógio da entrega).</summary>
        public void Fechar()
        {
            if (_conversa == null)
            {
                return;
            }

            UltimosMinutos = _conversa.MinutosGastos;

            // O tempo da conversa passa no relógio do jogo em qualquer estado; em corrida também atrasa a entrega (como as esperas do balcão).
            if (_flow.Relogio != null)
            {
                _flow.Relogio.AvancarMinutos(UltimosMinutos);
            }

            if (_flow.IsDriving && _flow.Tracker != null)
            {
                _flow.Tracker.AddDelaySeconds(UltimosMinutos * _flow.SegundosPorMinuto);
            }

            if (_trackerPausado && _flow.IsDriving)
            {
                _flow.Tracker.Resume();
            }

            _trackerPausado = false;

            if (_flow.IsRoaming)
            {
                _flow.Bike.SetLocked(false);
            }

            _pontoAtual?.EncerrarConversa();
            _pontoAtual = null;
            _conversa = null;
            _ui.HideDialogue();
        }

        private void TickConversa()
        {
            // Pausa, menu ou outro diálogo por cima: fecha (o fluxo dono do estado cuida do resto).
            if (_flow.Session == null || !_flow.IsRoaming)
            {
                _trackerPausado = false;
                _pontoAtual?.EncerrarConversa();
                _pontoAtual = null;
                _conversa = null;
                _ui.HideDialogue();

                return;
            }

            int tecla = RodaDeConversaUI.NumeroPressionado();

            if (tecla >= 0 && tecla < _ui.ChoiceLabels.Count)
            {
                Escolher(tecla);
            }
        }

        // ---------- O que os entregadores sabem ----------

        /// <summary>Monta o que a conversa pode contar, a partir da cena e da sessão (tudo verdadeiro; o rumor errado é decidido no Core).</summary>
        public ContextoFofoca MontarContexto()
        {
            var c = new ContextoFofoca();
            RoadGraph grafo = _flow.Map.Graph;
            var abertas = new HashSet<string>();

            foreach (BlitzSite site in _flow.BlitzSites)
            {
                if (site.IsOpen)
                {
                    abertas.Add(NomeDaRua(site.Position));
                }
            }

            foreach (BlitzSite site in _flow.BlitzSites)
            {
                string rua = NomeDaRua(site.Position);
                var local = new LocalFofoca(rua, Referencia(site.Position));

                if (site.IsOpen)
                {
                    c.BlitzHoje.Add(local);
                }
                else if (!abertas.Contains(rua))
                {
                    c.BlitzFalsas.Add(local);
                }
            }

            Vector3 jogador = _flow.Bike.Position;
            var perto = new List<SpeedCamera>();

            foreach (SpeedCamera camera in _radares)
            {
                if (camera != null && Vector3.Distance(Flat(camera.transform.position, jogador.y), jogador) < 380f)
                {
                    perto.Add(camera);
                }
            }

            perto.Sort((a, b) => (a.transform.position - jogador).sqrMagnitude.CompareTo((b.transform.position - jogador).sqrMagnitude));

            for (int i = 0; i < perto.Count && i < 4; i++)
            {
                c.Radares.Add(new RadarFofoca(NomeDaRua(perto[i].transform.position), perto[i].LimitKmh));
            }

            GameSession sessao = _flow.Session;
            c.ChoveHoje = sessao.IsRainingToday;
            c.ChoveAmanha = sessao.IsRainyDay(sessao.Day + 1);
            c.ChovendoAgora = _look != null && _look.Current == LookKind.Rain;

            // Verdade contra os sistemas: zona movimentada = zona quente do dia da agenda; restaurante lento = o do balcão.
            List<ZonaQuente> zonas = _flow.ZonasDoDia();

            if (zonas.Count > 0)
            {
                c.ZonaMovimentada = "lá na região de " + zonas[sessao.Day % zonas.Count].Nome;
            }

            string lento = _flow.RestauranteLentoDoDia(out int minutos);

            if (lento != null)
            {
                c.RestauranteLento = lento;
                c.MinutosDeEspera = minutos;
            }

            return c;
        }

        private string NomeDaRua(Vector3 posicao)
        {
            RoadGraph grafo = _flow.Map.Graph;
            int aresta = grafo.NearestEdge(posicao, out _);
            string nome = aresta >= 0 ? grafo.EdgeName(aresta) : string.Empty;

            return string.IsNullOrEmpty(nome) ? "uma rua sem nome" : nome;
        }

        private string Referencia(Vector3 posicao)
        {
            ViewPoint largo = _flow.Map.FindViewPoint("largo");
            Vector3 origem = largo != null ? largo.Position : new Vector3(_flow.Map.MapCenter.x, 0f, _flow.Map.MapCenter.y);
            float dx = posicao.x - origem.x;
            float dz = posicao.z - origem.z;

            if (dx * dx + dz * dz < 140f * 140f)
            {
                return "perto do Largo";
            }

            if (Mathf.Abs(dx) > Mathf.Abs(dz))
            {
                return dx > 0f ? "pro lado leste do Largo" : "pro lado oeste do Largo";
            }

            return dz > 0f ? "pro lado norte do Largo" : "pro lado sul do Largo";
        }

        private static Vector3 Flat(Vector3 p, float y) => new Vector3(p.x, y, p.z);

        // ---------- "?" no minimapa ----------

        private void AplicarReveladas()
        {
            for (; _reveladasAplicadas < _conversa.Reveladas.Count; _reveladasAplicadas++)
            {
                InfoRevelada info = _conversa.Reveladas[_reveladasAplicadas];

                if (info.Tema != FofocaTema.Blitz || (string.IsNullOrEmpty(info.Local) && !info.Vaga))
                {
                    continue;
                }

                BlitzSite site = AcharSite(info);

                if (site != null)
                {
                    Marcar(site.Position, info.Vaga);
                }
            }
        }

        private BlitzSite AcharSite(InfoRevelada info)
        {
            BlitzSite escolhido = null;

            foreach (BlitzSite site in _flow.BlitzSites)
            {
                bool bate = info.Vaga ? site.IsOpen : NomeDaRua(site.Position) == info.Local;

                if (bate && (escolhido == null || site.IsOpen))
                {
                    escolhido = site;
                }
            }

            return escolhido;
        }

        private void Marcar(Vector3 posicao, bool aproximado)
        {
            if (aproximado)
            {
                posicao += new Vector3((float)(_rng.NextDouble() * 70.0 - 35.0), 0f, (float)(_rng.NextDouble() * 70.0 - 35.0));
            }

            _diaDosMarcadores = _flow.Session.Day;
            var go = new GameObject("MarcadorBlitz");
            go.transform.SetParent(transform, false);
            go.layer = CamadaMinimapa;
            go.transform.SetPositionAndRotation(new Vector3(posicao.x, 90f, posicao.z), Quaternion.Euler(90f, 0f, 0f));
            var texto = go.AddComponent<TextMeshPro>();
            texto.text = "?";
            texto.fontSize = 140f;
            texto.fontStyle = FontStyles.Bold;
            texto.alignment = TextAlignmentOptions.Center;
            texto.color = new Color(1f, 0.82f, 0.12f);
            texto.outlineWidth = 0.35f;
            texto.outlineColor = new Color32(20, 12, 4, 255);
            texto.rectTransform.sizeDelta = new Vector2(60f, 60f);
            _marcadores.Add(go);
        }

        private void LimparMarcadoresVelhos()
        {
            if (_marcadores.Count == 0 || _flow.Session == null || _flow.Session.Day == _diaDosMarcadores)
            {
                return;
            }

            foreach (GameObject marcador in _marcadores)
            {
                Destroy(marcador);
            }

            _marcadores.Clear();
        }
    }
}
