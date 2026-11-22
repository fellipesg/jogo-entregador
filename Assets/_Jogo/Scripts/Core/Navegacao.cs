using System;
using System.Collections.Generic;
using System.Globalization;

namespace Entregador.Core
{
    /// <summary>Manobra de navegação, como num GPS de carro.</summary>
    public enum Manobra
    {
        Nenhuma,
        SigaEmFrente,
        MantenhaADireita,
        MantenhaAEsquerda,
        VireADireita,
        VireAEsquerda,
        Retorno,
        RotatoriaSaida,
        Chegada,
    }

    /// <summary>Ponto da rota para a navegação (x = leste, z = norte, em metros).</summary>
    public struct NavPonto
    {
        public double X;
        public double Z;

        /// <summary>Nome da rua do trecho que sai deste ponto (vazio se o mapa não tem nome).</summary>
        public string RuaSaida;

        /// <summary>O ponto é um cruzamento (3 ou mais trechos).</summary>
        public bool Cruzamento;

        /// <summary>O trecho que sai deste ponto faz parte de uma rotatória.</summary>
        public bool Rotatoria;

        public NavPonto(double x, double z, string ruaSaida = "", bool cruzamento = false, bool rotatoria = false)
        {
            X = x;
            Z = z;
            RuaSaida = ruaSaida ?? string.Empty;
            Cruzamento = cruzamento;
            Rotatoria = rotatoria;
        }
    }

    /// <summary>Uma instrução da rota: o que fazer, onde e em que rua.</summary>
    public sealed class Instrucao
    {
        public Manobra Manobra { get; set; }

        /// <summary>Rotatória: número da saída (1 = a primeira).</summary>
        public int Saida { get; set; }

        /// <summary>Rua em que se entra depois da manobra ("rua sem nome" se o mapa não tem nome).</summary>
        public string Rua { get; set; } = Navegacao.SemNome;

        /// <summary>Rua de onde se vem.</summary>
        public string RuaAnterior { get; set; } = Navegacao.SemNome;

        /// <summary>A manobra troca de rua (ou o mapa não tem nome nos dois lados e não dá para saber).</summary>
        public bool TrocaDeRua { get; set; }

        /// <summary>Ângulo da curva em graus: positivo para a direita, negativo para a esquerda.</summary>
        public double Angulo { get; set; }

        /// <summary>Distância da manobra até o fim da rota, em metros. A distância até a manobra é o que falta da rota menos isto.</summary>
        public double MetrosAteOFim { get; set; }

        public double X { get; set; }

        public double Z { get; set; }

        /// <summary>Direção (plano, unitária) de chegada à manobra e de saída dela.</summary>
        public double EntradaX { get; set; }

        public double EntradaZ { get; set; }

        public double SaidaX { get; set; }

        public double SaidaZ { get; set; }

        /// <summary>Chegada: -1 destino à esquerda, 1 à direita, 0 de frente ou colado na rua.</summary>
        public int Lado { get; set; }

        /// <summary>Curva fechada (a placa de chevron aparece).</summary>
        public bool Aguda => (Manobra == Manobra.VireADireita || Manobra == Manobra.VireAEsquerda) && Math.Abs(Angulo) >= 70.0 || Manobra == Manobra.Retorno;

        /// <summary>Instrução curta no imperativo, com a rua quando ela muda: "Vire à direita na Rua X".</summary>
        public string Texto
        {
            get
            {
                string baseText = Verbo();
                string rua = TrocaDeRua && Manobra != Manobra.Chegada && Manobra != Manobra.Retorno ? " " + Navegacao.Preposicao(Rua) + " " + Rua : string.Empty;

                return baseText + rua;
            }
        }

