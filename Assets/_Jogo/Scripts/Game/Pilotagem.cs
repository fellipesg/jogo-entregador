using UnityEngine;

namespace Entregador.Game
{
    /// <summary>
    /// Matemática pura da pilotagem (sem estado, testável): a direção comanda a INCLINAÇÃO da moto, e a guinada sai da inclinação pela relação
    /// da curva coordenada (guinada = g·tan(inclinação)/v). A inclinação demora mais para subir quanto maior a velocidade (efeito giroscópico),
    /// a aderência limita a aceleração lateral (círculo de atrito: freiar tira inclinação disponível) e em baixa velocidade o guidão gira direto.
    /// </summary>
    public static class Pilotagem
    {
        public const float Gravidade = 9.81f;

        /// <summary>Velocidade (m/s) em que o tempo de inclinação é o "rápido" (20 km/h).</summary>
        public const float VelocidadeTempoLento = 20f / 3.6f;

        /// <summary>Velocidade (m/s) em que o tempo de inclinação é o "lento" (110 km/h).</summary>
        public const float VelocidadeTempoRapido = 110f / 3.6f;

        /// <summary>Abaixo disto (m/s, ~11 km/h) só o guidão manda; acima de <see cref="FimDaMistura"/> (~20 km/h) só a inclinação.</summary>
        public const float InicioDaMistura = 3f;

        public const float FimDaMistura = 5.5f;

        /// <summary>Piso de velocidade (m/s) na relação g·tan/v, para a guinada não explodir quase parado.</summary>
        public const float VelocidadeMinimaCurva = 3f;

        /// <summary>Tempo (s) da inclinação: ~0,15 s a 20 km/h até ~0,45 s a 110 km/h, crescendo de forma suave com a velocidade.</summary>
        public static float TempoInclinacao(float velocidade, float lento = 0.15f, float rapido = 0.45f)
        {
            float x = Mathf.InverseLerp(VelocidadeTempoLento, VelocidadeTempoRapido, Mathf.Abs(velocidade));

            return Mathf.Lerp(lento, rapido, x);
        }

        /// <summary>Aceleração lateral (m/s²) de uma curva em regime com a inclinação dada (graus): g·tan(inclinação).</summary>
        public static float AceleracaoLateral(float inclinacaoGraus)
        {
            return Gravidade * Mathf.Tan(Mathf.Abs(inclinacaoGraus) * Mathf.Deg2Rad);
        }

        /// <summary>Guinada (graus/s) da curva coordenada: g·tan(inclinação)/v. Mesmo sinal da inclinação.</summary>
        public static float TaxaGuinada(float inclinacaoGraus, float velocidade)
        {
            float v = Mathf.Max(Mathf.Abs(velocidade), VelocidadeMinimaCurva);

            return Gravidade * Mathf.Tan(inclinacaoGraus * Mathf.Deg2Rad) / v * Mathf.Rad2Deg;
        }

        /// <summary>Raio da curva (m) para uma inclinação e velocidade (infinito reto).</summary>
        public static float RaioDaCurva(float inclinacaoGraus, float velocidade)
        {
            float a = AceleracaoLateral(inclinacaoGraus);

            return a < 0.0001f ? float.PositiveInfinity : velocidade * velocidade / a;
        }

        /// <summary>Peso (0 a 1) da direção por inclinação; o resto é o guidão direto, para manobra em baixa velocidade.</summary>
        public static float PesoInclinacao(float velocidade)
        {
            float x = Mathf.InverseLerp(InicioDaMistura, FimDaMistura, Mathf.Abs(velocidade));

            return x * x * (3f - 2f * x);
        }

        /// <summary>
        /// Aceleração lateral (m/s²) que o pneu ainda tem: círculo de atrito. aceleracaoMax é o teto da moto no seco, aderencia multiplica
        /// (1 seco, ~0,6 molhado) e desaceleracao é o freio que já usa parte do atrito.
        /// </summary>
        public static float AceleracaoLateralDisponivel(float aceleracaoMax, float aderencia, float desaceleracao)
        {
            float total = aceleracaoMax * aderencia;
            float freio = Mathf.Min(Mathf.Abs(desaceleracao), total * 0.92f);

            return Mathf.Sqrt(Mathf.Max(0f, total * total - freio * freio));
        }

