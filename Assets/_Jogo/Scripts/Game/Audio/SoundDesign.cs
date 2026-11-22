using System;

namespace Entregador.Game
{
    /// <summary>Um jingle: notas (MIDI) com instante de entrada, de timbre de sino.</summary>
    public sealed class Jingle
    {
        public float[] Midi;
        public float[] Tempo;
        public float Decay = 0.5f;
        public float Brightness = 1f;
        public float Peak = 0.8f;
        public float LastNoteDecay = 0f;
    }

    /// <summary>Os jingles do app: um por app fictício, mais variantes de pedido aceito e perdido. Originais (não copiam nenhum app real).</summary>
    public static class JingleCatalog
    {
        /// <summary>Sininho de pedido novo: 4 notas de marimba/sino, alegre, ~1,2 s.</summary>
        public static Jingle Oferta(string app)
        {
            if (string.Equals(app, "Rangô", StringComparison.OrdinalIgnoreCase))
            {
                // Sol-Si-Ré subindo e um Mi agudo comprido: "ti-ti-ti-dííng".
                return new Jingle
                {
                    Midi = new float[] { 79f, 83f, 86f, 88f },
                    Tempo = new float[] { 0f, 0.13f, 0.26f, 0.42f },
                    Decay = 0.45f,
                    LastNoteDecay = 0.9f,
                    Brightness = 1.1f,
                    Peak = 0.85f,
                };
            }

            // Outros apps: 4 notas da pentatônica escolhidas pelo nome.
            int h = Hash(app);
            float[] penta = { 0f, 2f, 4f, 7f, 9f, 12f };
            float baseNote = 74f + (h & 3);
            var midi = new float[4];

            for (int i = 0; i < 4; i++)
            {
                midi[i] = baseNote + penta[((h >> (2 + i * 3)) & 7) % penta.Length];
            }

            midi[3] = baseNote + 12f + penta[(h >> 3) % 4];

            return new Jingle
            {
                Midi = midi,
                Tempo = new float[] { 0f, 0.12f + (h & 1) * 0.02f, 0.25f, 0.42f },
                Decay = 0.42f,
                LastNoteDecay = 0.85f,
                Brightness = 1f,
                Peak = 0.85f,
            };
        }

        /// <summary>Pedido aceito: duas notas curtas e suaves, subindo.</summary>
        public static Jingle Aceito(string app)
        {
            Jingle oferta = Oferta(app);

            return new Jingle
            {
                Midi = new float[] { oferta.Midi[1] + 0f, oferta.Midi[1] + 7f },
                Tempo = new float[] { 0f, 0.09f },
                Decay = 0.22f,
                LastNoteDecay = 0.45f,
                Brightness = 0.7f,
                Peak = 0.5f,
            };
        }

        /// <summary>Pedido perdido/cancelado: três notas descendo, mais escuras.</summary>
        public static Jingle Perdido(string app)
        {
            return new Jingle
            {
                Midi = new float[] { 76f, 72f, 67f },
                Tempo = new float[] { 0f, 0.22f, 0.46f },
                Decay = 0.5f,
                LastNoteDecay = 0.9f,
                Brightness = 0.45f,
                Peak = 0.6f,
            };
        }

        private static int Hash(string s)
        {
            int h = 17;

            if (s != null)
            {
                foreach (char c in s)
                {
                    h = h * 31 + c;
                }
            }

            return h & 0x7fffffff;
        }
    }

    /// <summary>Geradores de amostras (mono, float) dos sons do jogo, sem Unity: sinos, buzinas, ambientes, efeitos de interface.</summary>
    public static class SoundDesign
    {
        public const int Rate = 44100;
        private const float TwoPi = 6.2831853f;

        // ---------- Sinos ----------

        private static readonly float[] BellRatios = { 1f, 2.01f, 2.76f, 5.40f, 8.93f };
        private static readonly float[] BellAmps = { 1f, 0.55f, 0.42f, 0.2f, 0.1f };

