using System;
using System.Collections.Generic;

namespace Entregador.Core
{
    /// <summary>Assunto de uma fala. Só os de informação (blitz, radar, chuva, demanda, restaurante, app) dependem do que o jogador pergunta.</summary>
    public enum FofocaTema
    {
        Saudacao,
        Papo,
        Blitz,
        Radar,
        Chuva,
        Demanda,
        Restaurante,
        App,
        Despedida,
        Jogador,
    }

    /// <summary>Uma blitz do dia, como os entregadores falam dela: a rua e uma referência ("perto do Largo").</summary>
    public sealed class LocalFofoca
    {
        public string Rua;
        public string Referencia;

        public LocalFofoca(string rua, string referencia)
        {
            Rua = rua;
            Referencia = referencia;
        }
    }

    /// <summary>Um radar fixo (rua e limite).</summary>
    public sealed class RadarFofoca
    {
        public string Rua;
        public int LimiteKmh;

        public RadarFofoca(string rua, int limiteKmh)
        {
            Rua = rua;
            LimiteKmh = limiteKmh;
        }
    }

    /// <summary>O que o jogo sabe e os entregadores podem contar. A camada Unity monta isto a partir da cena e da GameSession.</summary>
    public sealed class ContextoFofoca
    {
        /// <summary>Blitz abertas hoje (vazio = não há blitz).</summary>
        public readonly List<LocalFofoca> BlitzHoje = new List<LocalFofoca>();

        /// <summary>Pontos de blitz da cidade que NÃO estão abertos hoje (servem para rumor errado).</summary>
        public readonly List<LocalFofoca> BlitzFalsas = new List<LocalFofoca>();

        public readonly List<RadarFofoca> Radares = new List<RadarFofoca>();
        public bool ChovendoAgora;
        public bool ChoveHoje;
        public bool ChoveAmanha;

        /// <summary>Onde o movimento está maior, já em frase ("lá no Largo Treze"). Vazio = sem informação.</summary>
        public string ZonaMovimentada = string.Empty;

        public string RestauranteLento = string.Empty;
        public int MinutosDeEspera;
    }

    /// <summary>Ajustes da conversa.</summary>
    public sealed class ConversaConfig
    {
        /// <summary>Chance de a dica de blitz estar certa (0 a 1).</summary>
        public double PrecisaoBlitz = 0.8;

        /// <summary>Chance de a dica certa trazer a rua (senão só a referência vaga).</summary>
        public double ChanceDicaEspecifica = 0.6;

        public int TrocasMin = 3;
        public int TrocasMax = 6;
        public int MinutosPorTroca = 2;
    }

    /// <summary>Uma fala do chat. Autor é o índice do entregador no grupo, ou -1 para o jogador.</summary>
    public sealed class FalaConversa
    {
        public int Autor;
        public string Nome;
        public string Texto;
        public FofocaTema Tema;
    }

    /// <summary>Uma resposta que o jogador pode escolher.</summary>
    public sealed class OpcaoConversa
    {
        public string Rotulo;
        public FofocaTema Tema;
        public bool Sair;
    }

    /// <summary>Informação que o jogador recebeu. Verdadeira = confere com o estado real do jogo (rumor errado fica falso).</summary>
    public sealed class InfoRevelada
    {
        public FofocaTema Tema;
        public bool Verdadeira;

        /// <summary>A rua citada (blitz, radar) ou vazio.</summary>
        public string Local = string.Empty;

        /// <summary>Só a referência, sem a rua.</summary>
        public bool Vaga;
    }

    /// <summary>
    /// Uma roda de conversa em andamento. Começa com cumprimento e um papo; o jogador escolhe 2 a 3 respostas a cada passo
    /// (perguntar blitz, radar, chuva, movimento, app, ouvir um papo, ou sair). Dura de 3 a 6 trocas e gasta uns minutos de jogo.
    /// </summary>
    public sealed class Conversa
    {
        private readonly ConversaEntregadores _origem;
        private readonly string _grupo;
        private readonly string[] _nomes;
        private readonly ContextoFofoca _contexto;
        private readonly List<FalaConversa> _falas = new List<FalaConversa>();
        private readonly List<OpcaoConversa> _opcoes = new List<OpcaoConversa>();
        private readonly List<InfoRevelada> _reveladas = new List<InfoRevelada>();
        private readonly HashSet<FofocaTema> _perguntados = new HashSet<FofocaTema>();
        private readonly int _trocasAlvo;
        private int _ultimoAutor = -1;