        private string Verbo()
        {
            switch (Manobra)
            {
                case Manobra.VireADireita: return "Vire à direita";
                case Manobra.VireAEsquerda: return "Vire à esquerda";
                case Manobra.MantenhaADireita: return "Mantenha-se à direita";
                case Manobra.MantenhaAEsquerda: return "Mantenha-se à esquerda";
                case Manobra.Retorno: return "Faça o retorno";
                case Manobra.RotatoriaSaida: return "Na rotatória, pegue a " + Saida + "ª saída";
                case Manobra.SigaEmFrente: return "Siga em frente";
                case Manobra.Chegada: return Lado > 0 ? "Você chegou. O destino está à direita" : Lado < 0 ? "Você chegou. O destino está à esquerda" : "Você chegou ao destino";
                default: return string.Empty;
            }
        }
    }

    /// <summary>Calcula as manobras de uma rota (lista de pontos com nome de rua) e formata distâncias para a voz e para a tela.</summary>
    public static class Navegacao
    {
        public const string SemNome = "rua sem nome";

        /// <summary>Curvas menores que isto (graus) não viram manobra.</summary>
        public const double CurvaMinima = 25.0;

        public const double AnguloVire = 45.0;

        public const double AnguloRetorno = 150.0;

        /// <summary>Preposição do nome da rua: "na Rua X", "no Largo X".</summary>
        public static string Preposicao(string rua)
        {
            if (string.IsNullOrEmpty(rua))
            {
                return "na";
            }

            foreach (string masculino in new[] { "Largo ", "Parque ", "Viaduto ", "Beco ", "Túnel ", "Corredor " })
            {
                if (rua.StartsWith(masculino, StringComparison.OrdinalIgnoreCase))
                {
                    return "no";
                }
            }

            return "na";
        }

        /// <summary>Manobras da rota, em ordem. O primeiro e o último ponto nunca são manobra (o último vira a Chegada).</summary>
        public static List<Instrucao> Calcular(IReadOnlyList<NavPonto> entrada)
        {
            var result = new List<Instrucao>();
            List<NavPonto> p = Limpar(entrada);
            int n = p.Count;

            if (n < 2)
            {
                return result;
            }

            var cum = new double[n];

            for (int i = 1; i < n; i++)
            {
                cum[i] = cum[i - 1] + Dist(p[i - 1], p[i]);
            }

            double total = cum[n - 1];
            var angle = new double[n];

            for (int i = 1; i < n - 1; i++)
            {
                angle[i] = AnguloDireita(p[i - 1], p[i], p[i + 1]);
            }

            // O penúltimo ponto é o eixo da rua em frente ao destino e o último é o próprio destino (a perna entre eles é de lado):
            // essa curva não é manobra, vira a Chegada. Por isso os vértices vão só até n - 3.
            int k = 1;

            while (k <= n - 3)
            {
                if (p[k].Rotatoria && !p[k - 1].Rotatoria)
                {
                    k = Rotatoria(p, cum, total, k, result);

                    continue;
                }

                string entry = p[k - 1].RuaSaida;
                bool changeHere = !MesmaRua(entry, p[k].RuaSaida) && p[k].Cruzamento;

                if (Math.Abs(angle[k]) < 8.0 && !changeHere)
                {
                    k++;

                    continue;
                }

                // Vértices de uma mesma curva (ou de um desvio colado): somam o ângulo e viram uma manobra só.
                int last = k;
                int best = k;
                double sum = angle[k];
                bool cross = p[k].Cruzamento;

                while (last + 1 <= n - 3 && Dist(p[last], p[last + 1]) < 25.0 && Math.Abs(angle[last + 1]) >= 8.0 && !p[last + 1].Rotatoria)
                {
                    last++;
                    sum += angle[last];
                    cross |= p[last].Cruzamento;

                    if (Math.Abs(angle[last]) > Math.Abs(angle[best]))
                    {
                        best = last;
                    }
                }

                string exit = p[last].RuaSaida;
                bool troca = !MesmaRua(entry, exit);
                Manobra manobra = Classificar(sum, cross, troca);

                if (manobra != Manobra.Nenhuma)
                {
                    var inst = new Instrucao
                    {
                        Manobra = manobra,
                        Angulo = sum,
                        Rua = NomeOuPadrao(exit),
                        RuaAnterior = NomeOuPadrao(entry),
                        TrocaDeRua = troca,
                        MetrosAteOFim = total - cum[best],
                        X = p[best].X,
                        Z = p[best].Z,
                    };
                    Direcao(p[k - 1], p[k], out double ex, out double ez);
                    Direcao(p[last], p[last + 1], out double sx, out double sz);
                    inst.EntradaX = ex;
                    inst.EntradaZ = ez;
                    inst.SaidaX = sx;
                    inst.SaidaZ = sz;
                    result.Add(inst);
                }

                k = last + 1;
            }

            result.Add(Chegada(p, total));

            return result;
        }

