using System;
using System.Collections.Generic;

namespace Entregador.Core
{
    /// <summary>O que acontece no balcão do restaurante quando o entregador chega.</summary>
    public enum BalcaoSituacao
    {
        /// <summary>O pedido está pronto: é só pegar.</summary>
        PedidoPronto,

        /// <summary>"Tá saindo, uns X min": espera ou reclama no app.</summary>
        SaindoJaJa,

        /// <summary>A atendente pede o código do pedido (o app mostrou o certo; há três para escolher).</summary>
        PedeCodigo,

        /// <summary>O pedido está com item faltando ou trocado, e só quem confere percebe.</summary>
        PedidoTrocado,

        /// <summary>Restaurante lotado: fila para ser atendido.</summary>
        Lotado,
    }

    /// <summary>Ação que o jogador escolhe no balcão.</summary>
    public enum BalcaoAcao
    {
        /// <summary>Pega o pedido (já conferido, entregue ou pronto).</summary>
        PegarPedido,

        /// <summary>Pega o pedido sem conferir e vai embora.</summary>
        PegarESair,

        /// <summary>Abre a sacola e confere os itens (custa tempo).</summary>
        Conferir,

        /// <summary>Espera o preparo ou a fila.</summary>
        Esperar,

        /// <summary>Reclama no app e cancela a entrega.</summary>
        Reclamar,

        /// <summary>Diz um dos códigos (Indice, de 0 a 2, na lista de OpcoesCodigo).</summary>
        InformarCodigo,
    }

    /// <summary>Em que ponto do atendimento o balcão está.</summary>
    public enum RetiradaPasso
    {
        /// <summary>A atendente falou e o jogador escolhe.</summary>
        Atendimento,

        /// <summary>Esperando o preparo ou a fila (o relógio da cena conta EsperaMinutos).</summary>
        Espera,

        /// <summary>Tudo certo: só falta pegar o pedido.</summary>
        Entrega,

        /// <summary>Pedido retirado.</summary>
        Concluida,

        /// <summary>O jogador reclamou no app e cancelou.</summary>
        Cancelada,
    }

    /// <summary>Uma opção de resposta que a cena mostra como botão.</summary>
    public sealed class BalcaoOpcao
    {
        public BalcaoOpcao(BalcaoAcao acao, string rotulo, int indice = 0)
        {
            Acao = acao;
            Rotulo = rotulo;
            Indice = indice;
        }

        public BalcaoAcao Acao { get; }

        public string Rotulo { get; }

        /// <summary>Para InformarCodigo: posição na lista de OpcoesCodigo.</summary>
        public int Indice { get; }
    }

    /// <summary>Pesos e tempos do balcão. Tudo ajustável.</summary>
    public sealed class RetiradaConfig
    {
        public double PesoPronto = 40;
        public double PesoSaindo = 22;
        public double PesoCodigo = 16;
        public double PesoTrocado = 12;
        public double PesoLotado = 10;

        /// <summary>Minutos de jogo de preparo quando "tá saindo" (de EsperaMin a EsperaMax): cozinha de verdade demora (ver RelogioJogo).</summary>
        public int EsperaMin = 3;

        public int EsperaMax = 15;

        /// <summary>Minutos da fila quando o restaurante está lotado.</summary>
        public int FilaMin = 2;

        public int FilaMax = 10;

        /// <summary>Minutos para conferir o pedido.</summary>
        public int MinutosConferir = 2;

        /// <summary>Minutos perdidos a cada código errado.</summary>
        public int MinutosCodigoErrado = 1;

        /// <summary>Chance de o cliente reclamar quando o pedido trocado foi levado sem conferir.</summary>
        public double ChanceReclamacao = 0.8;

        /// <summary>Maior perda de estrelas por pedido trocado sem conferir (de 1 até este valor).</summary>
        public int PenalidadeMaxima = 2;

        public double Peso(BalcaoSituacao situacao)
        {
            switch (situacao)
            {
                case BalcaoSituacao.PedidoPronto: return PesoPronto;
                case BalcaoSituacao.SaindoJaJa: return PesoSaindo;
                case BalcaoSituacao.PedeCodigo: return PesoCodigo;
                case BalcaoSituacao.PedidoTrocado: return PesoTrocado;
                default: return PesoLotado;
            }
        }
    }

    /// <summary>
    /// A retirada do pedido no balcão do restaurante: decide a situação (sorteio com pesos), conduz o diálogo com a atendente e
    /// calcula o que sai dele: minutos perdidos, perda de estrelas na entrega (pedido trocado levado sem conferir) ou cancelamento.
    /// A cena só mostra Fala e Opcoes e chama Escolher; o relógio da espera fica por conta da cena (ConcluirEspera).
    /// O código do pedido nasce junto com a retirada (o app o mostra quando a corrida começa); a situação só é sorteada na chegada.
    /// </summary>
    public sealed class Retirada
    {
        private static readonly string[] Itens = { "o refrigerante", "a batata frita", "o molho", "os talheres", "um dos lanches", "a sobremesa" };

