using UnityEngine;

namespace Entregador.Game
{
    /// <summary>
    /// Estado das luzes de aviso do painel da moto (injeção, óleo, temperatura, bateria, reserva, neutro, farol alto e setas) e do tanque.
    /// A simulação é um esboço: o combustível cai devagar com a distância e o acelerador, e a luz de reserva acende abaixo de 15%.
    /// Sem consequência de jogo ainda. Os testes e a depuração acendem as luzes pelos campos ou por AcenderTudo.
    /// </summary>
    public sealed class PainelMotoEstado
    {
        /// <summary>Fração do tanque abaixo da qual a luz de reserva acende.</summary>
        public const float LimiteReserva = 0.15f;

        /// <summary>Luz de injeção (check engine).</summary>
        public bool Injecao;

        /// <summary>Pressão de óleo baixa.</summary>
        public bool Oleo;

        public bool Temperatura;

        public bool Bateria;

        /// <summary>Reserva de combustível (também liga sozinha quando o tanque baixa).</summary>
        public bool Reserva;

        /// <summary>Neutro (ponto morto).</summary>
        public bool Neutro;

        public bool FarolAlto;

        public bool SetaEsquerda;

        public bool SetaDireita;

        /// <summary>Tanque de 0 a 1.</summary>
        public float Combustivel = 1f;

        /// <summary>Com true a simulação não mexe nas luzes de neutro e reserva (demonstração e capturas de tela).</summary>
        public bool Demo;

        /// <summary>Litros do tanque e consumo de referência, só para o ritmo da queda do combustível.</summary>
        public float AutonomiaKm = 220f;

        public void Abastecer()
        {
            Combustivel = 1f;
            Reserva = false;
        }

        /// <summary>Acende todos os avisos (as setas também), para testar o visual.</summary>
        public void AcenderTudo()
        {
            Demo = true;
            Injecao = Oleo = Temperatura = Bateria = Reserva = Neutro = FarolAlto = SetaEsquerda = SetaDireita = true;
            Combustivel = 0.08f;
        }

        public void Limpar()
        {
            Demo = false;
            Injecao = Oleo = Temperatura = Bateria = Reserva = Neutro = FarolAlto = SetaEsquerda = SetaDireita = false;
            Combustivel = 1f;
        }

        /// <summary>Um passo da simulação: o combustível cai com a distância andada (mais com o acelerador a fundo).</summary>
        public void Simular(float dt, float speedKmh, float throttle, int gear)
        {
            float km = speedKmh / 3600f * dt;
            float drain = km / Mathf.Max(1f, AutonomiaKm) * (1f + Mathf.Clamp01(throttle) * 0.6f);
            Combustivel = Mathf.Clamp01(Combustivel - drain);

            if (!Demo)
            {
                Neutro = gear <= 0;
                Reserva = Combustivel <= LimiteReserva;
            }
        }
    }
}