        /// <summary>Inclinação máxima (graus): a da moto, limitada pela aderência (aceleração lateral disponível) e pelo freio.</summary>
        public static float InclinacaoMaxima(float inclinacaoMaxGraus, float aceleracaoMax, float aderencia, float desaceleracao)
        {
            float disponivel = AceleracaoLateralDisponivel(aceleracaoMax, aderencia, desaceleracao);
            float limite = Mathf.Atan(disponivel / Gravidade) * Mathf.Rad2Deg;

            return Mathf.Min(inclinacaoMaxGraus, limite);
        }

        /// <summary>
        /// Modela a entrada de direção (-1 a 1): em baixa velocidade é linear; em alta o expoente cresce, dando precisão perto do centro
        /// (a mesma ponta do dedo faz menos) sem perder o fundo de curso.
        /// </summary>
        public static float FormarEntrada(float entrada, float velocidade, float expoenteRapido = 1.5f)
        {
            float x = Mathf.InverseLerp(8f, 30f, Mathf.Abs(velocidade));
            float expoente = Mathf.Lerp(1f, expoenteRapido, x * x * (3f - 2f * x));
            float a = Mathf.Clamp01(Mathf.Abs(entrada));

            return Mathf.Sign(entrada) * Mathf.Pow(a, expoente);
        }

        /// <summary>
        /// Taxa (unidades/s) com que a direção aplicada anda até a entrada: rampa mais lenta em alta velocidade e mais rápida
        /// ao voltar ao centro (teclado nunca vai de 0 a 1 de uma vez).
        /// </summary>
        public static float TaxaRampaEntrada(float velocidade, bool voltando, float rapida = 8f, float lenta = 3.5f)
        {
            float x = Mathf.InverseLerp(5f, 30f, Mathf.Abs(velocidade));
            float taxa = Mathf.Lerp(rapida, lenta, x);

            return voltando ? taxa * 1.6f : taxa;
        }

        /// <summary>Fração da inclinação máxima que o fundo de curso da direção alcança: 1 até ~55 km/h e 0,85 a 110 km/h (ninguém deita 45° a 110 km/h).</summary>
        public static float FatorInclinacaoRapida(float velocidade)
        {
            float x = Mathf.InverseLerp(15f, 30f, Mathf.Abs(velocidade));

            return Mathf.Lerp(1f, 0.85f, x);
        }

        /// <summary>Zona morta com reescala (sem degrau na saída dela): |x| ≤ zona vale 0 e o resto volta a ir até 1.</summary>
        public static float ZonaMorta(float x, float zona)
        {
            float a = Mathf.Abs(x);

            if (a <= zona)
            {
                return 0f;
            }

            return Mathf.Sign(x) * Mathf.Min(1f, (a - zona) / (1f - zona));
        }

        /// <summary>
        /// Passo de um sistema de segunda ordem com amortecimento crítico (sem oscilar nem passar do alvo). tempo é a constante de tempo (s):
        /// em ~tempo a resposta ao degrau chega a ~63%. Solução exata, estável com qualquer dt.
        /// </summary>
        public static void PassoCritico(ref float valor, ref float taxa, float alvo, float tempo, float dt)
        {
            float omega = 2.15f / Mathf.Max(0.02f, tempo);
            float y = valor - alvo;
            float k = taxa + omega * y;
            float e = Mathf.Exp(-omega * dt);
            valor = alvo + (y + k * dt) * e;
            taxa = (taxa - omega * k * dt) * e;
        }

        /// <summary>Guinada (graus/s) do guidão direto: raio mínimo de esterçamento, limitado pelo giro máximo e pela aderência.</summary>
        public static float TaxaGuinadaGuidao(float direcao, float velocidade, float raioMinimo, float giroMaximo, float aceleracaoLateral)
        {
            float v = Mathf.Abs(velocidade);
            float fade = Mathf.Clamp01(v / 1.5f);
            float porRaio = Mathf.Max(v, 1f) / Mathf.Max(0.5f, raioMinimo) * Mathf.Rad2Deg;
            float porAderencia = aceleracaoLateral * Mathf.Rad2Deg / Mathf.Max(v, 1f);

            return direcao * Mathf.Min(giroMaximo, Mathf.Min(porRaio, porAderencia)) * fade;
        }

        /// <summary>Quanto a moto escapa de frente (0 a 1): sobe quando a inclinação passa da disponível, de forma progressiva.</summary>
        public static float AlvoDerrapagem(float inclinacaoGraus, float inclinacaoDisponivelGraus)
        {
            float excesso = Mathf.Abs(inclinacaoGraus) - inclinacaoDisponivelGraus - 1f;

            return Mathf.Clamp01(excesso / 6f);
        }
    }
}
