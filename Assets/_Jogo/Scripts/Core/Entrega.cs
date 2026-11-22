using System;
using System.Collections.Generic;

namespace Entregador.Core
{
    /// <summary>O que acontece na porta do cliente quando o entregador chega.</summary>
    public enum EntregaSituacao
    {
        /// <summary>O cliente avisa que já desce (2 a 5 min).</summary>
        ClienteDesce,

        /// <summary>O porteiro oferece receber o pedido.</summary>
        Portaria,

        /// <summary>O cliente ainda não está pronto (3 a 8 min).</summary>
        ClienteDemora,

        /// <summary>O interfone não funciona.</summary>
        InterfoneQuebrado,

        /// <summary>O cliente pede para você subir até o apartamento.</summary>
        PedeParaSubir,

        /// <summary>O cliente esqueceu o código de entrega e demora a achar.</summary>
        CodigoEntrega,
    }

    public enum EntregaAcao
    {
        /// <summary>Passa o pedido ao cliente.</summary>
        Entregar,

        /// <summary>Tira a foto da entrega (protege contra "não recebi").</summary>
        Fotografar,

        DeixarNaPortaria,

        /// <summary>Pede ao cliente que desça.</summary>
        ChamarOCliente,

        Esperar,

        LigarNoApp,

        BaterPalmas,

        EsperarMorador,

        SubirAteOApartamento,

        RecusarSubir,

        /// <summary>Na discussão: não subo mesmo.</summary>
        MantemPosicao,

        /// <summary>Na discussão: tá bom, eu subo.</summary>
        CederESubir,

        /// <summary>Digita um dos códigos (Indice na lista OpcoesCodigo).</summary>
        InformarCodigo,
    }

    public enum EntregaPasso
    {
        /// <summary>Falou e espera a escolha do jogador.</summary>
        Atendimento,

        /// <summary>O tempo corre (EsperaMinutos de jogo) e a fala muda no meio.</summary>
        Espera,

        Concluida,
    }

    public sealed class EntregaOpcao
    {
        public EntregaOpcao(EntregaAcao acao, string rotulo, int indice = 0)
        {
            Acao = acao;
            Rotulo = rotulo;
            Indice = indice;
        }

        public EntregaAcao Acao { get; }

        public string Rotulo { get; }

        public int Indice { get; }
    }

    /// <summary>Pesos, durações (minutos de jogo) e chances da entrega. Tudo ajustável.</summary>
    public sealed class EntregaConfig
    {
        public double PesoDesce = 44;
        public double PesoPortaria = 20;
        public double PesoDemora = 12;
        public double PesoInterfone = 8;
        public double PesoSubir = 9;
        public double PesoCodigo = 7;

        /// <summary>Cliente descendo: 2 a 5 min.</summary>
        public int DesceMin = 2;
        public int DesceMax = 5;

        /// <summary>Porteiro conferindo o morador: 1 a 2 min.</summary>
        public int PortariaMin = 1;
        public int PortariaMax = 2;

        /// <summary>Cliente atrasado: 3 a 8 min.</summary>
        public int DemoraMin = 3;
        public int DemoraMax = 8;

        /// <summary>Subir, entregar e voltar: 4 a 8 min.</summary>
        public int SubirMin = 4;
        public int SubirMax = 8;

        /// <summary>Discussão ao recusar subir: 1 a 2 min.</summary>
        public int DiscussaoMin = 1;
        public int DiscussaoMax = 2;

        /// <summary>Cliente procurando o código: 1 a 3 min.</summary>
        public int CodigoMin = 1;
        public int CodigoMax = 3;

        public int MinutosLigar = 1;
        public int MinutosCodigoErrado = 1;

        public double ChanceLigarAtende = 0.6;
        public double ChanceLigarInterfone = 0.7;
        public double ChancePalmas = 0.5;
        public double ChanceCodigoPeloApp = 0.5;

        /// <summary>Ao manter a posição de não subir: o cliente desce bufando, reclama ou fica por isso mesmo.</summary>
        public double ChanceDesceMesmoAssim = 0.35;
        public double ChanceReclama = 0.35;