        private static Manobra Classificar(double angulo, bool cruzamento, bool troca)
        {
            double a = Math.Abs(angulo);
            bool direita = angulo > 0;

            if (a >= AnguloRetorno)
            {
                return Manobra.Retorno;
            }

            if (a >= AnguloVire)
            {
                return cruzamento || a >= 60.0 || troca ? (direita ? Manobra.VireADireita : Manobra.VireAEsquerda) : Manobra.Nenhuma;
            }

            if (a >= CurvaMinima)
            {
                return cruzamento ? (direita ? Manobra.MantenhaADireita : Manobra.MantenhaAEsquerda) : Manobra.Nenhuma;
            }

            return troca && cruzamento ? Manobra.SigaEmFrente : Manobra.Nenhuma;
        }

        /// <summary>Entra numa rotatória em k: a instrução é "pegue a n-ésima saída". Devolve o índice depois da saída.</summary>
        private static int Rotatoria(List<NavPonto> p, double[] cum, double total, int k, List<Instrucao> result)
        {
            int r1 = k;

            while (r1 + 1 < p.Count - 2 && p[r1 + 1].Rotatoria)
            {
                r1++;
            }

            int exitIndex = Math.Min(r1 + 1, p.Count - 1);
            int saida = 1;

            for (int i = k + 1; i <= r1; i++)
            {
                if (p[i].Cruzamento)
                {
                    saida++;
                }
            }

            string exit = exitIndex < p.Count ? p[exitIndex].RuaSaida : string.Empty;
            var inst = new Instrucao
            {
                Manobra = Manobra.RotatoriaSaida,
                Saida = saida,
                Rua = NomeOuPadrao(exit),
                RuaAnterior = NomeOuPadrao(p[k - 1].RuaSaida),
                TrocaDeRua = !MesmaRua(p[k - 1].RuaSaida, exit),
                MetrosAteOFim = total - cum[k],
                X = p[k].X,
                Z = p[k].Z,
            };
            Direcao(p[k - 1], p[k], out double ex, out double ez);
            inst.EntradaX = ex;
            inst.EntradaZ = ez;
            inst.SaidaX = ex;
            inst.SaidaZ = ez;
            result.Add(inst);

            return exitIndex + 1;
        }

        private static Instrucao Chegada(List<NavPonto> p, double total)
        {
            int n = p.Count;
            NavPonto end = p[n - 1];
            NavPonto snap = p[n - 2];
            int lado = 0;
            double dx = 0;
            double dz = 0;

            // A direção da rua é a do trecho anterior ao último passo (do eixo da rua até o lugar).
            for (int i = n - 3; i >= 0; i--)
            {
                if (Dist(p[i], snap) > 0.5)
                {
                    dx = snap.X - p[i].X;
                    dz = snap.Z - p[i].Z;

                    break;
                }
            }

            double len = Math.Sqrt(dx * dx + dz * dz);

            if (len > 0.001)
            {
                double ox = end.X - snap.X;
                double oz = end.Z - snap.Z;
                double lateral = (dx * oz - dz * ox) / len;

                if (Math.Abs(lateral) >= 2.5)
                {
                    lado = lateral < 0 ? 1 : -1;
                }
            }

            Direcao(p[Math.Max(0, n - 2)], p[n - 1], out double ex, out double ez);

            return new Instrucao
            {
                Manobra = Manobra.Chegada,
                Lado = lado,
                Rua = NomeOuPadrao(p[Math.Max(0, n - 2)].RuaSaida),
                RuaAnterior = NomeOuPadrao(p[Math.Max(0, n - 2)].RuaSaida),
                MetrosAteOFim = 0,
                X = end.X,
                Z = end.Z,
                EntradaX = ex,
                EntradaZ = ez,
                SaidaX = ex,
                SaidaZ = ez,
            };
        }

