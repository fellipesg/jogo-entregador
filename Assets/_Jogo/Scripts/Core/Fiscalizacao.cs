using System.Text;

namespace Entregador.Core
{
    /// <summary>Limites de velocidade das ruas: a tag maxspeed do OpenStreetMap ou o costume de São Paulo.</summary>
    public static class LimitesDeVelocidade
    {
        /// <summary>Avenida (via arterial): 50 km/h.</summary>
        public const int Avenida = 50;

        /// <summary>Via coletora (secundária/terciária): 40 km/h.</summary>
        public const int Coletora = 40;

        /// <summary>Rua local ou residencial: 30 km/h.</summary>
        public const int Local = 30;

        /// <summary>Lê a tag maxspeed ("50", "40 km/h"). Devolve 0 para mph, "signals", vazio ou valores fora de 10 a 120.</summary>
        public static int LerTag(string tag)
        {
            if (string.IsNullOrEmpty(tag) || tag.Contains("mph"))
            {
                return 0;
            }

            var digits = new StringBuilder();

            foreach (char c in tag)
            {
                if (char.IsDigit(c))
                {
                    digits.Append(c);
                }
                else if (digits.Length > 0)
                {
                    break;
                }
            }

            return int.TryParse(digits.ToString(), out int value) && value >= 10 && value <= 120 ? value : 0;
        }

        /// <summary>O limite da tag, se existir; senão o padrão da classe da via.</summary>
        public static int Resolver(int tag, int padrao) => tag > 0 ? tag : padrao;
    }

    /// <summary>Quando a polícia que vê a moto autua em flagrante.</summary>
    public static class Flagrantes
    {
        /// <summary>
        /// Qual infração a polícia vê agora, se houver (ordem de gravidade percebida: contramão, faixa de ônibus, velocidade).
        /// Velocidade só conta se passar de (1 + tolerancia) vezes o limite e o excesso gerar infração pela regra do CTB.
        /// </summary>
        public static Infracao? Avaliar(bool contramao, bool faixaOnibus, double velocidadeKmh, double limiteKmh, double tolerancia = 0.2)
        {
            if (contramao)
            {
                return Infracao.Contramao;
            }

            if (faixaOnibus)
            {
                return Infracao.FaixaExclusivaOnibus;
            }

            if (velocidadeKmh > limiteKmh * (1 + tolerancia))
            {
                return CodigoTransito.ClassificarVelocidade(velocidadeKmh, limiteKmh);
            }

            return null;
        }
    }
}