        /// <summary>Chance de a nota cair ao deixar na portaria quando o cliente já estava atrasado.</summary>
        public double ChancePenalidadePortaria = 0.5;

        public double FatorGorjetaSubir = 1.6;
        public double FatorGorjetaCedeu = 0.9;
        public double FatorGorjetaDesceBufando = 0.3;
        public double FatorGorjetaPortaria = 0.3;

        /// <summary>Chance extra de "cliente diz que não recebeu" ao deixar na portaria, sem foto e com foto.</summary>
        public double RiscoPortariaSemFoto = 0.25;
        public double RiscoPortariaComFoto = 0.08;

        public double Peso(EntregaSituacao situacao)
        {
            switch (situacao)
            {
                case EntregaSituacao.ClienteDesce: return PesoDesce;
                case EntregaSituacao.Portaria: return PesoPortaria;
                case EntregaSituacao.ClienteDemora: return PesoDemora;
                case EntregaSituacao.InterfoneQuebrado: return PesoInterfone;
                case EntregaSituacao.PedeParaSubir: return PesoSubir;
                default: return PesoCodigo;
            }
        }
    }

    /// <summary>
    /// A entrega na porta do cliente: sorteia a situação (a maioria é só esperar o cliente descer), conduz o diálogo e calcula minutos
    /// perdidos, perda de estrelas, fator da gorjeta e risco de "não recebi". As esperas duram o que durariam na vida real (em minutos de
    /// jogo; ver RelogioJogo) e a fala muda no meio (FalaEspera). A cena só mostra Fala/Opcoes, chama Escolher e conta ConcluirEspera.
    /// </summary>
    public sealed class Entrega
    {
        private static readonly string[][] FalasDesce =
        {
            new[] { "Opa, já tô descendo!", "Peraí que o elevador tá demorando...", "Cheguei! Desculpa a demora." },
            new[] { "Oi! Só calçar o chinelo e já desço.", "Tô no elevador, mas ele para em todo andar!", "Prontinho, tô aqui no portão." },
            new[] { "Boa noite! Descendo em um minuto.", "Já tô chegando, só achando a chave do portão.", "Aqui! Obrigado por esperar." },
        };

        private static readonly string[] FalasPortaria =
        {
            "Boa noite! Pode deixar na portaria que eu entrego pro morador.",
            "Esse pedido é pro apartamento 52? Pode deixar aqui comigo.",
            "Moço, o morador pediu pra deixar com a gente na portaria.",
        };

        private static readonly string[][] FalasPorteiro =
        {
            new[] { "Deixa eu anotar aqui o nome...", "Interfonando pro morador...", "Pronto, pode deixar que eu entrego." },
            new[] { "Peraí que eu confiro o apartamento.", "O morador mandou liberar, só um instante.", "Tá certo! Pode ir, valeu." },
        };

        private static readonly string[] FalasDemora =
        {
            "Mô, desculpa, só mais {n} minutinhos que eu tô saindo do banho!",
            "Oi! Já tô indo, uns {n} min, tô achando o dinheiro da gorjeta...",
            "Peraí, tô terminando uma reunião aqui, {n} minutinhos!",
        };

        private static readonly string[][] FalasEsperaDemora =
        {
            new[] { "(Nenhum sinal do cliente ainda.)", "Pela janela dá pra ouvir: 'já vai, já vai!'", "A luz do corredor acendeu: ele tá vindo." },
            new[] { "(O cliente ainda não apareceu.)", "Ele grita lá de cima: 'só achar o chinelo!'", "A porta do prédio abre: finalmente." },
        };

        private static readonly string[] FalasInterfone =
        {
            "O interfone não funciona. Ninguém atende.",
            "Apertou o botão do interfone: só um zumbido. Quebrado.",
            "Interfone mudo e o portão tá trancado.",
        };

        private static readonly string[][] FalasEsperaInterfone =
        {
            new[] { "Você espera na calçada, olhando pro portão.", "Passa uma moça com o cachorro. Ainda nada.", "O portão abre: é o cliente!" },
            new[] { "Você tenta de novo o interfone. Nada.", "Um vizinho espia da janela.", "Alguém vem pelo corredor com a chave." },
        };