        internal Conversa(ConversaEntregadores origem, string grupo, string[] nomes, ContextoFofoca contexto, int trocasAlvo)
        {
            _origem = origem;
            _grupo = grupo;
            _nomes = nomes;
            _contexto = contexto;
            _trocasAlvo = trocasAlvo;
        }

        public IReadOnlyList<FalaConversa> Falas => _falas;

        public IReadOnlyList<OpcaoConversa> Opcoes => _opcoes;

        public IReadOnlyList<InfoRevelada> Reveladas => _reveladas;

        public IReadOnlyList<string> Nomes => _nomes;

        public bool Terminada { get; private set; }

        /// <summary>Quantas respostas o jogador já deu.</summary>
        public int Trocas { get; private set; }

        public int TrocasAlvo => _trocasAlvo;

        /// <summary>Minutos de jogo gastos até agora (1 do cumprimento mais os das trocas).</summary>
        public int MinutosGastos => 1 + Trocas * _origem.Config.MinutosPorTroca;

        internal void Comecar()
        {
            Fala(Proximo(), _origem.Escolhe(_grupo, "saudacao", FofocaCatalogo.Saudacoes), FofocaTema.Saudacao);
            Fala(Proximo(), _origem.Escolhe(_grupo, "papo", FofocaCatalogo.Papo), FofocaTema.Papo);
            MontarOpcoes();
        }

        /// <summary>O jogador escolheu uma resposta.</summary>
        public void Escolher(int indice)
        {
            if (Terminada || indice < 0 || indice >= _opcoes.Count)
            {
                return;
            }

            OpcaoConversa opcao = _opcoes[indice];
            Fala(-1, opcao.Rotulo, FofocaTema.Jogador);

            if (opcao.Sair)
            {
                Encerrar();

                return;
            }

            Trocas++;
            _perguntados.Add(opcao.Tema);
            int autor = Proximo();
            Fala(autor, Responder(opcao.Tema), opcao.Tema);

            if (_origem.Chance(0.55))
            {
                Fala(Proximo(), _origem.Escolhe(_grupo, "reacao", FofocaCatalogo.Reacoes), FofocaTema.Papo);
            }

            if (Trocas >= _trocasAlvo)
            {
                Fala(Proximo(), _origem.Escolhe(_grupo, "despedida", FofocaCatalogo.Despedidas), FofocaTema.Despedida);
                Terminada = true;
                _opcoes.Clear();

                return;
            }

            MontarOpcoes();
        }

        private void Encerrar()
        {
            Fala(Proximo(), _origem.Escolhe(_grupo, "despedida", FofocaCatalogo.Despedidas), FofocaTema.Despedida);
            Terminada = true;
            _opcoes.Clear();
        }

        private void Fala(int autor, string texto, FofocaTema tema)
        {
            _falas.Add(new FalaConversa { Autor = autor, Nome = autor >= 0 ? _nomes[autor] : "Você", Texto = texto, Tema = tema });
        }

        /// <summary>Próximo entregador a falar (nunca o mesmo duas vezes seguidas, se houver mais de um).</summary>
        private int Proximo()
        {
            int autor = _origem.Sorteia(_nomes.Length);

            if (_nomes.Length > 1 && autor == _ultimoAutor)
            {
                autor = (autor + 1) % _nomes.Length;
            }

            _ultimoAutor = autor;

            return autor;
        }

        // ---------- Opções ----------

