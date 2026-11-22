using System;
using System.Collections.Generic;

namespace Entregador.Core
{
    /// <summary>Números da agenda de pedidos: espera entre ofertas, picos, zonas quentes e ofertas durante a corrida.</summary>
    public sealed class OfertaConfig
    {
        /// <summary>Espera base (segundos reais) até o próximo pedido, de Min a Max.</summary>
        public double EsperaMin = 25;

        public double EsperaMax = 90;

        /// <summary>Piso da espera depois de todos os fatores.</summary>
        public double EsperaMinima = 8;

        /// <summary>Multiplicador da espera na chuva (mais gente pede em casa).</summary>
        public double FatorChuva = 0.75;

        /// <summary>Multiplicador da espera no almoço e no jantar.</summary>
        public double FatorPico = 0.7;

        /// <summary>Estar numa zona quente encurta a espera em 30%.</summary>
        public double FatorZonaQuente = 0.7;

        /// <summary>Abaixo desta taxa de aceite o app demora mais para chamar.</summary>
        public double LimiteAceite = 0.8;

        /// <summary>Maior multiplicador da espera por taxa de aceite baixa (taxa zero).</summary>
        public double FatorAceiteMax = 1.8;

        public double AlmocoInicio = 11;
        public double AlmocoFim = 14;
        public double JantarInicio = 18;
        public double JantarFim = 21;

        /// <summary>Chance de uma corrida receber uma segunda oferta no caminho.</summary>
        public double ChanceOfertaNaCorrida = 0.5;

        /// <summary>A partir deste progresso da rota (60%) a oferta é bem mais provável.</summary>
        public double ProgressoAlto = 0.6;

        /// <summary>Das ofertas no meio da corrida, esta fração cai depois de ProgressoAlto.</summary>
        public double PesoProgressoAlto = 0.7;

        /// <summary>Chance de a oferta extra ser do mesmo restaurante (pedido duplo).</summary>
        public double ChancePedidoDuplo = 0.18;

        /// <summary>Segundos reais para responder à oferta no meio da corrida.</summary>
        public double SegundosOfertaNaCorrida = 12;
    }

    /// <summary>O que influencia a espera pelo próximo pedido.</summary>
    public struct OfertaContexto
    {
        public bool Chuva;

        /// <summary>Hora do dia em horas decimais.</summary>
        public double Hora;

        /// <summary>Taxa de aceite de 0 a 1.</summary>
        public double TaxaAceite;

        public bool NaZonaQuente;
    }

    /// <summary>Quando o app chama: espera entre pedidos e ofertas no meio da corrida. Só regras; o tempo corre na cena.</summary>
    public static class OfertaAgenda
    {
        /// <summary>Almoço (11 a 14 h) ou jantar (18 a 21 h).</summary>
        public static bool EmPico(double hora, OfertaConfig config = null)
        {
            config = config ?? new OfertaConfig();

            return (hora >= config.AlmocoInicio && hora < config.AlmocoFim) || (hora >= config.JantarInicio && hora < config.JantarFim);
        }

        /// <summary>Multiplicador da espera: chuva e pico encurtam, taxa de aceite baixa alonga. A zona quente é à parte (VelocidadeDaEspera).</summary>
        public static double Fator(OfertaContexto contexto, OfertaConfig config = null)
        {
            config = config ?? new OfertaConfig();
            double fator = 1;

            if (contexto.Chuva)
            {
                fator *= config.FatorChuva;
            }

            if (EmPico(contexto.Hora, config))
            {
                fator *= config.FatorPico;
            }

            if (contexto.TaxaAceite < config.LimiteAceite)
            {
                double falta = (config.LimiteAceite - Math.Max(0, contexto.TaxaAceite)) / config.LimiteAceite;
                fator *= 1 + falta * (config.FatorAceiteMax - 1);
            }

            return fator;
        }

