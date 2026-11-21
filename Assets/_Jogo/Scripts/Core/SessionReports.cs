using System.Collections.Generic;

namespace Entregador.Core
{
    /// <summary>O que uma entrega concluída rendeu. O imprevisto pós-entrega, se houver, vem junto para a cena mostrar.</summary>
    public sealed class DeliveryResult
    {
        public DeliveryResult(Order order, Money pay, Money tip, int stars, bool spoiled, bool skipped, Incident afterDeliveryIncident)
        {
            Order = order;
            Pay = pay;
            Tip = tip;
            Stars = stars;
            Spoiled = spoiled;
            Skipped = skipped;
            AfterDeliveryIncident = afterDeliveryIncident;
        }

        public Order Order { get; }

        public Money Pay { get; }

        public Money Tip { get; }

        public Money Total => Pay + Tip;

        public int Stars { get; }

        /// <summary>A comida chegou estragada: sem pagamento e nota 1.</summary>
        public bool Spoiled { get; }

        /// <summary>A corrida foi pulada (entrega automática).</summary>
        public bool Skipped { get; }

        public Incident AfterDeliveryIncident { get; }
    }

    public enum WeekResult
    {
        SemanaBoa,
        Sobreviveu,
        NoVermelho,

        /// <summary>A semana acabou com a moto no pátio ou apreendida e o jogador sem moto para trabalhar.</summary>
        SemMoto,
    }

    /// <summary>Fechamento da semana: bônus, multas, aluguel, caução devolvida e o veredito.</summary>
    public sealed class WeekReport
    {
        public WeekReport(int deliveries, Money bonus, Money fines, Money rent, Money depositRefund, Money finalBalance, double rating, WeekResult result)
        {
            Deliveries = deliveries;
            Bonus = bonus;
            Fines = fines;
            Rent = rent;
            DepositRefund = depositRefund;
            FinalBalance = finalBalance;
            Rating = rating;
            Result = result;
        }

        public int Deliveries { get; }

        public Money Bonus { get; }

        public Money Fines { get; }

        public Money Rent { get; }

        public Money DepositRefund { get; }

        public Money FinalBalance { get; }

        public double Rating { get; }

        public WeekResult Result { get; }

        /// <summary>Resumo do trânsito: multas, CNH, moto e blitz. Preenchido pela GameSession.</summary>
        public TransitoReport Transito { get; internal set; }
    }

    /// <summary>Uma abordagem que aconteceu na semana e como terminou.</summary>
    public sealed class AbordagemRegistro
    {
        public AbordagemRegistro(int dia, AbordagemMotivo motivo, AbordagemChoice escolha, AbordagemDesfecho desfecho)
        {
            Dia = dia;
            Motivo = motivo;
            Escolha = escolha;
            Desfecho = desfecho;
        }

        public int Dia { get; }

        public AbordagemMotivo Motivo { get; }

        public AbordagemChoice Escolha { get; }

        public AbordagemDesfecho Desfecho { get; }
    }

    /// <summary>Parte do relatório da semana sobre trânsito: o que foi pago, o que ainda vem, CNH, moto e blitz.</summary>
    public sealed class TransitoReport
    {
        public TransitoReport(Money multasPagas, Money multasPendentes, int quantidadePendentes, int pontosCnh, CnhStatus cnh,
            MotoOrigem origemMoto, MotoStatus motoStatus, Money patioDevido, IReadOnlyList<MultaRegistro> multas,
            IReadOnlyList<AbordagemRegistro> abordagens)
        {
            MultasPagas = multasPagas;
            MultasPendentes = multasPendentes;
            QuantidadePendentes = quantidadePendentes;
            PontosCnh = pontosCnh;
            Cnh = cnh;
            OrigemMoto = origemMoto;
            MotoStatus = motoStatus;
            PatioDevido = patioDevido;
            Multas = multas;
            Abordagens = abordagens;
        }

        /// <summary>Multas de trânsito já cobradas (inclui taxa de indicação, não inclui as multas fixas legadas).</summary>
        public Money MultasPagas { get; }

        /// <summary>Multas que ainda vão chegar (a semana acabou antes).</summary>
        public Money MultasPendentes { get; }

        public int QuantidadePendentes { get; }

        public int PontosCnh { get; }

        public CnhStatus Cnh { get; }

        public MotoOrigem OrigemMoto { get; }

        public MotoStatus MotoStatus { get; }

        /// <summary>O que custaria tirar a moto do pátio hoje (zero se ela não está lá).</summary>
        public Money PatioDevido { get; }

        public IReadOnlyList<MultaRegistro> Multas { get; }

        public IReadOnlyList<AbordagemRegistro> Abordagens { get; }

        public int Blitz => Abordagens.Count;
    }
}