        private void MontarOpcoes()
        {
            _opcoes.Clear();
            var candidatos = new List<FofocaTema>();

            foreach (FofocaTema tema in new[] { FofocaTema.Blitz, FofocaTema.Radar, FofocaTema.Chuva, FofocaTema.Demanda, FofocaTema.Restaurante, FofocaTema.App })
            {
                if (!_perguntados.Contains(tema))
                {
                    candidatos.Add(tema);
                }
            }

            // A pergunta da blitz é a mais útil: aparece primeiro enquanto ninguém perguntou. A outra opção é sorteada (ou um papo).
            if (candidatos.Contains(FofocaTema.Blitz))
            {
                AddOpcao(FofocaTema.Blitz);
                candidatos.Remove(FofocaTema.Blitz);
            }

            if (candidatos.Count > 0 && !_origem.Chance(0.25))
            {
                AddOpcao(candidatos[_origem.Sorteia(candidatos.Count)]);
            }
            else
            {
                AddOpcao(FofocaTema.Papo);
            }

            if (_opcoes.Count < 2 && candidatos.Count > 0)
            {
                AddOpcao(candidatos[_origem.Sorteia(candidatos.Count)]);
            }

            _opcoes.Add(new OpcaoConversa { Rotulo = _origem.Escolhe(_grupo, "sair", FofocaCatalogo.PerguntaSair), Tema = FofocaTema.Despedida, Sair = true });
        }

        private void AddOpcao(FofocaTema tema)
        {
            string[] rotulos;

            switch (tema)
            {
                case FofocaTema.Blitz: rotulos = FofocaCatalogo.PerguntaBlitz; break;
                case FofocaTema.Radar: rotulos = FofocaCatalogo.PerguntaRadar; break;
                case FofocaTema.Chuva: rotulos = FofocaCatalogo.PerguntaChuva; break;
                case FofocaTema.Demanda: rotulos = FofocaCatalogo.PerguntaDemanda; break;
                case FofocaTema.Restaurante: rotulos = FofocaCatalogo.PerguntaRestaurante; break;
                case FofocaTema.App: rotulos = FofocaCatalogo.PerguntaApp; break;
                default: rotulos = FofocaCatalogo.PerguntaPapo; break;
            }

            _opcoes.Add(new OpcaoConversa { Rotulo = _origem.Escolhe(_grupo, "pergunta" + tema, rotulos), Tema = tema });
        }

        // ---------- Respostas ----------

        private string Responder(FofocaTema tema)
        {
            switch (tema)
            {
                case FofocaTema.Blitz: return RespostaBlitz();
                case FofocaTema.Radar: return RespostaRadar();
                case FofocaTema.Chuva: return RespostaChuva();
                case FofocaTema.Demanda: return RespostaDemanda();
                case FofocaTema.Restaurante: return RespostaRestaurante();
                case FofocaTema.App: return RespostaApp();
                default: return _origem.Escolhe(_grupo, "papo", FofocaCatalogo.Papo);
            }
        }

        private string RespostaBlitz()
        {
            ConversaConfig config = _origem.Config;
            bool certo = _origem.Chance(config.PrecisaoBlitz);
            ContextoFofoca c = _contexto;

            if (c.BlitzHoje.Count > 0)
            {
                if (certo)
                {
                    LocalFofoca real = c.BlitzHoje[_origem.Sorteia(c.BlitzHoje.Count)];
                    bool especifica = _origem.Chance(config.ChanceDicaEspecifica);
                    _reveladas.Add(new InfoRevelada { Tema = FofocaTema.Blitz, Verdadeira = true, Local = especifica ? real.Rua : string.Empty, Vaga = !especifica });

                    return especifica
                        ? Preenche(_origem.Escolhe(_grupo, "blitzE", FofocaCatalogo.BlitzEspecifica), real.Rua, real.Referencia)
                        : Preenche(_origem.Escolhe(_grupo, "blitzV", FofocaCatalogo.BlitzVaga), real.Rua, real.Referencia);
                }

                return Errada();
            }

            if (certo)
            {
                _reveladas.Add(new InfoRevelada { Tema = FofocaTema.Blitz, Verdadeira = true });

                return _origem.Escolhe(_grupo, "blitzL", FofocaCatalogo.BlitzLimpa);
            }

            return Errada();
        }