        private readonly IGameRandom _random;
        private readonly RetiradaConfig _config;
        private readonly List<BalcaoOpcao> _opcoes = new List<BalcaoOpcao>();
        private readonly string[] _codigos = new string[3];
        private readonly bool[] _codigoTentado = new bool[3];
        private int _indiceCerto;

        public Retirada(Order pedido, string restaurante, IGameRandom random, RetiradaConfig config = null)
        {
            Pedido = pedido ?? throw new ArgumentNullException(nameof(pedido));
            Restaurante = restaurante ?? string.Empty;
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _config = config ?? new RetiradaConfig();
            GerarCodigos();
        }

        public Order Pedido { get; }

        public string Restaurante { get; }

        /// <summary>Código certo do pedido (4 dígitos), o que o app mostra ao jogador.</summary>
        public string Codigo => _codigos[_indiceCerto];

        /// <summary>Os três códigos que o jogador escolhe quando a atendente pede (um deles é o certo, em ordem sorteada).</summary>
        public IReadOnlyList<string> OpcoesCodigo => _codigos;

        public BalcaoSituacao Situacao { get; private set; }

        /// <summary>A situação já foi definida (o jogador chegou ao balcão).</summary>
        public bool Iniciada { get; private set; }

        public RetiradaPasso Passo { get; private set; }

        /// <summary>O que a atendente diz agora.</summary>
        public string Fala { get; private set; } = string.Empty;

        /// <summary>Minutos de jogo do preparo ou da fila, quando Situacao é SaindoJaJa ou Lotado.</summary>
        public int EsperaMinutos { get; private set; }

        /// <summary>Minutos de jogo perdidos até agora (espera, conferência, códigos errados).</summary>
        public int MinutosPerdidos { get; private set; }

        /// <summary>O pedido estava com item faltando.</summary>
        public bool PedidoComProblema { get; private set; }

        /// <summary>Item que faltava no pedido com problema.</summary>
        public string ItemFaltando { get; private set; } = string.Empty;

        /// <summary>O jogador conferiu a sacola (o problema, se havia, foi corrigido).</summary>
        public bool Conferiu { get; private set; }

        public int ErrosCodigo { get; private set; }

        /// <summary>Estrelas a menos na nota desta entrega (0 a PenalidadeMaxima): pedido trocado levado sem conferir e cliente reclamou.</summary>
        public int PenalidadeEstrelas { get; private set; }

        public bool Concluida => Passo == RetiradaPasso.Concluida;

        public bool Cancelada => Passo == RetiradaPasso.Cancelada;

        public bool Encerrada => Concluida || Cancelada;

        /// <summary>As respostas possíveis agora (vazio durante a espera e depois de encerrada).</summary>
        public IReadOnlyList<BalcaoOpcao> Opcoes => _opcoes;

        /// <summary>Texto curto para o jogador ver o código no começo da corrida.</summary>
        public string TextoCodigo => "Código do pedido: " + Codigo;

        // ---------- Início ----------

        /// <summary>O jogador parou no balcão: sorteia a situação.</summary>
        public void Iniciar()
        {
            Iniciar(Sortear(_random, _config));
        }