        // ---------- Distâncias em texto ----------

        private static readonly int[] Degraus = { 50, 100, 200, 300, 500, 800 };

        /// <summary>Distância para a voz: 50, 100, 200, 300, 500 ou 800 metros; a partir de 950 m, em quilômetros de meio em meio ("1 quilômetro", "1,5 quilômetro").</summary>
        public static string DistanciaFalada(double metros)
        {
            if (metros >= 950.0)
            {
                double km = Math.Round(metros / 500.0) * 0.5;

                if (km == Math.Floor(km))
                {
                    return km == 1.0 ? "1 quilômetro" : ((int)km).ToString(CultureInfo.InvariantCulture) + " quilômetros";
                }

                return km.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',') + (km < 2.0 ? " quilômetro" : " quilômetros");
            }

            int best = Degraus[0];

            foreach (int step in Degraus)
            {
                if (Math.Abs(step - metros) < Math.Abs(best - metros))
                {
                    best = step;
                }
            }

            return best + " metros";
        }

        /// <summary>Distância para a tela: "30 m", "250 m", "1,2 km".</summary>
        public static string DistanciaCurta(double metros)
        {
            if (metros >= 1000.0)
            {
                return (Math.Round(metros / 100.0) / 10.0).ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',') + " km";
            }

            double step = metros < 100.0 ? 10.0 : 50.0;

