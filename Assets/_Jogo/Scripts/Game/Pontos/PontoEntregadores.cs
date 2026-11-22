using System.Collections.Generic;
using Entregador.Core;
using UnityEngine;

namespace Entregador.Game
{
    /// <summary>Uma vaga do ponto: onde a moto estaciona (rente à guia, de lado) e onde o piloto fica em pé ao lado dela.</summary>
    public struct VagaPonto
    {
        public Vector3 Moto;
        public float MotoYaw;
        public Vector3 EmPe;
        public Vector3 ParaRua;
        public Vector3 Rua;
    }

    /// <summary>
    /// Um ponto de entregadores: de 3 a 6 entregadores com as motos estacionadas na calçada perto de um restaurante, esperando pedido e
    /// conversando. Cuida do que acontece ali (posturas, cabeças virando para quem fala, balão "Salve!" quando o jogador passa devagar,
    /// conversa solta, e um entregador que sai rodando enquanto outro chega). Só é montado quando o jogador chega perto e fica desligado
    /// longe (custo zero).
    /// </summary>
    public sealed class PontoEntregadores : MonoBehaviour
    {
        public const float DistanciaAtiva = 125f;
        public const float DistanciaViva = 45f;
        private const float DistanciaCumprimento = 15f;

        private static readonly Color[] Tons =
        {
            new Color(1f, 1f, 1f), new Color(0.9f, 0.86f, 0.82f), new Color(1.08f, 0.95f, 0.88f), new Color(0.8f, 0.82f, 0.9f),
            new Color(0.95f, 1.05f, 0.9f), new Color(0.72f, 0.7f, 0.72f), new Color(1.1f, 0.9f, 0.9f), new Color(0.88f, 0.94f, 1.05f),
        };

        private static readonly (string nome, Color cor)[] Apps =
        {
            ("Rangô", new Color(0.92f, 0.1f, 0.12f)), ("Entrega Já", new Color(0.98f, 0.55f, 0.08f)),
            ("PedeAí", new Color(0.18f, 0.5f, 0.95f)), ("Vapt", new Color(0.55f, 0.2f, 0.8f)),
        };

        private readonly List<VagaPonto> _vagas = new List<VagaPonto>();
        private readonly List<EntregadorNpc> _membros = new List<EntregadorNpc>();
        private readonly List<EntregadorNpc> _saindo = new List<EntregadorNpc>();
        private EntregadorNpc[] _ocupantes = new EntregadorNpc[0];
        private PersonagensCatalogo _catalogo;
        private MotoVisual _molde;
        private Personagem _evitar;
        private System.Random _rng;
        private BalaoMundo _balao;
        private RoadGraph _grafo;
        private Vector3 _centro;
        private float _cumprimentoEm;
        private float _conversaSolta;
        private float _proximoEvento;
        private float _olharAte;
        private float _olharMuda;
        private int _quemOlha = -1;
        private int _alvoOlhar = -1;
        private bool _conversando;
        private int _falando = -1;
        private bool _olharJogador;
        private int _alvoQuantidade;
        private int _frame;

        public string Id { get; private set; }

        public Place Restaurante { get; private set; }

        public bool Montado { get; private set; }

        public IReadOnlyList<VagaPonto> Vagas => _vagas;

        public IReadOnlyList<EntregadorNpc> Membros => _membros;

        public Vector3 Centro => _centro;

        public int Quantidade => _membros.Count;

        public BalaoMundo Balao => _balao;

        /// <summary>Quantos entregadores o ponto deve ter agora (hora do dia e chuva).</summary>
        public int AlvoQuantidade => _alvoQuantidade;

        /// <summary>Quantas pessoas as vagas comportam.</summary>
        public int Capacidade => _vagas.Count;

        public void Configurar(string id, Place restaurante, List<VagaPonto> vagas, PersonagensCatalogo catalogo, MotoVisual molde, Personagem evitar, RoadGraph grafo, int semente)
        {
            Id = id;
            Restaurante = restaurante;
            _vagas.Clear();
            _vagas.AddRange(vagas);
            _catalogo = catalogo;
            _molde = molde;
            _evitar = evitar;
            _grafo = grafo;
            _rng = new System.Random(semente);
            _ocupantes = new EntregadorNpc[_vagas.Count];
            _centro = Vector3.zero;

            foreach (VagaPonto vaga in _vagas)
            {
                _centro += vaga.Moto;
            }

            _centro = _vagas.Count > 0 ? _centro / _vagas.Count : restaurante.Position;
            _proximoEvento = 30f + (float)_rng.NextDouble() * 40f;
            _alvoQuantidade = Mathf.Min(4, _vagas.Count);
            gameObject.SetActive(false);
        }