        private static readonly string[] FalasSubir =
        {
            "Moço, tô com a perna machucada. Dá pra subir? É o apto 42, 4º andar, sem elevador.",
            "Você pode subir? Tô com o bebê no colo, é o 3º andar.",
            "Dá pra trazer até a porta? É rapidinho, apto 31, escada mesmo.",
        };

        private static readonly string[] FalasDiscussao =
        {
            "Ah, mas é só um lance de escada, moço! No app tá escrito 'entrega na porta'.",
            "Poxa, sério? Todo entregador sobe, nunca ninguém reclamou!",
            "Nossa, que falta de boa vontade. Eu moro aqui há anos e todo mundo sobe.",
        };

        private static readonly string[][] FalasEsperaSubir =
        {
            new[] { "Subindo a escada: 1º andar...", "Quase lá, falta um lance...", "Entregou na porta. O cliente agradece." },
            new[] { "Você sobe os degraus de dois em dois.", "Parou pra respirar no 3º andar.", "Tá na porta, o cliente sorri e pega o pedido." },
        };

        private static readonly string[] FalasCodigo =
        {
            "O código de entrega? Peraí, esqueci... onde é que vem...",
            "Código? Ih, o app tá em outra tela, deixa eu procurar...",
            "Calma, calma, o código chegou por mensagem, só um minutinho.",
        };

        private static readonly string[] FalasObrigado =
        {
            "Valeu, bom trabalho!",
            "Obrigado! Boa noite pra você.",
            "Tá quentinho, né? Brigadão!",
            "Show, obrigado pela entrega!",
        };

        private readonly IGameRandom _random;
        private readonly EntregaConfig _config;
        private readonly List<EntregaOpcao> _principais = new List<EntregaOpcao>();
        private readonly string[] _codigos = new string[3];
        private readonly bool[] _codigoTentado = new bool[3];
        private string[] _falasEspera = new string[3];
        private int _indiceCerto;
        private bool _semFoto;
        private bool _ligou;
        private Depois _depois;
        private int _esperaDeMinutos;

        private enum Depois
        {
            Entregar,
            Concluir,
            Codigos,
        }

        public Entrega(Order pedido, string cliente, IGameRandom random, EntregaConfig config = null)
        {
            Pedido = pedido ?? throw new ArgumentNullException(nameof(pedido));
            Cliente = cliente ?? string.Empty;
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _config = config ?? new EntregaConfig();
            FatorGorjeta = 1;
            GerarCodigos();
        }

        public Order Pedido { get; }

        public string Cliente { get; }

        public EntregaSituacao Situacao { get; private set; }

        public bool Iniciada { get; private set; }

        public EntregaPasso Passo { get; private set; }

        /// <summary>"Cliente" ou "Porteiro": quem fala.</summary>
        public string Interlocutor { get; private set; } = "Cliente";

        public string Fala { get; private set; } = string.Empty;

        /// <summary>Minutos de jogo da espera em andamento (ou da última espera).</summary>
        public int EsperaMinutos { get; private set; }

        /// <summary>Minutos de jogo perdidos até agora (esperas, ligações, códigos errados, discussão).</summary>
        public int MinutosPerdidos { get; private set; }

        /// <summary>Estrelas a menos na nota desta entrega.</summary>
        public int PenalidadeEstrelas { get; private set; }

        /// <summary>Multiplicador da gorjeta (1 normal; a gorjeta nunca passa de AppConfig.MaxTip).</summary>
        public double FatorGorjeta { get; private set; }

        public bool DeixouNaPortaria { get; private set; }

        public bool FotoTirada { get; private set; }

        public bool Concluida => Passo == EntregaPasso.Concluida;

        public string Codigo => _codigos[_indiceCerto];

        public IReadOnlyList<string> OpcoesCodigo => _codigos;