            return (Math.Round(metros / step) * step).ToString("0", CultureInfo.InvariantCulture) + " m";
        }

        // ---------- Geometria ----------

        private static List<NavPonto> Limpar(IReadOnlyList<NavPonto> entrada)
        {
            var result = new List<NavPonto>();

            foreach (NavPonto ponto in entrada)
            {
                if (result.Count > 0 && Dist(result[result.Count - 1], ponto) < 0.3)
                {
                    NavPonto anterior = result[result.Count - 1];
                    anterior.RuaSaida = ponto.RuaSaida;
                    anterior.Cruzamento |= ponto.Cruzamento;
                    anterior.Rotatoria |= ponto.Rotatoria;
                    result[result.Count - 1] = anterior;

                    continue;
                }

                result.Add(ponto);
            }

            return result;
        }

        private static double Dist(NavPonto a, NavPonto b)
        {
            double dx = b.X - a.X;
            double dz = b.Z - a.Z;

            return Math.Sqrt(dx * dx + dz * dz);
        }

        private static void Direcao(NavPonto a, NavPonto b, out double x, out double z)
        {
            double dx = b.X - a.X;
            double dz = b.Z - a.Z;
            double len = Math.Sqrt(dx * dx + dz * dz);
            x = len < 1e-9 ? 0 : dx / len;
            z = len < 1e-9 ? 1 : dz / len;
        }

        /// <summary>Ângulo (graus, positivo para a direita) da curva em b, de a-b para b-c, com x à direita e z à frente.</summary>
        public static double AnguloDireita(NavPonto a, NavPonto b, NavPonto c)
        {
            double x1 = b.X - a.X;
            double z1 = b.Z - a.Z;
            double x2 = c.X - b.X;
            double z2 = c.Z - b.Z;
            double cross = x1 * z2 - z1 * x2;
            double dot = x1 * x2 + z1 * z2;

            return -Math.Atan2(cross, dot) * 180.0 / Math.PI;
        }

        private static bool MesmaRua(string a, string b) =>
            string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        private static string NomeOuPadrao(string nome) => string.IsNullOrWhiteSpace(nome) ? SemNome : nome;
    }

    /// <summary>Em que momento da aproximação o aviso foi dado.</summary>
    public enum EstagioAviso
    {
        Nenhum,
        Longe,
        Perto,
        Agora,
    }

    /// <summary>Um aviso de voz.</summary>
    public sealed class Aviso
    {
        public Aviso(Instrucao instrucao, EstagioAviso estagio, string texto)
        {
            Instrucao = instrucao;
            Estagio = estagio;
            Texto = texto;
        }

        public Instrucao Instrucao { get; }

        public Manobra Manobra => Instrucao.Manobra;

        public EstagioAviso Estagio { get; }

        public string Texto { get; }
    }

    /// <summary>
    /// Decide quando falar cada manobra, em função da distância e da velocidade (anuncia mais cedo quando se anda rápido):
    /// "Em 600 metros, vire à direita" (Longe: max(600 m, 12 s)), "Em 200 metros, ..." (Perto: max(200 m, 8 s)) e "Vire à direita" (Agora: max(40 m, 3 s)).
    /// Cada estágio sai uma vez por instrução, em ordem; estágios já ultrapassados são pulados em silêncio.
    /// </summary>
    public sealed class AnunciadorNavegacao
    {
        public double LongeMetros = 600;
        public double LongeSegundos = 12;
        public double PertoMetros = 200;
        public double PertoSegundos = 8;
        public double AgoraMetros = 40;
        public double AgoraSegundos = 3;

        private readonly Dictionary<Instrucao, EstagioAviso> _feito = new Dictionary<Instrucao, EstagioAviso>();

        public double Longe(double velocidadeMs) => Math.Max(LongeMetros, LongeSegundos * Math.Max(0, velocidadeMs));

        public double Perto(double velocidadeMs) => Math.Max(PertoMetros, PertoSegundos * Math.Max(0, velocidadeMs));

        public double Agora(double velocidadeMs) => Math.Max(AgoraMetros, AgoraSegundos * Math.Max(0, velocidadeMs));

        public void Limpar()
        {
            _feito.Clear();
        }

        /// <summary>O aviso a dar agora para a instrução (ou null). distanciaMetros é a distância até a manobra pela rota.</summary>
        public Aviso Avaliar(Instrucao instrucao, double distanciaMetros, double velocidadeMs)
        {
            if (instrucao == null || distanciaMetros < 0)
            {
                return null;
            }

            _feito.TryGetValue(instrucao, out EstagioAviso feito);
            EstagioAviso alvo;

            if (distanciaMetros <= Agora(velocidadeMs))
            {
                alvo = EstagioAviso.Agora;
            }
            else if (distanciaMetros <= Perto(velocidadeMs))
            {
                alvo = EstagioAviso.Perto;
            }
            else if (distanciaMetros <= Longe(velocidadeMs))
            {
                alvo = EstagioAviso.Longe;
            }
            else
            {
                return null;
            }

            if (alvo <= feito)
            {
                return null;
            }

            _feito[instrucao] = alvo;

            // Manobra sem importância (seguir em frente) só é dita de longe.
            if (instrucao.Manobra == Manobra.SigaEmFrente && alvo != EstagioAviso.Longe)
            {
                return null;
            }

            return new Aviso(instrucao, alvo, Texto(instrucao, alvo, distanciaMetros));
        }

        private static string Texto(Instrucao inst, EstagioAviso estagio, double distancia)
        {
            if (estagio == EstagioAviso.Agora)
            {
                return inst.Texto + ".";
            }

            string em = "Em " + Navegacao.DistanciaFalada(distancia) + ", ";

            if (inst.Manobra == Manobra.Chegada)
            {
                return em + (inst.Lado > 0 ? "seu destino estará à direita." : inst.Lado < 0 ? "seu destino estará à esquerda." : "você chega ao destino.");
            }

            if (inst.Manobra == Manobra.SigaEmFrente)
            {
                return "Continue " + (inst.TrocaDeRua ? Navegacao.Preposicao(inst.Rua).Replace("na", "pela").Replace("no", "pelo") + " " + inst.Rua : "em frente") + ".";
            }

            string texto = inst.Texto;

            return em + char.ToLowerInvariant(texto[0]) + texto.Substring(1) + ".";
        }
    }
}