        public void DefinirPersonagemDoJogador(Personagem personagem)
        {
            _evitar = personagem;
        }

        /// <summary>Define quantos devem estar no ponto. Se o ponto já está montado, o excesso vai embora e a falta chega (aos poucos).</summary>
        public void DefinirAlvo(int quantidade)
        {
            _alvoQuantidade = Mathf.Clamp(quantidade, Mathf.Min(2, _vagas.Count), _vagas.Count);
        }

        // ---------- Montagem ----------

        /// <summary>Cria os entregadores (um por vez, para não travar). Devolve quantos faltam.</summary>
        public bool MontarProximo()
        {
            if (Montado)
            {
                return false;
            }

            int ja = 0;

            foreach (EntregadorNpc npc in _ocupantes)
            {
                if (npc != null)
                {
                    ja++;
                }
            }

            if (ja >= _alvoQuantidade)
            {
                Montado = true;
                Preparar();

                return false;
            }

            int vaga = ProximaVagaLivre();
            bool sentado = _rng.NextDouble() < 0.45;

            // Garante pelo menos um sentado (os que saem rodando) quando o ponto tem 3 ou mais.
            if (ja == _alvoQuantidade - 1 && _alvoQuantidade >= 3 && !_membros.Exists(m => m.Sentado))
            {
                sentado = true;
            }

            Criar(vaga, sentado, false);

            return true;
        }

        private void Preparar()
        {
            Alinhar();
        }

        private int ProximaVagaLivre()
        {
            for (int i = 0; i < _ocupantes.Length; i++)
            {
                if (_ocupantes[i] == null)
                {
                    return i;
                }
            }

            return -1;
        }

        private EntregadorEspec Sorteia(bool sentado)
        {
            Personagem personagem = Personagem.Jefinho;
            bool achou = false;

            // Primeiro tenta um personagem que ninguém do ponto usa e que não é o do jogador; depois só evita o do jogador.
            for (int tentativa = 0; tentativa < 60 && !achou; tentativa++)
            {
                personagem = (Personagem)_rng.Next(0, 5);
                achou = personagem != _evitar && (tentativa >= 30 || !_membros.Exists(m => m.Espec.Personagem == personagem));
            }

            var usados = new HashSet<string>();

            foreach (EntregadorNpc m in _membros)
            {
                usados.Add(m.Nome);
            }

            string nome;
            int guarda = 0;

            do
            {
                nome = FofocaCatalogo.NomesEntregadores[_rng.Next(FofocaCatalogo.NomesEntregadores.Length)];
            }
            while (usados.Contains(nome) && guarda++ < 40);

            (string appNome, Color appCor) = Apps[_rng.Next(Apps.Length)];

            return new EntregadorEspec
            {
                Nome = nome,
                Personagem = personagem,
                Tom = Tons[_rng.Next(Tons.Length)],
                Modelo = _rng.NextDouble() < 0.6 ? MotoModelo.Titan : MotoModelo.Twister,
                CorMoto = (MotoCor)_rng.Next(0, 4),
                CorApp = appCor,
                NomeApp = appNome,
                Sentado = sentado,
                CapaceteNaMao = !sentado && _rng.NextDouble() < 0.5,
                Fase = (float)_rng.NextDouble() * 20f,
            };
        }

        /// <summary>Cria o entregador na vaga (já parado) ou, se chegando, devolve sem posicionar (o chamador define a rota).</summary>
        private EntregadorNpc Criar(int vaga, bool sentado, bool chegando)
        {
            if (vaga < 0)
            {
                return null;
            }

            VagaPonto v = _vagas[vaga];
            EntregadorEspec espec = Sorteia(sentado);
            var go = new GameObject("Entregador_" + espec.Nome);
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(v.Moto + Vector3.up * -MotoVisual.AlturaDoChao, Quaternion.Euler(0f, v.MotoYaw, 0f));
            var npc = go.AddComponent<EntregadorNpc>();
            npc.Montar(_catalogo, _molde, espec);

            if (!sentado)
            {
                npc.PosicionarCorpo(v.EmPe, v.MotoYaw);
            }

            _ocupantes[vaga] = npc;
            _membros.Add(npc);

            return npc;
        }