        /// <summary>Chance extra de "cliente diz que não recebeu" (deixou na portaria sem foto pesa mais).</summary>
        public double RiscoNaoRecebeu =>
            DeixouNaPortaria ? (FotoTirada ? _config.RiscoPortariaComFoto : _config.RiscoPortariaSemFoto) : 0;

        /// <summary>As respostas possíveis agora: as da situação mais "Tirar foto da entrega" quando cabe.</summary>
        public IReadOnlyList<EntregaOpcao> Opcoes => MontarOpcoes();

        private List<EntregaOpcao> MontarOpcoes()
        {
            var lista = new List<EntregaOpcao>();

            if (Passo == EntregaPasso.Atendimento)
            {
                lista.AddRange(_principais);

                if (!FotoTirada && !_semFoto)
                {
                    lista.Add(new EntregaOpcao(EntregaAcao.Fotografar, "Tirar foto da entrega"));
                }
            }

            return lista;
        }

        // ---------- Início ----------

        public void Iniciar()
        {
            Iniciar(Sortear(_random, _config));
        }

        public void Iniciar(EntregaSituacao situacao)
        {
            if (Iniciada)
            {
                throw new InvalidOperationException("A entrega já começou.");
            }

            Iniciada = true;
            Situacao = situacao;
            _principais.Clear();

            switch (situacao)
            {
                case EntregaSituacao.Portaria:
                    Interlocutor = "Porteiro";
                    Fala = Pick(FalasPortaria);
                    Atender(
                        new EntregaOpcao(EntregaAcao.DeixarNaPortaria, "Deixar com o porteiro"),
                        new EntregaOpcao(EntregaAcao.ChamarOCliente, "Pedir pro cliente descer"));
                    break;
                case EntregaSituacao.ClienteDemora:
                    EsperaMinutos = _random.Range(_config.DemoraMin, _config.DemoraMax + 1);
                    Fala = Pick(FalasDemora).Replace("{n}", EsperaMinutos.ToString());
                    _falasEspera = Pick(FalasEsperaDemora);
                    OpcoesDemora();
                    break;
                case EntregaSituacao.InterfoneQuebrado:
                    Fala = Pick(FalasInterfone);
                    _falasEspera = Pick(FalasEsperaInterfone);
                    Atender(
                        new EntregaOpcao(EntregaAcao.LigarNoApp, "Ligar pelo app"),
                        new EntregaOpcao(EntregaAcao.BaterPalmas, "Bater palmas no portão"),
                        new EntregaOpcao(EntregaAcao.EsperarMorador, "Esperar alguém abrir"));
                    break;
                case EntregaSituacao.PedeParaSubir:
                    Fala = Pick(FalasSubir);
                    Atender(
                        new EntregaOpcao(EntregaAcao.SubirAteOApartamento, "Subir até o apartamento"),
                        new EntregaOpcao(EntregaAcao.RecusarSubir, "Recusar educadamente"));
                    break;
                case EntregaSituacao.CodigoEntrega:
                    EsperaMinutos = _random.Range(_config.CodigoMin, _config.CodigoMax + 1);
                    Fala = Pick(FalasCodigo);
                    OpcoesCodigoEsquecido();
                    break;
                default:
                    ComecarEspera(_random.Range(_config.DesceMin, _config.DesceMax + 1), Pick(FalasDesce), Depois.Entregar);
                    break;
            }
        }

        public static EntregaSituacao Sortear(IGameRandom random, EntregaConfig config = null)
        {
            config = config ?? new EntregaConfig();
            var todas = (EntregaSituacao[])Enum.GetValues(typeof(EntregaSituacao));
            double total = 0;

            foreach (EntregaSituacao s in todas)
            {
                total += Math.Max(0, config.Peso(s));
            }

            double roll = random.NextDouble() * total;

            foreach (EntregaSituacao s in todas)
            {
                roll -= Math.Max(0, config.Peso(s));

                if (roll < 0)
                {
                    return s;
                }
            }

            return EntregaSituacao.ClienteDesce;
        }

        // ---------- Escolhas ----------