        /// <summary>Começa numa situação definida (testes e QA).</summary>
        public void Iniciar(BalcaoSituacao situacao)
        {
            if (Iniciada)
            {
                throw new InvalidOperationException("A retirada já começou.");
            }

            Iniciada = true;
            Situacao = situacao;
            Passo = RetiradaPasso.Atendimento;
            _opcoes.Clear();

            switch (situacao)
            {
                case BalcaoSituacao.SaindoJaJa:
                    EsperaMinutos = _random.Range(_config.EsperaMin, _config.EsperaMax + 1);
                    Fala = $"Oi! O pedido {Pedido.Id:000} tá saindo, uns {EsperaMinutos} min. Quer esperar?";
                    _opcoes.Add(new BalcaoOpcao(BalcaoAcao.Esperar, $"Esperar ({EsperaMinutos} min)"));
                    _opcoes.Add(new BalcaoOpcao(BalcaoAcao.Reclamar, "Reclamar no app (cancelar)"));
                    break;
                case BalcaoSituacao.Lotado:
                    EsperaMinutos = _random.Range(_config.FilaMin, _config.FilaMax + 1);
                    Fala = $"Moço, hoje tá lotado! A fila de retirada é de uns {EsperaMinutos} min.";
                    _opcoes.Add(new BalcaoOpcao(BalcaoAcao.Esperar, $"Esperar na fila ({EsperaMinutos} min)"));
                    _opcoes.Add(new BalcaoOpcao(BalcaoAcao.Reclamar, "Reclamar no app (cancelar)"));
                    break;
                case BalcaoSituacao.PedeCodigo:
                    Fala = "Oi! Qual é o código do pedido?";
                    AdicionarOpcoesCodigo();
                    break;
                case BalcaoSituacao.PedidoTrocado:
                    PedidoComProblema = true;
                    ItemFaltando = Itens[_random.Range(0, Itens.Length)];
                    Fala = $"Pedido {Pedido.Id:000}, tá aqui. Já lacramos a sacola, pode levar.";
                    _opcoes.Add(new BalcaoOpcao(BalcaoAcao.Conferir, $"Conferir o pedido ({_config.MinutosConferir} min)"));
                    _opcoes.Add(new BalcaoOpcao(BalcaoAcao.PegarESair, "Pegar e sair"));
                    break;
                default:
                    Fala = $"Oi! O pedido {Pedido.Id:000} tá prontinho, pode levar.";
                    _opcoes.Add(new BalcaoOpcao(BalcaoAcao.PegarPedido, "Pegar o pedido"));
                    break;
            }
        }

        /// <summary>Sorteia a situação com os pesos da configuração.</summary>
        public static BalcaoSituacao Sortear(IGameRandom random, RetiradaConfig config = null)
        {
            config = config ?? new RetiradaConfig();
            var todas = (BalcaoSituacao[])Enum.GetValues(typeof(BalcaoSituacao));
            double total = 0;

            foreach (BalcaoSituacao s in todas)
            {
                total += Math.Max(0, config.Peso(s));
            }

            double roll = random.NextDouble() * total;

            foreach (BalcaoSituacao s in todas)
            {
                roll -= Math.Max(0, config.Peso(s));

                if (roll < 0)
                {
                    return s;
                }
            }

            return BalcaoSituacao.PedidoPronto;
        }

        /// <summary>A fala da atendente no ponto da espera (progresso de 0 a 1): começo, meio e fim, para a espera não ficar muda.</summary>
        public string FalaEspera(double progresso)
        {
            if (Passo != RetiradaPasso.Espera)
            {
                return Fala;
            }

            int i = progresso < 0.34 ? 0 : progresso < 0.67 ? 1 : 2;
            int variante = Pedido.Id % 2;

            if (Situacao == BalcaoSituacao.Lotado)
            {
                string[][] fila =
                {
                    new[] { "Beleza, entra na fila aí.", "Tá andando, tem mais dois na sua frente.", "Falta pouco, é o próximo!" },
                    new[] { "Hoje o movimento tá pesado, aguenta aí.", "A chapa tá cheia, mas já já chega a sua vez.", "Sua vez, moço!" },
                };

                return fila[variante][i];
            }

            string[][] cozinha =
            {
                new[] { "Pode esperar aqui do lado, já já fica pronto.", "Tá na chapa ainda, uns minutinhos.", "Quase pronto, só fechando a sacola!" },
                new[] { "Entrou agora na cozinha, vai levar um tempinho.", "O cozinheiro falou que tá terminando.", "Pronto! Só lacrar e já te entrego." },
            };

            return cozinha[variante][i];
        }

        // ---------- Escolhas ----------

        /// <summary>Aplica a escolha do jogador. Escolhas que não estão em Opcoes lançam InvalidOperationException.</summary>
        public void Escolher(BalcaoAcao acao, int indice = 0)
        {
            if (!Iniciada || Passo == RetiradaPasso.Espera || Encerrada)
            {
                throw new InvalidOperationException("Não há o que escolher agora.");
            }

            BalcaoOpcao opcao = _opcoes.Find(o => o.Acao == acao && (acao != BalcaoAcao.InformarCodigo || o.Indice == indice));

            if (opcao == null)
            {
                throw new InvalidOperationException("Esta escolha não está disponível: " + acao);
            }

            switch (acao)
            {
                case BalcaoAcao.Esperar:
                    Passo = RetiradaPasso.Espera;
                    Fala = Situacao == BalcaoSituacao.Lotado ? "Beleza, entra na fila aí." : "Pode esperar aqui do lado, já já fica pronto.";
                    _opcoes.Clear();
                    break;
                case BalcaoAcao.Reclamar:
                    Passo = RetiradaPasso.Cancelada;
                    Fala = "Tudo bem, desculpa o transtorno. Pode cancelar no app.";
                    _opcoes.Clear();
                    break;
                case BalcaoAcao.Conferir:
                    Conferiu = true;
                    MinutosPerdidos += _config.MinutosConferir;
                    PedidoComProblema = false;
                    Passo = RetiradaPasso.Entrega;
                    Fala = $"Você abre a sacola: faltava {ItemFaltando}! A atendente repõe na hora.";
                    _opcoes.Clear();
                    _opcoes.Add(new BalcaoOpcao(BalcaoAcao.PegarPedido, "Pegar o pedido"));
                    break;
                case BalcaoAcao.InformarCodigo:
                    EscolherCodigo(indice);
                    break;
                case BalcaoAcao.PegarESair:
                    LevarSemConferir();
                    break;
                default:
                    Passo = RetiradaPasso.Concluida;
                    Fala = "Bom trabalho! Boa entrega.";
                    _opcoes.Clear();
                    break;
            }
        }