        /// <summary>Vira cada entregador em pé para o grupo (o centro dos outros) e dá um olhar inicial.</summary>
        private void Alinhar()
        {
            for (int i = 0; i < _ocupantes.Length; i++)
            {
                EntregadorNpc npc = _ocupantes[i];

                if (npc == null || npc.Sentado || npc.Corpo == null)
                {
                    continue;
                }

                Vector3 soma = Vector3.zero;
                int n = 0;

                for (int j = 0; j < _ocupantes.Length; j++)
                {
                    if (j != i && _ocupantes[j] != null)
                    {
                        soma += _ocupantes[j].Sentado ? _ocupantes[j].transform.position : _ocupantes[j].Corpo.position;
                        n++;
                    }
                }

                if (n == 0)
                {
                    continue;
                }

                Vector3 alvo = soma / n - npc.Corpo.position;
                alvo.y = 0f;

                if (alvo.sqrMagnitude > 0.25f)
                {
                    float yaw = Mathf.Atan2(alvo.x, alvo.z) * Mathf.Rad2Deg + (float)(_rng.NextDouble() * 50.0 - 25.0);
                    npc.PosicionarCorpo(npc.Corpo.position, yaw);
                }
            }
        }

        // ---------- Conversa ----------

        /// <summary>O jogador abriu a roda: todos viram para ele; quem fala recebe os olhares.</summary>
        public void IniciarConversa()
        {
            _conversando = true;
            _falando = -1;
            _olharJogador = true;
        }

        public void EncerrarConversa()
        {
            _conversando = false;
            _falando = -1;
            _olharJogador = false;

            if (_balao != null)
            {
                _balao.Ocultar();
            }
        }

        /// <summary>Indice (no grupo atual, por nome) de quem fala: os outros olham para ele.</summary>
        public void Falar(string nome, string texto)
        {
            _falando = _membros.FindIndex(m => m.Nome == nome);
            _olharJogador = _falando < 0;

            if (_falando >= 0 && !string.IsNullOrEmpty(texto))
            {
                MostrarBalao(_membros[_falando], texto, 4f);
            }
        }

        /// <summary>Nomes dos entregadores do ponto (a ordem da conversa).</summary>
        public string[] Nomes()
        {
            var nomes = new string[_membros.Count];

            for (int i = 0; i < nomes.Length; i++)
            {
                nomes[i] = _membros[i].Nome;
            }

            return nomes;
        }

        private void MostrarBalao(EntregadorNpc npc, string texto, float segundos)
        {
            if (_balao == null)
            {
                _balao = new GameObject("Balao_" + Id).AddComponent<BalaoMundo>();
                _balao.transform.SetParent(transform, false);
            }

            _balao.Mostrar(npc, texto, segundos);
        }

        /// <summary>Distância (plano) do ponto mais próximo de algum entregador do ponto.</summary>
        public float DistanciaMinima(Vector3 de)
        {
            float melhor = float.MaxValue;

            foreach (EntregadorNpc npc in _membros)
            {
                Vector3 p = npc.Sentado || npc.Corpo == null ? npc.transform.position : npc.Corpo.position;
                p.y = de.y;
                melhor = Mathf.Min(melhor, Vector3.Distance(p, de));
            }

            return melhor;
        }

        // ---------- Quadro a quadro ----------

        /// <summary>Chamado pelo diretor a cada quadro enquanto o ponto está ativo.</summary>
        public void Tick(float dt, Vector3 jogador, float jogadorKmh)
        {
            _frame++;
            float distancia = DistanciaMinima(jogador);
            bool perto = distancia < DistanciaViva;

            if (!Montado)
            {
                return;
            }

            AtualizarOlhares(dt, jogador, perto);

            for (int i = 0; i < _membros.Count; i++)
            {
                EntregadorNpc npc = _membros[i];
                bool olhando = false;
                float yaw = 0f;

                if (_falando >= 0 && i != _falando && _falando < _membros.Count)
                {
                    olhando = true;
                    yaw = YawAte(npc, PontoDe(_membros[_falando]));
                }
                else if (_olharJogador || (_quemOlha == i && Time.time < _olharAte))
                {
                    olhando = true;
                    Vector3 alvo = _olharJogador || _alvoOlhar < 0 || _alvoOlhar >= _membros.Count ? jogador : PontoDe(_membros[_alvoOlhar]);
                    yaw = YawAte(npc, alvo);
                }

                npc.Viver(dt, perto, yaw, olhando, _conversando || (_quemOlha == i && _alvoOlhar >= 0));
            }

            if (!perto)
            {
                return;
            }

            Cumprimentar(jogador, jogadorKmh, distancia);
            ConversaSolta(dt, distancia);
            Eventos(dt, jogador);
        }