        public void Escolher(EntregaAcao acao, int indice = 0)
        {
            if (!Iniciada || Passo != EntregaPasso.Atendimento)
            {
                throw new InvalidOperationException("Não há o que escolher agora.");
            }

            EntregaOpcao opcao = MontarOpcoes().Find(o => o.Acao == acao && (acao != EntregaAcao.InformarCodigo || o.Indice == indice));

            if (opcao == null)
            {
                throw new InvalidOperationException("Esta escolha não está disponível: " + acao);
            }

            switch (acao)
            {
                case EntregaAcao.Fotografar:
                    FotoTirada = true;
                    break;
                case EntregaAcao.Entregar:
                    Concluir(Pick(FalasObrigado));
                    break;
                case EntregaAcao.DeixarNaPortaria:
                    DeixarNaPortaria();
                    break;
                case EntregaAcao.ChamarOCliente:
                    Interlocutor = "Cliente";
                    ComecarEspera(_random.Range(_config.DesceMin, _config.DesceMax + 1), Pick(FalasDesce), Depois.Entregar);
                    break;
                case EntregaAcao.Esperar:
                    ComecarEspera(Math.Max(1, EsperaMinutos), null, Situacao == EntregaSituacao.CodigoEntrega ? Depois.Codigos : Depois.Entregar);
                    break;
                case EntregaAcao.LigarNoApp:
                    Ligar();
                    break;
                case EntregaAcao.BaterPalmas:
                    BaterPalmas();
                    break;
                case EntregaAcao.EsperarMorador:
                    ComecarEspera(_random.Range(2, 5), null, Depois.Entregar);
                    break;
                case EntregaAcao.SubirAteOApartamento:
                    FatorGorjeta = _config.FatorGorjetaSubir;
                    ComecarEspera(_random.Range(_config.SubirMin, _config.SubirMax + 1), Pick(FalasEsperaSubir), Depois.Concluir);
                    break;
                case EntregaAcao.RecusarSubir:
                    Discutir();
                    break;
                case EntregaAcao.MantemPosicao:
                    MantemPosicao();
                    break;
                case EntregaAcao.CederESubir:
                    FatorGorjeta = _config.FatorGorjetaCedeu;
                    ComecarEspera(_random.Range(_config.SubirMin, _config.SubirMax + 1), Pick(FalasEsperaSubir), Depois.Concluir);
                    break;
                default:
                    EscolherCodigo(indice);
                    break;
            }
        }

        /// <summary>A fala certa para o ponto da espera (progresso de 0 a 1): começo, meio e fim.</summary>
        public string FalaEspera(double progresso)
        {
            if (Passo != EntregaPasso.Espera)
            {
                return Fala;
            }

            int i = progresso < 0.34 ? 0 : progresso < 0.67 ? 1 : 2;

            return _falasEspera[i].Replace("{codigo}", Codigo);
        }

        /// <summary>O relógio da cena contou os minutos da espera.</summary>
        public void ConcluirEspera()
        {
            if (Passo != EntregaPasso.Espera)
            {
                throw new InvalidOperationException("Não há espera em andamento.");
            }

            MinutosPerdidos += _esperaDeMinutos;
            _principais.Clear();
            Passo = EntregaPasso.Atendimento;
            string ultima = _falasEspera[2].Replace("{codigo}", Codigo);

            switch (_depois)
            {
                case Depois.Concluir:
                    Concluir(ultima);
                    break;
                case Depois.Codigos:
                    Fala = ultima;
                    _semFoto = true;
                    AdicionarOpcoesCodigo();
                    break;
                default:
                    Fala = ultima;
                    Interlocutor = "Cliente";
                    _principais.Add(new EntregaOpcao(EntregaAcao.Entregar, "Entregar o pedido"));
                    break;
            }
        }

        // ---------- Internos ----------

        private void Atender(params EntregaOpcao[] opcoes)
        {
            Passo = EntregaPasso.Atendimento;
            _principais.Clear();
            _principais.AddRange(opcoes);
        }