        /// <summary>O relógio da cena contou os minutos da espera: o pedido fica pronto.</summary>
        public void ConcluirEspera()
        {
            if (Passo != RetiradaPasso.Espera)
            {
                throw new InvalidOperationException("Não há espera em andamento.");
            }

            MinutosPerdidos += EsperaMinutos;
            Passo = RetiradaPasso.Entrega;
            Fala = Situacao == BalcaoSituacao.Lotado ? "Sua vez! Aqui o pedido, boa entrega." : "Pronto! Desculpa a demora. Aqui o pedido.";
            _opcoes.Clear();
            _opcoes.Add(new BalcaoOpcao(BalcaoAcao.PegarPedido, "Pegar o pedido"));
        }

        // ---------- Internos ----------

        private void EscolherCodigo(int indice)
        {
            _codigoTentado[indice] = true;

            if (indice == _indiceCerto)
            {
                Passo = RetiradaPasso.Entrega;
                Fala = "Isso mesmo! Aqui está o seu pedido.";
                _opcoes.Clear();
                _opcoes.Add(new BalcaoOpcao(BalcaoAcao.PegarPedido, "Pegar o pedido"));

                return;
            }

            ErrosCodigo++;
            MinutosPerdidos += _config.MinutosCodigoErrado;
            Fala = "Hum, esse código não é. Confere direitinho no app.";
            AdicionarOpcoesCodigo();
        }

        private void AdicionarOpcoesCodigo()
        {
            _opcoes.Clear();

            for (int i = 0; i < _codigos.Length; i++)
            {
                if (!_codigoTentado[i])
                {
                    _opcoes.Add(new BalcaoOpcao(BalcaoAcao.InformarCodigo, _codigos[i], i));
                }
            }
        }

        private void LevarSemConferir()
        {
            Passo = RetiradaPasso.Concluida;
            _opcoes.Clear();

            if (PedidoComProblema && _random.Chance(_config.ChanceReclamacao))
            {
                PenalidadeEstrelas = 1 + _random.Range(0, Math.Max(1, _config.PenalidadeMaxima));
                PenalidadeEstrelas = Math.Min(PenalidadeEstrelas, Math.Max(1, _config.PenalidadeMaxima));
            }

            Fala = "Tchau, boa entrega!";
        }

        /// <summary>Quatro dígitos para o código certo e dois códigos parecidos e diferentes, na ordem sorteada.</summary>
        private void GerarCodigos()
        {
            int certo = _random.Range(1000, 10000);
            _indiceCerto = _random.Range(0, 3);
            var usados = new HashSet<int> { certo };
            int errado1 = Diferente(certo, usados);
            usados.Add(errado1);
            int errado2 = Diferente(certo, usados);
            int[] ordem = { errado1, errado2 };
            int proximo = 0;

            for (int i = 0; i < 3; i++)
            {
                _codigos[i] = (i == _indiceCerto ? certo : ordem[proximo++]).ToString();
            }
        }

        /// <summary>Um código parecido com o certo (um ou dois dígitos trocados) que ainda não saiu.</summary>
        private int Diferente(int certo, HashSet<int> usados)
        {
            int posicao = _random.Range(0, 4);
            int passo = 1 + _random.Range(0, 9);
            int candidato = certo;

            for (int tentativa = 0; tentativa < 40; tentativa++)
            {
                int divisor = 1;

                for (int i = 0; i < posicao; i++)
                {
                    divisor *= 10;
                }

                int digito = certo / divisor % 10;
                int novo = (digito + passo + tentativa) % 10;
                candidato = certo + (novo - digito) * divisor;

                if (candidato >= 1000 && candidato <= 9999 && !usados.Contains(candidato))
                {
                    return candidato;
                }
            }

            candidato = certo;

            while (usados.Contains(candidato) || candidato < 1000 || candidato > 9999)
            {
                candidato = candidato >= 9999 ? 1000 : candidato + 1;
            }

            return candidato;
        }
    }
}