        /// <summary>Soma uma nota de sino (parciais inarmônicos, ataque rápido, os agudos morrem antes) no buffer.</summary>
        public static void AddBell(float[] buf, float startSec, float freq, float decay, float gain, float brightness, int rate = Rate)
        {
            int start = (int)(startSec * rate);

            for (int k = 0; k < BellRatios.Length; k++)
            {
                float f = freq * BellRatios[k];

                if (f > rate * 0.45f)
                {
                    continue;
                }

                float tau = decay / (1f + 0.8f * k);
                float amp = BellAmps[k] * gain * (k == 0 ? 1f : (float)Math.Min(1.4, brightness));
                int n = Math.Min(buf.Length - start, (int)(tau * 7f * rate));

                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)rate;
                    float env = MathF.Exp(-t / tau) * Math.Min(1f, t / 0.0025f);
                    buf[start + i] += amp * env * MathF.Sin(TwoPi * f * t);
                }
            }
        }

        public static float[] RenderJingle(Jingle j, int rate = Rate)
        {
            float last = j.Tempo[j.Tempo.Length - 1];
            float tail = Math.Max(j.LastNoteDecay, j.Decay) * 3.2f;
            var buf = new float[(int)((last + tail) * rate)];

            for (int i = 0; i < j.Midi.Length; i++)
            {
                bool isLast = i == j.Midi.Length - 1;
                float decay = isLast && j.LastNoteDecay > 0f ? j.LastNoteDecay : j.Decay;
                AddBell(buf, j.Tempo[i], AudioMath.MidiToHz(j.Midi[i]), decay, isLast ? 1f : 0.8f, j.Brightness, rate);

                // Um brilho de oitava na última nota de sinos alegres.
                if (isLast && j.Brightness > 0.9f)
                {
                    AddBell(buf, j.Tempo[i], AudioMath.MidiToHz(j.Midi[i] + 12f), decay * 0.4f, 0.22f, 1f, rate);
                }
            }

            Normalize(buf, j.Peak);
            FadeTail(buf, 0.05f, rate);

            return buf;
        }

        // ---------- Interface ----------

        public static float[] Click()
        {
            var rng = new AudioRng(7);
            var buf = new float[(int)(0.035f * Rate)];
            float lp = 0f;

            for (int i = 0; i < buf.Length; i++)
            {
                float t = i / (float)Rate;
                float n = rng.Signed();
                lp += (n - lp) * 0.4f;
                buf[i] = 0.5f * MathF.Sin(TwoPi * 1900f * t) * MathF.Exp(-t / 0.004f) + 0.35f * (n - lp) * MathF.Exp(-t / 0.0015f);
            }

            return buf;
        }

        /// <summary>Notificação (toast): um tim curto.</summary>
        public static float[] Ping()
        {
            var buf = new float[(int)(0.7f * Rate)];
            AddBell(buf, 0f, 1568f, 0.25f, 0.6f, 0.9f);
            Normalize(buf, 0.5f);

            return buf;
        }

        /// <summary>Pagamento: "plim" de caixa registradora (duas notas agudas e um clique metálico).</summary>
        public static float[] Plim()
        {
            var rng = new AudioRng(11);
            var buf = new float[(int)(1.0f * Rate)];

            for (int i = 0; i < (int)(0.02f * Rate); i++)
            {
                buf[i] += rng.Signed() * 0.35f * MathF.Exp(-i / (0.004f * Rate));
            }

            AddBell(buf, 0.005f, 2093f, 0.2f, 0.7f, 1f);
            AddBell(buf, 0.085f, 3136f, 0.45f, 0.8f, 1f);
            Normalize(buf, 0.6f);
            FadeTail(buf, 0.05f);

            return buf;
        }

        /// <summary>Flash do radar: dois cliques de obturador.</summary>
        public static float[] RadarClick()
        {
            var rng = new AudioRng(23);
            var buf = new float[(int)(0.3f * Rate)];
            float lp = 0f;

            for (int i = 0; i < buf.Length; i++)
            {
                float t = i / (float)Rate;
                float n = rng.Signed();
                lp += (n - lp) * 0.3f;
                float hp = n - lp;
                float a = MathF.Exp(-t / 0.003f);
                float tb = t - 0.075f;
                float b = tb > 0f ? MathF.Exp(-tb / 0.006f) : 0f;
                buf[i] = hp * (0.8f * a + 0.55f * b) + (tb > 0f ? 0.25f * MathF.Sin(TwoPi * 380f * tb) * MathF.Exp(-tb / 0.01f) : 0f);
            }

            Normalize(buf, 0.6f);

            return buf;
        }

        /// <summary>"Uiu": curto toque de sirene na abordagem.</summary>
        public static float[] Whoop()
        {
            var buf = new float[(int)(0.75f * Rate)];
            float phase = 0f;

            for (int i = 0; i < buf.Length; i++)
            {
                float t = i / (float)Rate;
                float f = t < 0.32f ? 650f * MathF.Pow(1700f / 650f, t / 0.32f) : 1700f * MathF.Pow(650f / 1700f, Math.Min(1f, (t - 0.32f) / 0.35f));
                phase += f / Rate;
                float p = phase * TwoPi;
                float env = Math.Min(1f, t / 0.02f) * (t > 0.62f ? Math.Max(0f, 1f - (t - 0.62f) / 0.13f) : 1f);
                buf[i] = AudioMath.SoftClip((MathF.Sin(p) + 0.4f * MathF.Sin(2f * p) + 0.25f * MathF.Sin(3f * p)) * 0.9f) * env * 0.6f;
            }

            return buf;
        }

        // ---------- Trânsito ----------

        /// <summary>Buzina brasileira, "fom-fom": duas buzinadas curtas de dois tons (~420 e ~530 Hz).</summary>
        public static float[] Horn(float baseHz = 420f)
        {
            var buf = new float[(int)(0.75f * Rate)];
            AddHonk(buf, 0f, 0.2f, baseHz);
            AddHonk(buf, 0.29f, 0.26f, baseHz);
            Normalize(buf, 0.7f);

            return buf;
        }

        private static void AddHonk(float[] buf, float start, float dur, float baseHz)
        {
            int s = (int)(start * Rate);
            int n = (int)(dur * Rate);

            for (int i = 0; i < n && s + i < buf.Length; i++)
            {
                float t = i / (float)Rate;
                float env = Math.Min(1f, t / 0.012f) * Math.Min(1f, (dur - t) / 0.03f);
                float v = 0f;

                for (int k = 1; k <= 7; k++)
                {
                    float w = 1f / MathF.Pow(k, 0.95f);
                    v += w * MathF.Sin(TwoPi * baseHz * k * t) + 0.8f * w * MathF.Sin(TwoPi * baseHz * 1.26f * k * t);
                }

                buf[s + i] += AudioMath.SoftClip(v * 0.5f) * env;
            }
        }

        /// <summary>Zumbido grave de motor de carro, em laço (1 s; 38 Hz de base, ciclos inteiros).</summary>
        public static float[] CarHum()
        {
            float[] noise = NoiseLoop(1f, 240f, 30f, 5);
            var buf = new float[Rate];
            float[] amps = { 1f, 0.85f, 0.7f, 0.5f, 0.35f, 0.25f, 0.15f, 0.1f };

            for (int i = 0; i < buf.Length; i++)
            {
                float t = i / (float)Rate;
                float v = 0f;

                for (int k = 0; k < amps.Length; k++)
                {
                    v += amps[k] * MathF.Sin(TwoPi * 38f * (k + 1) * t + k);
                }

                float rough = 0.8f + 0.2f * MathF.Sin(TwoPi * 19f * t);
                buf[i] = (v * 0.16f * rough + noise[i] * 0.9f) * 0.5f;
            }

            Normalize(buf, 0.6f);

            return buf;
        }

        /// <summary>Rangido de pneu/freio (1 s em laço, ciclos inteiros).</summary>
        public static float[] Squeal()
        {
            var buf = new float[Rate];
            var rng = new AudioRng(41);
            float phase = 0f;
            float jit = 0f;

            for (int i = 0; i < buf.Length; i++)
            {
                float t = i / (float)Rate;
                float f = 2700f + 140f * MathF.Sin(TwoPi * 3f * t) + 90f * MathF.Sin(TwoPi * 7f * t);
                phase += f / Rate;
                jit += (rng.Signed() - jit) * 0.01f;
                float am = 0.7f + 0.3f * MathF.Sin(TwoPi * 11f * t);
                buf[i] = (MathF.Sin(TwoPi * phase) + 0.45f * MathF.Sin(TwoPi * phase * 1.5f)) * am * 0.5f + jit * 2f;
            }

            Normalize(buf, 0.6f);

            return buf;
        }

        // ---------- Ambientes ----------

        /// <summary>Barulho distante da cidade: rumor de trânsito, 8 s em laço.</summary>
        public static float[] CityBed()
        {
            float[] low = NoiseLoop(8f, 330f, 35f, 31);
            float[] mid = NoiseLoop(8f, 1700f, 500f, 32);
            var buf = new float[low.Length];

            for (int i = 0; i < buf.Length; i++)
            {
                float t = i / (float)Rate;
                float swell = 0.82f + 0.18f * MathF.Sin(TwoPi * t / 8f * 1f) + 0.06f * MathF.Sin(TwoPi * t / 8f * 3f);
                buf[i] = (low[i] + 0.32f * mid[i]) * swell;
            }

            Normalize(buf, 0.5f);

            return buf;
        }

        /// <summary>Chuva: chiado com pingos, 4 s em laço.</summary>
        public static float[] Rain()
        {
            float[] hiss = NoiseLoop(4f, 8000f, 1100f, 51);
            var rng = new AudioRng(52);
            var buf = new float[hiss.Length];

            for (int i = 0; i < buf.Length; i++)
            {
                buf[i] = hiss[i];
            }

            int drops = 180;

            for (int d = 0; d < drops; d++)
            {
                int pos = (int)(rng.Value() * (buf.Length - 400));
                float amp = 0.1f + 0.25f * rng.Value();
                float f = 1800f + 2500f * rng.Value();

                for (int i = 0; i < 300; i++)
                {
                    float t = i / (float)Rate;
                    buf[pos + i] += amp * MathF.Sin(TwoPi * f * t) * MathF.Exp(-t / 0.002f);
                }
            }

            Normalize(buf, 0.5f);

            return buf;
        }

        /// <summary>Vento: ruído de banda larga grave, 4 s em laço.</summary>
        public static float[] Wind()
        {
            float[] noise = NoiseLoop(4f, 1300f, 90f, 61);
            var buf = new float[noise.Length];

            for (int i = 0; i < buf.Length; i++)
            {
                float t = i / (float)Rate;
                buf[i] = noise[i] * (0.8f + 0.2f * MathF.Sin(TwoPi * t / 4f));
            }

            Normalize(buf, 0.6f);

            return buf;
        }

        /// <summary>Um gorjeio de passarinho (variante 0, 1 ou 2).</summary>
        public static float[] Bird(int variant)
        {
            var buf = new float[(int)(1.1f * Rate)];
            var rng = new AudioRng((uint)(70 + variant));
            int notes = 3 + variant;
            float t0 = 0.02f;

            for (int n = 0; n < notes; n++)
            {
                float dur = 0.07f + 0.06f * rng.Value();
                float f0 = 3200f + 1600f * rng.Value();
                float f1 = f0 + (variant == 1 ? -900f : 900f) * (rng.Value() + 0.3f);
                float vib = 25f + 30f * rng.Value();
                int s = (int)(t0 * Rate);
                int len = (int)(dur * Rate);
                float phase = 0f;

                for (int i = 0; i < len && s + i < buf.Length; i++)
                {
                    float u = i / (float)len;
                    float f = f0 + (f1 - f0) * u + 120f * MathF.Sin(TwoPi * vib * u * dur);
                    phase += f / Rate;
                    float env = MathF.Sin(MathF.PI * u);
                    buf[s + i] += MathF.Sin(TwoPi * phase) * env * env * 0.5f;
                }

                t0 += dur + 0.05f + 0.05f * rng.Value();
            }

            Normalize(buf, 0.35f);

            return buf;
        }

        /// <summary>Estática de sintonia do rádio (0,3 s).</summary>
        public static float[] Static()
        {
            var rng = new AudioRng(81);
            var buf = new float[(int)(0.32f * Rate)];
            float lp = 0f;

            for (int i = 0; i < buf.Length; i++)
            {
                float t = i / (float)Rate;
                float n = rng.Signed();
                lp += (n - lp) * 0.5f;
                float env = Math.Min(1f, t / 0.01f) * Math.Min(1f, (0.32f - t) / 0.08f);
                float whistle = 0.15f * MathF.Sin(TwoPi * (900f + 3000f * t) * t);
                buf[i] = ((n - lp) * 0.8f + whistle) * env;
            }

            Normalize(buf, 0.5f);

            return buf;
        }

        // ---------- Utilidades ----------

        /// <summary>
        /// Ruído filtrado (passa-baixa de dois polos em lowHz, passa-alta de um polo em highHz) que fecha o laço sem estalo
        /// (emenda com fusão de potência constante). Normalizado para RMS ~0,25.
        /// </summary>
        public static float[] NoiseLoop(float seconds, float lowHz, float highHz, uint seed)
        {
            int n = (int)(seconds * Rate);
            int fade = Math.Min(n / 2, (int)(0.25f * Rate));
            var raw = new float[n + fade];
            var rng = new AudioRng(seed);
            float a = 1f - MathF.Exp(-TwoPi * lowHz / Rate);
            float h = 1f - MathF.Exp(-TwoPi * Math.Max(1f, highHz) / Rate);
            float l1 = 0f;
            float l2 = 0f;
            float hp = 0f;

            for (int i = 0; i < raw.Length; i++)
            {
                l1 += (rng.Signed() - l1) * a;
                l2 += (l1 - l2) * a;
                hp += (l2 - hp) * h;
                raw[i] = l2 - hp;
            }

            var buf = new float[n];

            for (int i = 0; i < n; i++)
            {
                buf[i] = raw[i];
            }

            for (int i = 0; i < fade; i++)
            {
                float w = i / (float)fade * (MathF.PI * 0.5f);
                buf[i] = raw[i] * MathF.Sin(w) + raw[n + i] * MathF.Cos(w);
            }

            double sum = 0;

            for (int i = 0; i < n; i++)
            {
                sum += buf[i] * buf[i];
            }

            float rms = (float)Math.Sqrt(sum / n);
            float g = rms > 1e-9f ? 0.25f / rms : 1f;

            for (int i = 0; i < n; i++)
            {
                buf[i] *= g;
            }

            return buf;
        }

        public static void Normalize(float[] buf, float peak)
        {
            float max = 0f;

            for (int i = 0; i < buf.Length; i++)
            {
                max = Math.Max(max, Math.Abs(buf[i]));
            }

            if (max < 1e-9f)
            {
                return;
            }

            float g = peak / max;

            for (int i = 0; i < buf.Length; i++)
            {
                buf[i] *= g;
            }
        }

        public static void FadeTail(float[] buf, float seconds, int rate = Rate)
        {
            int n = Math.Min(buf.Length, (int)(seconds * rate));

            for (int i = 0; i < n; i++)
            {
                buf[buf.Length - 1 - i] *= i / (float)n;
            }
        }
    }
}
