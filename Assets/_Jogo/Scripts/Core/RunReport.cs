using System;
using System.Collections.Generic;

namespace Entregador.Core
{
    /// <summary>
    /// O que aconteceu numa corrida dirigida. A cena preenche; as regras transformam em dinheiro e nota.
    /// </summary>
    public sealed class RunReport
    {
        /// <summary>Quilômetros rodados, para o combustível.</summary>
        public double KmDriven;

        /// <summary>Tempo gasto dividido pelo tempo previsto. 1 é no horário; 1,5 é 50% atrasado.</summary>
        public double TimeRatio = 1;

        /// <summary>Estado da comida, de 0 (estragada) a 1 (perfeita). Cai com buraco, freada e batida.</summary>
        public double FoodCondition = 1;

        /// <summary>Dano novo na moto, de 0 a 1.</summary>
        public double DamageAdded;

        /// <summary>Multas fixas simples (legado): cada uma custa EconomyConfig.Fine, cobrada no fim da semana.</summary>
        public int Fines;

        /// <summary>Infrações capturadas por radar na corrida. A multa chega dias depois (só para moto alugada).</summary>
        public readonly List<Infracao> Capturas = new List<Infracao>();

        /// <summary>Registra uma passagem por radar. Devolve false se a velocidade, com a tolerância, não gerou infração.</summary>
        public bool AddRadar(double medidaKmh, double limiteKmh)
        {
            Infracao? infracao = CodigoTransito.ClassificarVelocidade(medidaKmh, limiteKmh);

            if (infracao.HasValue)
            {
                Capturas.Add(infracao.Value);
            }

            return infracao.HasValue;
        }

        /// <summary>Minutos de jogo perdidos no balcão do restaurante (espera, fila, conferência). Já estão no TimeRatio; fica aqui para o relatório.</summary>
        public int MinutosNoBalcao;

        /// <summary>Estrelas a menos na nota: pedido trocado ou faltando item levado sem conferir (ver Retirada).</summary>
        public int PenalidadeRetirada;

        /// <summary>Minutos de jogo perdidos na porta do cliente (espera, interfone, discussão). Já estão no TimeRatio; fica aqui para o relatório.</summary>
        public int MinutosNaEntrega;

        /// <summary>Estrelas a menos na nota: cliente irritado ou deixou na portaria de um cliente que esperava (ver Entrega).</summary>
        public int PenalidadeEntrega;

        /// <summary>Multiplicador da gorjeta vindo da conversa na porta (1 normal; subir ao apartamento paga melhor). Nunca passa de MaxTip.</summary>
        public double FatorGorjeta = 1;

        /// <summary>Chance extra de "cliente diz que não recebeu" (pedido deixado na portaria).</summary>
        public double RiscoNaoRecebeu;

        /// <summary>Registra um avanço de sinal vermelho pego por fotossensor (a multa chega dias depois, como a do radar).</summary>
        public void AddAvancoSinal()
        {
            Capturas.Add(Infracao.AvancarSinalVermelho);
        }

        /// <summary>O jogador fotografou a entrega na porta do cliente.</summary>
        public bool PhotoTaken;

        public static RunReport OnTime(double km, bool photoTaken = true) =>
            new RunReport { KmDriven = km, PhotoTaken = photoTaken };
    }

    public static class RatingRules
    {
        /// <summary>Abaixo disso a comida chega estragada e o cliente pede reembolso.</summary>
        public const double SpoiledBelow = 0.2;

        public static bool IsSpoiled(RunReport report) => report.FoodCondition < SpoiledBelow;

        /// <summary>Nota de 1 a 5 pela pontualidade, pelo estado da comida e pela chuva sem capa.</summary>
        public static int Stars(RunReport report, bool isRaining, bool hasRaincoat)
        {
            if (IsSpoiled(report))
            {
                return 1;
            }

            int stars = 5;

            if (report.TimeRatio > 1.5)
            {
                stars -= 2;
            }
            else if (report.TimeRatio > 1.2)
            {
                stars -= 1;
            }

            if (report.FoodCondition < 0.4)
            {
                stars -= 2;
            }
            else if (report.FoodCondition < 0.7)
            {
                stars -= 1;
            }

            if (isRaining && !hasRaincoat)
            {
                stars -= 1;
            }

            stars -= Math.Max(0, report.PenalidadeRetirada);
            stars -= Math.Max(0, report.PenalidadeEntrega);

            return Math.Max(1, stars);
        }
    }
}