        private static Vector3 PontoDe(EntregadorNpc npc) => npc.Sentado || npc.Corpo == null ? npc.transform.position : npc.Corpo.position;

        /// <summary>Giro (graus, -180..180) que o entregador precisa para olhar o ponto, relativo à frente do corpo (ou da moto).</summary>
        private static float YawAte(EntregadorNpc npc, Vector3 ponto)
        {
            Transform frente = npc.Sentado || npc.Corpo == null ? npc.transform : npc.Corpo;
            Vector3 d = ponto - frente.position;
            d.y = 0f;

            return d.sqrMagnitude < 0.01f ? 0f : Vector3.SignedAngle(frente.forward, d, Vector3.up);
        }

        private void AtualizarOlhares(float dt, Vector3 jogador, bool perto)
        {
            if (_conversando || !perto)
            {
                return;
            }

            _olharMuda -= dt;

            if (_olharMuda > 0f || _membros.Count < 2)
            {
                return;
            }

            _olharMuda = 2f + (float)_rng.NextDouble() * 4f;
            _quemOlha = _rng.Next(_membros.Count);
            _alvoOlhar = _rng.Next(_membros.Count);

            if (_alvoOlhar == _quemOlha)
            {
                _alvoOlhar = -1;
            }

            _olharAte = Time.time + _olharMuda * 0.8f;
        }

        /// <summary>O jogador passa devagar: um deles manda um "Salve!" e olha para ele.</summary>
        private void Cumprimentar(Vector3 jogador, float kmh, float distancia)
        {
            if (_conversando || Time.time < _cumprimentoEm || distancia > DistanciaCumprimento || kmh < 3f || kmh > 32f)
            {
                return;
            }

            EntregadorNpc perto = null;
            float melhor = float.MaxValue;

            foreach (EntregadorNpc npc in _membros)
            {
                if (npc.EmRota)
                {
                    continue;
                }

                Vector3 p = PontoDe(npc);
                p.y = jogador.y;
                float d = Vector3.Distance(p, jogador);

                if (d < melhor)
                {
                    melhor = d;
                    perto = npc;
                }
            }

            if (perto == null)
            {
                return;
            }

            _cumprimentoEm = Time.time + 40f;
            _quemOlha = _membros.IndexOf(perto);
            _alvoOlhar = -1;
            _olharAte = Time.time + 3f;
            MostrarBalao(perto, Saudacao(), 2.6f);
        }

        private string Saudacao() => FofocaCatalogo.Cumprimentos[_rng.Next(FofocaCatalogo.Cumprimentos.Length)];

        /// <summary>De vez em quando um deles solta uma frase curta para o outro (só com o jogador por perto).</summary>
        private void ConversaSolta(float dt, float distancia)
        {
            if (_conversando || distancia > 28f)
            {
                return;
            }

            _conversaSolta -= dt;

            if (_conversaSolta > 0f || _membros.Count < 2 || (_balao != null && _balao.Visivel))
            {
                return;
            }

            _conversaSolta = 14f + (float)_rng.NextDouble() * 16f;
            EntregadorNpc npc = _membros[_rng.Next(_membros.Count)];

            if (!npc.EmRota)
            {
                MostrarBalao(npc, FofocaCatalogo.Reacoes[_rng.Next(FofocaCatalogo.Reacoes.Length)], 2.8f);
            }
        }

        // ---------- Sair e chegar ----------

        private void Eventos(float dt, Vector3 jogador)
        {
            for (int i = _membros.Count - 1; i >= 0; i--)
            {
                EntregadorNpc npc = _membros[i];

                if (npc.Saiu || (npc.EmRota && !npc.Chegando && Vector3.Distance(npc.transform.position, jogador) > DistanciaAtiva - 10f))
                {
                    Remover(npc);
                }
            }

            if (_conversando)
            {
                return;
            }

            _proximoEvento -= dt;

            if (_proximoEvento > 0f)
            {
                return;
            }

            _proximoEvento = 35f + (float)_rng.NextDouble() * 45f;
            int livres = 0;
            int parados = 0;

            for (int i = 0; i < _ocupantes.Length; i++)
            {
                if (_ocupantes[i] == null)
                {
                    livres++;
                }
                else if (!_ocupantes[i].EmRota)
                {
                    parados++;
                }
            }

            if (parados > _alvoQuantidade || (parados >= 3 && _rng.NextDouble() < 0.5))
            {
                Sair(jogador);
            }

            if (parados <= _alvoQuantidade && livres > 0)
            {
                Chegar(jogador);
            }
        }