        private void ComecarEspera(int minutos, string[] falas, Depois depois)
        {
            Passo = EntregaPasso.Espera;
            _esperaDeMinutos = minutos;
            EsperaMinutos = minutos;
            _depois = depois;
            _principais.Clear();

            if (falas != null)
            {
                _falasEspera = falas;
            }
            else if (Situacao == EntregaSituacao.CodigoEntrega)
            {
                _falasEspera = new[]
                {
                    "Ele rola a tela do celular...",
                    "Abre o app, fecha, abre de novo...",
                    "Achei! O código é {codigo}.",
                };
            }
            else if (_falasEspera == null || _falasEspera[0] == null)
            {
                _falasEspera = Pick(FalasEsperaInterfone);
            }

            Fala = _falasEspera[0].Replace("{codigo}", Codigo);
        }

        private void Concluir(string fala)
        {
            Passo = EntregaPasso.Concluida;
            Fala = fala;
            _principais.Clear();
        }

        private void OpcoesDemora()
        {
            Atender(new EntregaOpcao(EntregaAcao.Esperar, $"Esperar ({EsperaMinutos} min)"));

            if (!_ligou)
            {
                _principais.Add(new EntregaOpcao(EntregaAcao.LigarNoApp, "Ligar pelo app"));
            }

            _principais.Add(new EntregaOpcao(EntregaAcao.DeixarNaPortaria, "Deixar na portaria (arriscado)"));
        }

        private void OpcoesCodigoEsquecido()
        {
            Atender(new EntregaOpcao(EntregaAcao.Esperar, $"Esperar o cliente achar ({EsperaMinutos} min)"));

            if (!_ligou)
            {
                _principais.Add(new EntregaOpcao(EntregaAcao.LigarNoApp, "Ligar pelo app"));
            }
        }

        private void Ligar()
        {
            _ligou = true;
            MinutosPerdidos += _config.MinutosLigar;

            switch (Situacao)
            {
                case EntregaSituacao.ClienteDemora:
                    if (_random.Chance(_config.ChanceLigarAtende))
                    {
                        ComecarEspera(Math.Max(1, EsperaMinutos / 2), new[] { "Atendeu: 'Tô descendo, juro!'", "O elevador chegou, dá pra ouvir ao fundo.", "Ele sai correndo pelo portão." }, Depois.Entregar);
                    }
                    else
                    {
                        Fala = "Chamou, chamou e caiu na caixa postal.";
                        OpcoesDemora();
                    }

                    break;
                case EntregaSituacao.CodigoEntrega:
                    if (_random.Chance(_config.ChanceCodigoPeloApp))
                    {
                        _falasEspera = new[] { "Você liga: 'Vou te mandar o código por mensagem!'", "A mensagem chega no seu app...", "Chegou! O código é {codigo}." };
                        ComecarEspera(1, _falasEspera, Depois.Codigos);
                    }
                    else
                    {
                        Fala = "A ligação cai e o cliente volta a procurar sozinho.";
                        MinutosPerdidos += 1;
                        OpcoesCodigoEsquecido();
                    }

                    break;
                default:
                    if (_random.Chance(_config.ChanceLigarInterfone))
                    {
                        ComecarEspera(_random.Range(1, 4), new[] { "Ele atende: 'Quebrou de novo? Já desço!'", "Você ouve a porta do apartamento fechando.", "O cliente aparece no portão." }, Depois.Entregar);
                    }
                    else
                    {
                        Fala = "Ninguém atende pelo app. Melhor tentar outra coisa.";
                        _principais.RemoveAll(o => o.Acao == EntregaAcao.LigarNoApp);
                    }

                    break;
            }
        }

        private void BaterPalmas()
        {
            if (_random.Chance(_config.ChancePalmas))
            {
                ComecarEspera(_random.Range(1, 3), new[] { "O cliente abre a janela: 'já desço!'", "Ouve-se passos na escada.", "O portão se abre e ele chega." }, Depois.Entregar);

                return;
            }

            MinutosPerdidos += 1;
            Fala = "Nada. Os cachorros da rua latem mais alto que as suas palmas.";
            _principais.RemoveAll(o => o.Acao == EntregaAcao.BaterPalmas);
        }

