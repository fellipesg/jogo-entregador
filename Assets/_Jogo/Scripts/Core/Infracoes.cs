using System;

namespace Entregador.Core
{
    public enum Infracao
    {
        ExcessoVelocidadeAte20,
        ExcessoVelocidade20a50,
        ExcessoVelocidadeAcima50,
        Contramao,
        FaixaExclusivaOnibus,
        AvancarSinalVermelho,
        DirigirSemCnh,
        MotoSemLicenciamento,
        FugaDeAbordagem,
        EquipamentoIrregular,
    }

    public enum GravidadeInfracao
    {
        Media,
        Grave,
        Gravissima,
    }

    /// <summary>Valor, pontos e consequências administrativas de uma infração (tabela do CTB).</summary>
    public readonly struct RegraInfracao
    {
        public readonly string Descricao;
        public readonly Money Valor;
        public readonly int Pontos;
        public readonly GravidadeInfracao Gravidade;

        /// <summary>Suspensão imediata do direito de dirigir (excesso de velocidade acima de 50%).</summary>
        public readonly bool SuspendeDireito;

        /// <summary>O veículo fica retido/removido ao pátio.</summary>
        public readonly bool RetemVeiculo;

        /// <summary>A infração é do veículo (dono), não do condutor: moto "bob" não tem dono no registro.</summary>
        public readonly bool DoVeiculo;

        public RegraInfracao(string descricao, decimal valor, int pontos, GravidadeInfracao gravidade,
            bool suspendeDireito = false, bool retemVeiculo = false, bool doVeiculo = false)
        {
            Descricao = descricao;
            Valor = Money.Reais(valor);
            Pontos = pontos;
            Gravidade = gravidade;
            SuspendeDireito = suspendeDireito;
            RetemVeiculo = retemVeiculo;
            DoVeiculo = doVeiculo;
        }
    }

    /// <summary>Tabela de infrações e regras de pontuação da CNH. Valores reais do CTB.</summary>
    public static class CodigoTransito
    {
        /// <summary>Tolerância do radar até 100 km/h: desconta-se 7 km/h da velocidade medida. Acima disso, 7%.</summary>
        public const double ToleranciaRadarKmh = 7;

        public const double LimiteToleranciaFixa = 100;

        public const double ToleranciaRadarPercentual = 0.07;

        public static RegraInfracao Regra(Infracao infracao)
        {
            switch (infracao)
            {
                case Infracao.ExcessoVelocidadeAte20:
                    return new RegraInfracao("Excesso de velocidade até 20%", 130.16m, 4, GravidadeInfracao.Media);

                case Infracao.ExcessoVelocidade20a50:
                    return new RegraInfracao("Excesso de velocidade de 20% a 50%", 195.23m, 5, GravidadeInfracao.Grave);

                case Infracao.ExcessoVelocidadeAcima50:
                    return new RegraInfracao("Excesso de velocidade acima de 50%", 880.41m, 7, GravidadeInfracao.Gravissima, suspendeDireito: true);

                case Infracao.Contramao:
                    return new RegraInfracao("Contramão (art. 186, II)", 293.47m, 7, GravidadeInfracao.Gravissima);

                case Infracao.FaixaExclusivaOnibus:
                    return new RegraInfracao("Faixa exclusiva de ônibus (art. 184, III)", 293.47m, 7, GravidadeInfracao.Gravissima);

                case Infracao.AvancarSinalVermelho:
                    return new RegraInfracao("Avançar o sinal vermelho (art. 208)", 293.47m, 7, GravidadeInfracao.Gravissima);

                case Infracao.DirigirSemCnh:
                    return new RegraInfracao("Dirigir sem CNH (art. 162, I)", 880.41m, 7, GravidadeInfracao.Gravissima, retemVeiculo: true);

                case Infracao.MotoSemLicenciamento:
                    return new RegraInfracao("Moto sem licenciamento (art. 230, V)", 293.47m, 7, GravidadeInfracao.Gravissima, retemVeiculo: true, doVeiculo: true);

                case Infracao.FugaDeAbordagem:
                    return new RegraInfracao("Desobediência à ordem de parada (art. 195)", 293.47m, 7, GravidadeInfracao.Gravissima, retemVeiculo: true);

                case Infracao.EquipamentoIrregular:
                    return new RegraInfracao("Equipamento obrigatório irregular (art. 230, XVIII)", 195.23m, 5, GravidadeInfracao.Grave);

                default:
                    throw new ArgumentOutOfRangeException(nameof(infracao), infracao, null);
            }
        }