        private void Remover(EntregadorNpc npc)
        {
            int vaga = System.Array.IndexOf(_ocupantes, npc);

            if (vaga >= 0)
            {
                _ocupantes[vaga] = null;
            }

            _membros.Remove(npc);

            if (npc.Corpo != null)
            {
                Destroy(npc.Corpo.gameObject);
            }

            Destroy(npc.gameObject);
        }

        private void Sair(Vector3 jogador)
        {
            EntregadorNpc quem = null;

            foreach (EntregadorNpc npc in _membros)
            {
                if (npc.Sentado && !npc.EmRota)
                {
                    quem = npc;

                    break;
                }
            }

            if (quem == null || _grafo == null)
            {
                return;
            }

            int vaga = System.Array.IndexOf(_ocupantes, quem);
            var rota = new List<Vector3>();
            Vector3 origem = quem.transform.position;
            Vector3 naRua = vaga >= 0 ? _vagas[vaga].Rua : origem;
            rota.Add(origem);
            rota.Add(vaga >= 0 ? _vagas[vaga].Moto + _vagas[vaga].ParaRua * 1.8f : origem);

            if (!RotaLonge(naRua, jogador, rota, false))
            {
                return;
            }

            // Fica no mundo ocupando a vaga até sumir; o grupo só conta como livre depois.
            quem.Partir(rota);
        }

        private void Chegar(Vector3 jogador)
        {
            int vaga = ProximaVagaLivre();

            if (vaga < 0 || _grafo == null)
            {
                return;
            }

            VagaPonto v = _vagas[vaga];
            var rota = new List<Vector3>();

            if (!RotaLonge(v.Rua, jogador, rota, true))
            {
                return;
            }

            rota.Add(v.Moto + v.ParaRua * 1.8f);
            rota.Add(v.Moto);
            EntregadorNpc npc = Criar(vaga, true, true);

            if (npc != null)
            {
                npc.transform.position = rota[0] + Vector3.up * -MotoVisual.AlturaDoChao;
                npc.Chegar(rota);
            }
        }

        /// <summary>Monta a rota entre um ponto da rua e um nó longe da vista do jogador (e do ponto). Na chegada a rota vai do nó até a rua.</summary>
        private bool RotaLonge(Vector3 naRua, Vector3 jogador, List<Vector3> saida, bool chegada)
        {
            var trecho = new List<Vector3>();

            for (int tentativa = 0; tentativa < 24; tentativa++)
            {
                Vector3 no = _grafo.NodePosition(_rng.Next(_grafo.NodeCount));
                float dj = Vector3.Distance(Flat(no, jogador.y), jogador);
                float dp = Vector3.Distance(Flat(no, naRua.y), naRua);

                if (dj < DistanciaAtiva + 10f || dp < 90f)
                {
                    continue;
                }

                trecho.Clear();
                bool ok = chegada ? _grafo.TryFindRoute(no, naRua, trecho, out _) : _grafo.TryFindRoute(naRua, no, trecho, out _);

                if (!ok || trecho.Count < 2)
                {
                    continue;
                }

                saida.AddRange(trecho);

                return true;
            }

            return false;
        }

        private static Vector3 Flat(Vector3 p, float y) => new Vector3(p.x, y, p.z);

        /// <summary>Para os testes: chama o evento de saída agora.</summary>
        public bool ForcarSaida(Vector3 jogador)
        {
            int antes = _membros.FindAll(m => m.EmRota).Count;
            Sair(jogador);

            return _membros.FindAll(m => m.EmRota).Count > antes;
        }

        /// <summary>Para os testes: chama o evento de chegada agora.</summary>
        public bool ForcarChegada(Vector3 jogador)
        {
            int antes = _membros.Count;
            Chegar(jogador);

            return _membros.Count > antes;
        }

        /// <summary>Para os testes e capturas: o balão "Salve!" agora.</summary>
        public void ForcarSalve()
        {
            if (_membros.Count > 0)
            {
                MostrarBalao(_membros[0], Saudacao(), 4f);
            }
        }
    }
}