        private void DeixarNaPortaria()
        {
            DeixouNaPortaria = true;

            if (Situacao == EntregaSituacao.ClienteDemora)
            {
                FatorGorjeta = 0;

                if (_random.Chance(_config.ChancePenalidadePortaria))
                {
                    PenalidadeEstrelas = 1;
                }

                Concluir("Você deixa com o porteiro e vai embora. O cliente não vai gostar muito.");

                return;
            }

            FatorGorjeta = _config.FatorGorjetaPortaria;
            Interlocutor = "Porteiro";
            ComecarEspera(_random.Range(_config.PortariaMin, _config.PortariaMax + 1), Pick(FalasPorteiro), Depois.Concluir);
        }

        private void Discutir()
        {
            Interlocutor = "Cliente";
            MinutosPerdidos += _random.Range(_config.DiscussaoMin, _config.DiscussaoMax + 1);
            _semFoto = true;
            Fala = Pick(FalasDiscussao);
            Atender(
                new EntregaOpcao(EntregaAcao.MantemPosicao, "Manter: não posso subir"),
                new EntregaOpcao(EntregaAcao.CederESubir, "Ceder e subir"));
        }

        private void MantemPosicao()
        {
            double roll = _random.NextDouble();

            if (roll < _config.ChanceDesceMesmoAssim)
            {
                FatorGorjeta = _config.FatorGorjetaDesceBufando;
                ComecarEspera(_random.Range(2, 5), new[] { "O cliente resmunga e pega a chave.", "Ele desce as escadas pisando forte.", "'Aqui, me dá isso.' Ele pega o pedido sem sorrir." }, Depois.Concluir);

                return;
            }

            if (roll < _config.ChanceDesceMesmoAssim + _config.ChanceReclama)
            {
                PenalidadeEstrelas = 1;
                FatorGorjeta = 0;
                Concluir("'Vou reclamar no app!' O cliente bate a porta. Você deixa o pedido na portaria.");
                DeixouNaPortaria = true;

                return;
            }

            FatorGorjeta = 0.6;
            Concluir("Ele respira fundo: 'Tá bom, tá bom, eu desço.' Pega o pedido e agradece de má vontade.");
        }

        private void EscolherCodigo(int indice)
        {
            _codigoTentado[indice] = true;

            if (indice == _indiceCerto)
            {
                Concluir("Confere! O app libera a entrega. Valeu!");

                return;
            }

            MinutosPerdidos += _config.MinutosCodigoErrado;
            Fala = "O app recusou: esse não é o código. Confere de novo.";
            AdicionarOpcoesCodigo();
        }

        private void AdicionarOpcoesCodigo()
        {
            Passo = EntregaPasso.Atendimento;
            _principais.Clear();

            for (int i = 0; i < _codigos.Length; i++)
            {
                if (!_codigoTentado[i])
                {
                    _principais.Add(new EntregaOpcao(EntregaAcao.InformarCodigo, _codigos[i], i));
                }
            }
        }

        private string Pick(string[] opcoes) => opcoes[_random.Range(0, opcoes.Length)];

        private string[] Pick(string[][] opcoes) => opcoes[_random.Range(0, opcoes.Length)];

        private void GerarCodigos()
        {
            int certo = _random.Range(1000, 10000);
            _indiceCerto = _random.Range(0, 3);
            var usados = new HashSet<int> { certo };
            var errados = new List<int>();

            for (int tentativa = 0; errados.Count < 2; tentativa++)
            {
                int posicao = _random.Range(0, 4);
                int divisor = 1;

                for (int i = 0; i < posicao; i++)
                {
                    divisor *= 10;
                }

                int digito = certo / divisor % 10;
                int novo = (digito + 1 + _random.Range(0, 9) + tentativa / 8) % 10;
                int candidato = certo + (novo - digito) * divisor;

                if (candidato >= 1000 && candidato <= 9999 && usados.Add(candidato))
                {
                    errados.Add(candidato);
                }
            }

            int proximo = 0;

            for (int i = 0; i < 3; i++)
            {
                _codigos[i] = (i == _indiceCerto ? certo : errados[proximo++]).ToString();
            }
        }
    }
}
