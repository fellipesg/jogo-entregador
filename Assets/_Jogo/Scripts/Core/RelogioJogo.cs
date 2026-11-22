using System;
using System.Globalization;

namespace Entregador.Core
{
    /// <summary>
    /// O relógio do jogo: dia e hora do dia, uma só fonte para corridas, esperas, turno e HUD.
    /// Compressão: 1 minuto de jogo = SegundosPorMinuto (8) segundos reais. Assim um cliente que "desce em 3 min" faz o jogador esperar
    /// uns 24 s de verdade, e um turno de 4 h dura cerca de 32 min reais. O dia começa às 08:00 e para de andar à 23:59.
    /// </summary>
    public sealed class RelogioJogo
    {
        /// <summary>Segundos reais que valem 1 minuto de jogo.</summary>
        public const float SegundosPorMinuto = 8f;

        /// <summary>Hora em que cada dia começa.</summary>
        public const double HoraInicioDia = 8.0;

        public const double MinutosNoDia = 24 * 60 - 1;

        public RelogioJogo(int dia = 1)
        {
            Reiniciar(dia);
        }

        /// <summary>Dia da semana, de 1 a 7.</summary>
        public int Dia { get; private set; }

        /// <summary>Minutos desde a meia-noite.</summary>
        public double Minutos { get; private set; }

        /// <summary>Hora do dia em horas decimais (14,5 = 14:30).</summary>
        public double Hora => Minutos / 60.0;

        public bool FimDoDia => Minutos >= MinutosNoDia;

        /// <summary>"14:05".</summary>
        public string Texto => Formatar(Minutos);

        /// <summary>Novo dia, às 08:00.</summary>
        public void Reiniciar(int dia)
        {
            Dia = dia;
            Minutos = HoraInicioDia * 60.0;
        }

        /// <summary>Anda segundos reais.</summary>
        public void Avancar(double segundosReais)
        {
            AvancarMinutos(Math.Max(0, segundosReais) / SegundosPorMinuto);
        }

        public void AvancarMinutos(double minutos)
        {
            Minutos = Math.Min(MinutosNoDia, Minutos + Math.Max(0, minutos));
        }

        public static double MinutosParaSegundos(double minutos) => minutos * SegundosPorMinuto;

        public static string Formatar(double minutos)
        {
            int total = (int)Math.Floor(Math.Max(0, minutos));

            return (total / 60 % 24).ToString("00", CultureInfo.InvariantCulture) + ":" + (total % 60).ToString("00", CultureInfo.InvariantCulture);
        }
    }
}