        /// <summary>
        /// Classifica o excesso de velocidade medido por radar. Aplica a tolerância do CTB (7 km/h até 100 km/h;
        /// 7% acima disso) e devolve null se a velocidade considerada não passa do limite.
        /// </summary>
        public static Infracao? ClassificarVelocidade(double medidaKmh, double limiteKmh)
        {
            double considerada = medidaKmh <= LimiteToleranciaFixa
                ? medidaKmh - ToleranciaRadarKmh
                : medidaKmh * (1 - ToleranciaRadarPercentual);

            if (limiteKmh <= 0 || considerada <= limiteKmh)
            {
                return null;
            }

            double excesso = (considerada - limiteKmh) / limiteKmh;

            if (excesso <= 0.20)
            {
                return Infracao.ExcessoVelocidadeAte20;
            }

            if (excesso <= 0.50)
            {
                return Infracao.ExcessoVelocidade20a50;
            }

            return Infracao.ExcessoVelocidadeAcima50;
        }

        /// <summary>
        /// Limite de pontos da suspensão: 40 sem infração gravíssima, 30 com uma, 20 com duas ou mais.
        /// Simplificação: a janela de 12 meses é o save inteiro. Esta é a única função com a regra.
        /// </summary>
        public static bool DeveSuspender(int pontos, int gravissimas)
        {
            int limite = gravissimas >= 2 ? 20 : gravissimas == 1 ? 30 : 40;

            return pontos >= limite;
        }
    }

    public enum CnhStatus
    {
        Valida,
        Suspensa,
        SemCnh,
    }

    /// <summary>A CNH do jogador: pontos e suspensão. Quem nunca tirou a carteira nunca acumula pontos.</summary>
    public sealed class Carteira
    {
        public Carteira(bool temCnh)
        {
            TemCnh = temCnh;
        }

        public bool TemCnh { get; }

        public int Pontos { get; private set; }

        public int Gravissimas { get; private set; }

        public bool Suspensa { get; private set; }

        /// <summary>Habilitado e não suspenso. Suspensa conta como "sem CNH" nas abordagens.</summary>
        public bool Valida => TemCnh && !Suspensa;

        public CnhStatus Status => !TemCnh ? CnhStatus.SemCnh : Suspensa ? CnhStatus.Suspensa : CnhStatus.Valida;

        /// <summary>Soma os pontos da infração e suspende se passar do limite. Sem CNH, não faz nada.</summary>
        public void Registrar(Infracao infracao)
        {
            if (!TemCnh)
            {
                return;
            }

            RegraInfracao regra = CodigoTransito.Regra(infracao);
            Pontos += regra.Pontos;

            if (regra.Gravidade == GravidadeInfracao.Gravissima)
            {
                Gravissimas++;
            }

            if (regra.SuspendeDireito || CodigoTransito.DeveSuspender(Pontos, Gravissimas))
            {
                Suspensa = true;
            }
        }
    }

    public enum MotoOrigem
    {
        Alugada,
        Bob,
    }

    public enum MotoStatus
    {
        Ok,
        NoPatio,
        Apreendida,
    }

    /// <summary>Como o jogador começa: que moto usa e se tem carteira de motorista categoria A.</summary>
    public sealed class PerfilInicial
    {
        public MotoOrigem Origem = MotoOrigem.Alugada;
        public bool TemCnh = true;

        public static PerfilInicial Padrao => new PerfilInicial();
    }

    /// <summary>Uma multa registrada (paga ou na fila para chegar).</summary>
    public sealed class MultaRegistro
    {
        public MultaRegistro(Infracao infracao, int diaInfracao, int diaChegada, Money valor, Money taxa, bool noRadar)
        {
            Infracao = infracao;
            DiaInfracao = diaInfracao;
            DiaChegada = diaChegada;
            Valor = valor;
            Taxa = taxa;
            NoRadar = noRadar;
        }

        public Infracao Infracao { get; }

        public int DiaInfracao { get; }

        /// <summary>Dia em que a notificação chega pelo app/correio (igual ao da infração se foi cobrada na hora).</summary>
        public int DiaChegada { get; }

        public Money Valor { get; }

        /// <summary>Taxa de indicação cobrada pela locadora.</summary>
        public Money Taxa { get; }

        public bool NoRadar { get; }

        public Money Total => Valor + Taxa;
    }
}
