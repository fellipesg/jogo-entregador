using Entregador.Core;
using UnityEngine;

namespace Entregador.Game
{
    /// <summary>O semáforo à frente de alguém: estado, distância até a linha de retenção e quanto falta para mudar.</summary>
    public struct SemaforoLeitura
    {
        /// <summary>Há um semáforo à frente dentro do alcance?</summary>
        public bool Existe;

        public SemaforoEstado Estado;

        /// <summary>Distância (m) até a linha de retenção, ao longo da via.</summary>
        public float Distancia;

        /// <summary>Segundos até o sinal mudar de cor.</summary>
        public float SegundosParaMudar;

        /// <summary>O cruzamento tem fotossensor (câmera de avanço de sinal).</summary>
        public bool Fotossensor;

        public Vector3 Linha;

        public SemaforoControlador Controlador;
    }

    /// <summary>
    /// API pública dos semáforos: a interface e o GPS perguntam "tem sinal vermelho à frente?" e os carros do trânsito decidem
    /// se param na linha. Só olha para os braços cujo sentido de chegada combina com a direção de quem pergunta.
    /// </summary>
    public static class Semaforo
    {
        /// <summary>Meio comprimento de um carro mais a folga que ele deixa antes da linha (m).</summary>
        public const float RecuoDoCarro = 2.6f;

        /// <summary>Freada com que o carro do trânsito chega parado à linha (m/s2).</summary>
        public const float FreadaDoCarro = 5f;

        /// <summary>
        /// O semáforo mais próximo à frente de quem está em "posicao" indo para "direcao" (até "alcance" metros). Existe = false se
        /// não há nenhum. Usa a pista do braço (com uma folga de 4 m) e uma direção até uns 45 graus da do braço.
        /// </summary>
        public static SemaforoLeitura EstadoPara(Vector3 posicao, Vector3 direcao, float alcance = 80f)
        {
            if (Encontrar(posicao, direcao, alcance, 0.7f, 4f, out SemaforoAproximacao a, out float distancia))
            {
                return new SemaforoLeitura
                {
                    Existe = true,
                    Estado = a.Dono.Estado(a),
                    Distancia = distancia,
                    SegundosParaMudar = a.Dono.SegundosParaMudar(a),
                    Fotossensor = a.Dono.Fotossensor,
                    Linha = a.Linha,
                    Controlador = a.Dono,
                };
            }

            return default;
        }

        /// <summary>Sinal vermelho (ou amarelo prestes a fechar) à frente dentro de "alcance" metros?</summary>
        public static bool VermelhoAFrente(Vector3 posicao, Vector3 direcao, float alcance = 60f)
        {
            SemaforoLeitura leitura = EstadoPara(posicao, direcao, alcance);

            return leitura.Existe && leitura.Estado != SemaforoEstado.Verde;
        }

        private static bool Encontrar(Vector3 posicao, Vector3 direcao, float alcance, float alinhamento, float folgaLateral, out SemaforoAproximacao melhor, out float distancia)
        {
            melhor = null;
            distancia = float.MaxValue;
            direcao.y = 0f;

            if (direcao.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            direcao.Normalize();

            foreach (SemaforoControlador controlador in SemaforoControlador.Lista)
            {
                foreach (SemaforoAproximacao a in controlador.Aproximacoes)
                {
                    float d = a.DistanciaAte(posicao);

                    if (d < -0.5f || d > alcance || d >= distancia || Vector3.Dot(direcao, a.Direcao) < alinhamento || !a.NaPista(posicao, folgaLateral))
                    {
                        continue;
                    }

                    melhor = a;
                    distancia = d;
                }
            }

            return melhor != null;
        }

        /// <summary>
        /// Decide se um carro do trânsito (centro em "posicao", indo para "frente" a "velocidade" m/s) tem de parar na linha de um
        /// semáforo. Devolve true com a velocidade máxima que o deixa parar na linha. No amarelo, quem não consegue parar com segurança
        /// passa e fica "comprometido" com o braço (não freia de repente se o vermelho vier antes de cruzar).
        /// </summary>
        public static bool ParaCarro(Vector3 posicao, Vector3 frente, float velocidade, ref SemaforoAproximacao compromisso, out float velocidadeMaxima)
        {
            velocidadeMaxima = float.MaxValue;

            if (!Encontrar(posicao, frente, 40f, 0.85f, 1.2f, out SemaforoAproximacao a, out float distancia))
            {
                compromisso = null;

                return false;
            }

            float alvo = distancia - RecuoDoCarro;

            // Já cruzou a linha (o nariz passou dela): o resto é com o cruzamento.
            if (alvo < -0.8f)
            {
                return false;
            }

            if (compromisso == a)
            {
                return false;
            }

            SemaforoEstado estado = a.Dono.Estado(a);

            if (!SemaforoRegras.DeveParar(estado, velocidade, Mathf.Max(0f, alvo)))
            {
                if (estado == SemaforoEstado.Amarelo)
                {
                    compromisso = a;
                }

                return false;
            }

            velocidadeMaxima = (float)SemaforoRegras.VelocidadeParaParar(Mathf.Max(0f, alvo), FreadaDoCarro);

            return true;
        }
    }
}
