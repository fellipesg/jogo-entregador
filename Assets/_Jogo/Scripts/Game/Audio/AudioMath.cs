using System;

namespace Entregador.Game
{
    /// <summary>Gerador pseudoaleatório pequeno (xorshift), determinístico: a mesma semente dá o mesmo som.</summary>
    public sealed class AudioRng
    {
        private uint _state;

        public AudioRng(uint seed)
        {
            _state = seed == 0 ? 2463534242u : seed;
        }

        /// <summary>Valor de 0 a 1.</summary>
        public float Value()
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;

            return (_state & 0xFFFFFF) / 16777216f;
        }

        /// <summary>Valor de -1 a 1.</summary>
        public float Signed() => Value() * 2f - 1f;
    }

    /// <summary>Contas puras do som (sem Unity): frequência de explosão do motor, suavização, notas, Doppler.</summary>
    public static class AudioMath
    {
        public const float SpeedOfSound = 343f;

        /// <summary>
        /// Explosões por segundo de um motor de 1 cilindro, 4 tempos: uma a cada duas voltas do virabrequim (rpm / 120).
        /// </summary>
        public static float FiringHz(float rpm) => Math.Max(0f, rpm) / 120f;

        /// <summary>Coeficiente de um filtro de um polo (0 a 1 por amostra) para a constante de tempo dada.</summary>
        public static float OnePole(float seconds, float sampleRate)
        {
            if (seconds <= 0f)
            {
                return 1f;
            }

            return 1f - MathF.Exp(-1f / (seconds * sampleRate));
        }

        /// <summary>Suavização exponencial independente de quadro: anda de current até target com a constante de tempo tau.</summary>
        public static float Smooth(float current, float target, float dt, float tau)
        {
            if (tau <= 0f)
            {
                return target;
            }

            return current + (target - current) * (1f - MathF.Exp(-dt / tau));
        }

        public static float Clamp01(float x) => x < 0f ? 0f : (x > 1f ? 1f : x);

        public static float Lerp(float a, float b, float t) => a + (b - a) * t;

        /// <summary>Giro normalizado: 0 na marcha lenta, 1 no limite.</summary>
        public static float RpmNormalizado(float rpm, float idle, float limite) =>
            limite <= idle ? 0f : Clamp01((rpm - idle) / (limite - idle));

        /// <summary>Frequência de uma nota MIDI (69 = lá 440 Hz).</summary>
        public static float MidiToHz(float midi) => 440f * MathF.Pow(2f, (midi - 69f) / 12f);

        /// <summary>Distorção suave (aproxima tanh) que segura picos sem estalar.</summary>
        public static float SoftClip(float x)
        {
            if (x > 3f)
            {
                return 1f;
            }

            if (x < -3f)
            {
                return -1f;
            }

            return x * (27f + x * x) / (27f + 9f * x * x);
        }

        /// <summary>Fator de pitch por efeito Doppler: velocidade radial positiva = fonte se aproximando.</summary>
        public static float DopplerPitch(float radialApproachSpeed)
        {
            float v = Math.Max(-60f, Math.Min(60f, radialApproachSpeed));

            return SpeedOfSound / (SpeedOfSound - v);
        }
    }

    /// <summary>Tempo e tons da sirene da polícia: dois tons que se alternam (agudo e grave), com uma curta passagem entre eles.</summary>
    public static class SirenMath
    {
        public const float HiHz = 960f;
        public const float LoHz = 770f;
        public const float ToneSeconds = 0.5f;
        public const float GlideSeconds = 0.06f;

        /// <summary>Período completo (agudo + grave).</summary>
        public static float Period => ToneSeconds * 2f;

        /// <summary>Frequência instantânea (Hz) da sirene no instante t (segundos desde que ligou).</summary>
        public static float FrequencyAt(float t)
        {
            float p = t % Period;

            if (p < 0f)
            {
                p += Period;
            }

            // 0..Tone = agudo; Tone..2Tone = grave; a transição leva GlideSeconds no começo de cada metade.
            bool hiHalf = p < ToneSeconds;
            float into = hiHalf ? p : p - ToneSeconds;
            float from = hiHalf ? LoHz : HiHz;
            float to = hiHalf ? HiHz : LoHz;
            float k = AudioMath.Clamp01(into / GlideSeconds);
            k = k * k * (3f - 2f * k);

            return from + (to - from) * k;
        }
    }
}