        /// <summary>Rumor errado: cita um ponto sem blitz hoje, ou diz que está limpo quando não está.</summary>
        private string Errada()
        {
            ContextoFofoca c = _contexto;

            if (c.BlitzHoje.Count > 0)
            {
                // Há blitz: o rumor errado é "está limpo" ou aponta outro lugar.
                if (c.BlitzFalsas.Count > 0 && _origem.Chance(0.6))
                {
                    LocalFofoca falsa = c.BlitzFalsas[_origem.Sorteia(c.BlitzFalsas.Count)];
                    _reveladas.Add(new InfoRevelada { Tema = FofocaTema.Blitz, Verdadeira = false, Local = falsa.Rua });

                    return Preenche(_origem.Escolhe(_grupo, "blitzF", FofocaCatalogo.BlitzFalsa), falsa.Rua, falsa.Referencia);
                }

                _reveladas.Add(new InfoRevelada { Tema = FofocaTema.Blitz, Verdadeira = false });

                return _origem.Escolhe(_grupo, "blitzFL", FofocaCatalogo.BlitzFalsaLimpa);
            }

            // Não há blitz: o rumor errado inventa uma.
            if (c.BlitzFalsas.Count > 0)
            {
                LocalFofoca falsa = c.BlitzFalsas[_origem.Sorteia(c.BlitzFalsas.Count)];
                _reveladas.Add(new InfoRevelada { Tema = FofocaTema.Blitz, Verdadeira = false, Local = falsa.Rua });

                return Preenche(_origem.Escolhe(_grupo, "blitzF", FofocaCatalogo.BlitzFalsa), falsa.Rua, falsa.Referencia);
            }

            _reveladas.Add(new InfoRevelada { Tema = FofocaTema.Blitz, Verdadeira = true });

            return _origem.Escolhe(_grupo, "blitzL", FofocaCatalogo.BlitzLimpa);
        }

        private string RespostaRadar()
        {
            if (_contexto.Radares.Count == 0)
            {
                _reveladas.Add(new InfoRevelada { Tema = FofocaTema.Radar, Verdadeira = true });

                return _origem.Escolhe(_grupo, "radarN", FofocaCatalogo.RadarNenhum);
            }

            RadarFofoca radar = _contexto.Radares[_origem.Sorteia(_contexto.Radares.Count)];
            _reveladas.Add(new InfoRevelada { Tema = FofocaTema.Radar, Verdadeira = true, Local = radar.Rua });

            return _origem.Escolhe(_grupo, "radar", FofocaCatalogo.Radar).Replace("{rua}", radar.Rua).Replace("{lim}", radar.LimiteKmh.ToString());
        }

        private string RespostaChuva()
        {
            _reveladas.Add(new InfoRevelada { Tema = FofocaTema.Chuva, Verdadeira = true });

            if (_contexto.ChovendoAgora)
            {
                return _origem.Escolhe(_grupo, "chuvaA", FofocaCatalogo.ChuvaAgora);
            }

            if (_contexto.ChoveHoje)
            {
                return _origem.Escolhe(_grupo, "chuvaT", FofocaCatalogo.ChuvaMaisTarde);
            }

            return _contexto.ChoveAmanha
                ? _origem.Escolhe(_grupo, "chuvaM", FofocaCatalogo.ChuvaAmanha)
                : _origem.Escolhe(_grupo, "chuvaN", FofocaCatalogo.ChuvaNenhuma);
        }

        private string RespostaDemanda()
        {
            if (string.IsNullOrEmpty(_contexto.ZonaMovimentada))
            {
                return RespostaApp();
            }

            _reveladas.Add(new InfoRevelada { Tema = FofocaTema.Demanda, Verdadeira = true, Local = _contexto.ZonaMovimentada });

            return _origem.Escolhe(_grupo, "demanda", FofocaCatalogo.Demanda).Replace("{zona}", _contexto.ZonaMovimentada);
        }