        /// <summary>Sorteia a espera (segundos reais) até a próxima oferta, sem a zona quente.</summary>
        public static double SortearEspera(IGameRandom random, OfertaContexto contexto, OfertaConfig config = null)
        {
            config = config ?? new OfertaConfig();
            double espera = random.Between(config.EsperaMin, config.EsperaMax) * Fator(contexto, config);

            return Math.Max(config.EsperaMinima, espera);
        }

        /// <summary>Quanto a espera anda por segundo real: dentro de uma zona quente ela termina 30% antes (1 / 0,7).</summary>
        public static double VelocidadeDaEspera(bool naZonaQuente, OfertaConfig config = null)
        {
            config = config ?? new OfertaConfig();

            return naZonaQuente ? 1.0 / config.FatorZonaQuente : 1.0;
        }

        /// <summary>A corrida vai receber uma oferta extra? Menos provável com taxa de aceite baixa, mais no pico e na chuva.</summary>
        public static bool SortearOfertaNaCorrida(IGameRandom random, OfertaContexto contexto, OfertaConfig config = null)
        {
            config = config ?? new OfertaConfig();
            double chance = config.ChanceOfertaNaCorrida;

            if (contexto.TaxaAceite < config.LimiteAceite)
            {
                chance *= 0.6;
            }

            if (contexto.Chuva || EmPico(contexto.Hora, config))
            {
                chance *= 1.3;
            }

            return random.Chance(Math.Min(0.9, chance));
        }

        /// <summary>Em que ponto da rota (0 a 1) a oferta extra chega: 70% das vezes depois dos 60% do caminho.</summary>
        public static double SortearProgressoDaOferta(IGameRandom random, OfertaConfig config = null)
        {
            config = config ?? new OfertaConfig();

            return random.Chance(config.PesoProgressoAlto)
                ? random.Between(config.ProgressoAlto, 0.9)
                : random.Between(0.15, config.ProgressoAlto);
        }

        /// <summary>A oferta extra é do mesmo restaurante?</summary>
        public static bool SortearPedidoDuplo(IGameRandom random, OfertaConfig config = null)
        {
            config = config ?? new OfertaConfig();

            return random.Chance(config.ChancePedidoDuplo);
        }
    }

    /// <summary>Área de movimento alto no mapa (em volta de um aglomerado de restaurantes).</summary>
    public sealed class ZonaQuente
    {
        public ZonaQuente(string nome, double x, double z, double raio)
        {
            Nome = nome;
            X = x;
            Z = z;
            Raio = raio;
        }

        public string Nome { get; }

        public double X { get; }

        public double Z { get; }

        public double Raio { get; }

        public bool Contem(double x, double z)
        {
            double dx = x - X;
            double dz = z - Z;

            return dx * dx + dz * dz <= Raio * Raio;
        }
    }

    public static class ZonasQuentes
    {
        /// <summary>Escolhe de minimo a maximo zonas distintas entre as candidatas.</summary>
        public static List<ZonaQuente> Sortear(IGameRandom random, IReadOnlyList<ZonaQuente> candidatas, int minimo = 1, int maximo = 3)
        {
            var sobra = new List<ZonaQuente>(candidatas);
            var escolhidas = new List<ZonaQuente>();
            int quantas = Math.Min(sobra.Count, random.Range(minimo, maximo + 1));

            for (int i = 0; i < quantas; i++)
            {
                int indice = random.Range(0, sobra.Count);
                escolhidas.Add(sobra[indice]);
                sobra.RemoveAt(indice);
            }

            return escolhidas;
        }

        /// <summary>A primeira zona que contém o ponto, ou null.</summary>
        public static ZonaQuente Qual(IReadOnlyList<ZonaQuente> zonas, double x, double z)
        {
            for (int i = 0; i < zonas.Count; i++)
            {
                if (zonas[i].Contem(x, z))
                {
                    return zonas[i];
                }
            }

            return null;
        }
    }
}