        private string RespostaRestaurante()
        {
            if (string.IsNullOrEmpty(_contexto.RestauranteLento))
            {
                return RespostaDemanda();
            }

            _reveladas.Add(new InfoRevelada { Tema = FofocaTema.Restaurante, Verdadeira = true, Local = _contexto.RestauranteLento });

            return _origem.Escolhe(_grupo, "restLento", FofocaCatalogo.RestauranteLento)
                .Replace("{rest}", _contexto.RestauranteLento)
                .Replace("{min}", _contexto.MinutosDeEspera.ToString());
        }

        private string RespostaApp()
        {
            _reveladas.Add(new InfoRevelada { Tema = FofocaTema.App, Verdadeira = true });

            // Gorjeta citada nunca passa de R$ 5.
            if (_origem.Chance(0.2))
            {
                int valor = 1 + _origem.Sorteia(5);

                return _origem.Escolhe(_grupo, "gorjeta", FofocaCatalogo.Gorjeta).Replace("{valor}", valor.ToString());
            }

            return _origem.Escolhe(_grupo, "app", FofocaCatalogo.App);
        }

        private static string Preenche(string modelo, string rua, string referencia) => modelo.Replace("{rua}", rua ?? string.Empty).Replace("{ref}", referencia ?? string.Empty);
    }

    /// <summary>
    /// Gerador das conversas dos pontos de entregadores. Guarda, por grupo e por dia, quais falas já saíram (sem repetir no mesmo dia).
    /// Misturar papo e informação verdadeira (ou rumor errado, com a chance de ConversaConfig.PrecisaoBlitz) é o trabalho da Conversa.
    /// </summary>
    public sealed class ConversaEntregadores
    {
        private readonly IGameRandom _random;
        private readonly Dictionary<string, HashSet<int>> _usadas = new Dictionary<string, HashSet<int>>();
        private int _dia = -1;

        public ConversaEntregadores(IGameRandom random, ConversaConfig config = null)
        {
            _random = random ?? throw new ArgumentNullException(nameof(random));
            Config = config ?? new ConversaConfig();
        }

        public ConversaConfig Config { get; }

        /// <summary>Começa uma conversa do grupo. Um dia novo zera a lista de falas já usadas.</summary>
        public Conversa Iniciar(string grupo, int dia, IReadOnlyList<string> nomes, ContextoFofoca contexto)
        {
            if (nomes == null || nomes.Count == 0)
            {
                throw new ArgumentException("O grupo precisa de pelo menos um entregador.", nameof(nomes));
            }

            if (dia != _dia)
            {
                _dia = dia;
                _usadas.Clear();
            }

            var copia = new string[nomes.Count];

            for (int i = 0; i < copia.Length; i++)
            {
                copia[i] = nomes[i];
            }

            int alvo = _random.Range(Config.TrocasMin, Config.TrocasMax + 1);
            var conversa = new Conversa(this, grupo, copia, contexto ?? new ContextoFofoca(), alvo);
            conversa.Comecar();

            return conversa;
        }

        /// <summary>Um cumprimento curto para o balão acima da cabeça.</summary>
        public string Cumprimento() => FofocaCatalogo.Cumprimentos[_random.Range(0, FofocaCatalogo.Cumprimentos.Length)];

        internal bool Chance(double p) => _random.Chance(p);

        internal int Sorteia(int n) => _random.Range(0, n);

        /// <summary>Sorteia uma fala do grupo que ainda não saiu hoje; se todas já saíram, recomeça aquele conjunto.</summary>
        internal string Escolhe(string grupo, string conjunto, string[] linhas)
        {
            string chave = grupo + "|" + conjunto;

            if (!_usadas.TryGetValue(chave, out HashSet<int> usadas))
            {
                usadas = new HashSet<int>();
                _usadas[chave] = usadas;
            }

            if (usadas.Count >= linhas.Length)
            {
                usadas.Clear();
            }

            int livres = linhas.Length - usadas.Count;
            int alvo = _random.Range(0, livres);

            for (int i = 0; i < linhas.Length; i++)
            {
                if (usadas.Contains(i))
                {
                    continue;
                }

                if (alvo-- == 0)
                {
                    usadas.Add(i);

                    return linhas[i];
                }
            }

            return linhas[0];
        }
    }
}
