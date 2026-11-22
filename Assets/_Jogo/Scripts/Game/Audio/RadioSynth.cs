using System;

namespace Entregador.Game
{
    /// <summary>Estilo do laço sintetizado que serve de faixa provisória de cada estação.</summary>
    public enum RadioEstilo
    {
        Funk,
        Sertanejo,
        Pagode,
        Rock,
        LoFi,
    }

    /// <summary>
    /// Faixas provisórias do rádio, sintetizadas (bumbo, caixa, chimbal, baixo, acorde) em laço de compassos inteiros, e as vinhetas
    /// das estações. Nada sofisticado: serve para o sistema soar agora; faixas reais entram depois pelo catálogo.
    /// </summary>
    public static class RadioSynth
    {
        public const int Rate = 22050;
        private const float TwoPi = 6.2831853f;

        public static float Bpm(RadioEstilo e)
        {
            switch (e)
            {
                case RadioEstilo.Funk: return 92f;
                case RadioEstilo.Sertanejo: return 100f;
                case RadioEstilo.Pagode: return 96f;
                case RadioEstilo.Rock: return 132f;
                default: return 76f;
            }
        }

        public static int Compassos(RadioEstilo e)
        {
            switch (e)
            {
                case RadioEstilo.Rock: return 12;
                case RadioEstilo.LoFi: return 8;
                case RadioEstilo.Sertanejo: return 8;
                default: return 8;
            }
        }

        /// <summary>Duração (s) do laço do estilo.</summary>
        public static float Duracao(RadioEstilo e) => Compassos(e) * 4f * 60f / Bpm(e);

        public static float[] RenderLoop(RadioEstilo estilo)
        {
            float bpm = Bpm(estilo);
            float beat = 60f / bpm;
            float step = beat / 4f;
            int bars = Compassos(estilo);
            int steps = bars * 16;
            var buf = new float[(int)(Duracao(estilo) * Rate)];
            var rng = new AudioRng(100u + (uint)estilo);

            // Progressão de acordes (raiz MIDI + terça maior/menor), um por compasso (repete a cada 4).
            float[] roots;
            bool minor;

            switch (estilo)
            {
                case RadioEstilo.Funk: roots = new float[] { 45f, 45f, 41f, 43f }; minor = true; break;
                case RadioEstilo.Sertanejo: roots = new float[] { 48f, 55f, 57f, 53f }; minor = false; break;
                case RadioEstilo.Pagode: roots = new float[] { 50f, 55f, 48f, 55f }; minor = false; break;
                case RadioEstilo.Rock: roots = new float[] { 40f, 40f, 43f, 38f }; minor = true; break;
                default: roots = new float[] { 52f, 57f, 50f, 55f }; minor = true; break;
            }

            for (int s = 0; s < steps; s++)
            {
                int bar = s / 16;
                int sb = s % 16;
                float t = s * step;
                float root = roots[bar % 4];

                switch (estilo)
                {
                    case RadioEstilo.Funk:
                        if (sb == 0 || sb == 3 || sb == 6 || sb == 10) { Kick(buf, t, 52f, 0.9f); }
                        if (sb == 4 || sb == 12) { Snare(buf, t, rng, 0.55f, 0.12f); }
                        if (sb % 2 == 0) { Hat(buf, t, rng, 0.22f, 0.025f); }
                        if (sb == 0 || sb == 3 || sb == 6 || sb == 10) { Tone(buf, t, AudioMath.MidiToHz(root - 12f), step * 2.6f, 0.5f, 1, 0.003f, 0.18f); }
                        if (sb == 0 || sb == 8) { Chord(buf, t, root + 12f, minor, beat * 1.6f, 0.13f, 2); }
                        break;
                    case RadioEstilo.Sertanejo:
                        if (sb == 0 || sb == 8) { Kick(buf, t, 60f, 0.65f); }
                        if (sb == 4 || sb == 12) { Snare(buf, t, rng, 0.3f, 0.09f); }
                        if (sb % 4 == 2) { Hat(buf, t, rng, 0.15f, 0.03f); }
                        if (sb == 0) { Tone(buf, t, AudioMath.MidiToHz(root - 12f), beat * 1.8f, 0.42f, 1, 0.004f, 0.3f); }
                        if (sb == 8) { Tone(buf, t, AudioMath.MidiToHz(root - 5f), beat * 1.8f, 0.4f, 1, 0.004f, 0.3f); }
                        if (sb % 2 == 0) { Pluck(buf, t, AudioMath.MidiToHz(root + (new[] { 0f, 4f, 7f, 12f })[(sb / 2) % 4]), 0.35f, 0.16f); }
                        break;
                    case RadioEstilo.Pagode:
                        if (sb == 4 || sb == 12) { Kick(buf, t, 48f, 0.8f, 0.2f); }
                        if (sb == 0 || sb == 8) { Kick(buf, t, 70f, 0.35f, 0.1f); }
                        if (sb % 2 == 0 || sb == 3 || sb == 11) { Hat(buf, t, rng, sb % 4 == 0 ? 0.26f : 0.16f, 0.02f); }
                        if (sb == 6 || sb == 14) { Snare(buf, t, rng, 0.25f, 0.06f); }
                        if (sb == 0 || sb == 6 || sb == 8) { Tone(buf, t, AudioMath.MidiToHz(root - 12f), step * 3f, 0.42f, 1, 0.004f, 0.3f); }
                        Pluck(buf, t, AudioMath.MidiToHz(root + 12f + (new[] { 0f, 4f, 7f, 11f })[(sb / 2) % 4]), 0.18f + 0.1f * (sb % 4 == 0 ? 1f : 0f), 0.11f);
                        break;
                    case RadioEstilo.Rock:
                        if (sb == 0 || sb == 2 || sb == 8 || sb == 10) { Kick(buf, t, 58f, 0.9f); }
                        if (sb == 4 || sb == 12) { Snare(buf, t, rng, 0.75f, 0.14f); }
                        if (sb % 2 == 0) { Hat(buf, t, rng, 0.28f, 0.04f); }
                        if (sb % 2 == 0) { Tone(buf, t, AudioMath.MidiToHz(root), step * 1.8f, 0.3f, 2, 0.002f, 0.18f); }
                        if (sb == 0 || sb == 6 || sb == 8 || sb == 14) { PowerChord(buf, t, root + 12f, step * (sb == 0 || sb == 8 ? 5f : 2.5f)); }
                        break;
                    default:
                        if (sb == 0 || sb == 10) { Kick(buf, t, 50f, 0.6f, 0.22f); }
                        if (sb == 4 || sb == 12) { Snare(buf, t, rng, 0.28f, 0.1f, 0.25f); }
                        if (sb % 2 == 0) { Hat(buf, t, rng, sb % 4 == 2 ? 0.12f : 0.07f, 0.03f); }
                        if (sb == 0) { Tone(buf, t, AudioMath.MidiToHz(root - 12f), beat * 3.5f, 0.38f, 1, 0.01f, 0.6f); }
                        if (sb == 0) { Chord(buf, t, root, minor, beat * 3.6f, 0.17f, 3); }
                        if (sb == 8 && bar % 2 == 1) { Chord(buf, t, root + 7f, minor, beat * 1.8f, 0.1f, 3); }
                        break;
                }
            }

            if (estilo == RadioEstilo.LoFi)
            {
                // Chiado de vinil.
                for (int i = 0; i < buf.Length; i++)
                {
                    float crackle = rng.Value() < 0.0008f ? rng.Signed() * 0.25f : 0f;
                    buf[i] += rng.Signed() * 0.006f + crackle;
                }
            }

            SoundDesign.Normalize(buf, 0.85f);

            return buf;
        }

        /// <summary>Vinheta da estação (~2 s): arpejo de sinos no tom do estilo, com um "chiado" no começo.</summary>
        public static float[] RenderVinheta(RadioEstilo estilo)
        {
            var buf = new float[(int)(2.2f * Rate)];
            float[] notes;
            float gap = 0.14f;

            switch (estilo)
            {
                case RadioEstilo.Funk: notes = new float[] { 69f, 72f, 76f, 69f, 81f }; gap = 0.11f; break;
                case RadioEstilo.Sertanejo: notes = new float[] { 72f, 76f, 79f, 84f }; break;
                case RadioEstilo.Pagode: notes = new float[] { 74f, 79f, 83f, 86f, 83f }; gap = 0.12f; break;
                case RadioEstilo.Rock: notes = new float[] { 64f, 71f, 76f, 83f }; gap = 0.1f; break;
                default: notes = new float[] { 76f, 79f, 83f, 88f }; gap = 0.2f; break;
            }

            for (int i = 0; i < notes.Length; i++)
            {
                SoundDesign.AddBell(buf, 0.05f + i * gap, AudioMath.MidiToHz(notes[i]), 0.4f, 0.8f, 1f, Rate);
            }

            SoundDesign.Normalize(buf, 0.5f);
            SoundDesign.FadeTail(buf, 0.1f, Rate);

            return buf;
        }

        // ---------- Instrumentos ----------

        private static void Kick(float[] buf, float start, float lowHz, float gain, float dur = 0.16f)
        {
            int s = (int)(start * Rate);
            int n = (int)(dur * Rate);
            float phase = 0f;

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float f = lowHz + 110f * MathF.Exp(-t / 0.025f);
                phase += f / Rate;
                Add(buf, s + i, MathF.Sin(TwoPi * phase) * MathF.Exp(-t / (dur * 0.35f)) * gain);
            }
        }

        private static void Snare(float[] buf, float start, AudioRng rng, float gain, float dur, float tone = 0.5f)
        {
            int s = (int)(start * Rate);
            int n = (int)(dur * 2f * Rate);
            float lp = 0f;

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = rng.Signed();
                lp += (noise - lp) * 0.5f;
                float v = (noise - lp * 0.6f) * MathF.Exp(-t / (dur * 0.5f)) + tone * MathF.Sin(TwoPi * 185f * t) * MathF.Exp(-t / 0.05f);
                Add(buf, s + i, v * gain * 0.7f);
            }
        }

        private static void Hat(float[] buf, float start, AudioRng rng, float gain, float dur)
        {
            int s = (int)(start * Rate);
            int n = (int)(dur * 3f * Rate);
            float lp = 0f;

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = rng.Signed();
                lp += (noise - lp) * 0.7f;
                Add(buf, s + i, (noise - lp) * MathF.Exp(-t / dur) * gain);
            }
        }

        /// <summary>Nota simples (baixo): seno com harmônicos opcionais (1 = só seno, 2 = com serra suave).</summary>
        private static void Tone(float[] buf, float start, float hz, float dur, float gain, int timbre, float attack, float release)
        {
            int s = (int)(start * Rate);
            int n = (int)((dur + release) * Rate);

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float env = Math.Min(1f, t / Math.Max(0.001f, attack)) * (t < dur ? 1f : Math.Max(0f, 1f - (t - dur) / release));
                float p = TwoPi * hz * t;
                float v = MathF.Sin(p);

                if (timbre >= 2)
                {
                    v = AudioMath.SoftClip((v + 0.6f * MathF.Sin(2f * p) + 0.4f * MathF.Sin(3f * p)) * 0.9f);
                }

                Add(buf, s + i, v * env * gain);
            }
        }

        private static void Pluck(float[] buf, float start, float hz, float gain, float decay)
        {
            int s = (int)(start * Rate);
            int n = (int)(decay * 5f * Rate);

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float p = TwoPi * hz * t;
                float v = MathF.Sin(p) + 0.5f * MathF.Sin(2f * p) * MathF.Exp(-t / (decay * 0.5f)) + 0.25f * MathF.Sin(3f * p) * MathF.Exp(-t / (decay * 0.3f));
                Add(buf, s + i, v * MathF.Exp(-t / decay) * gain * Math.Min(1f, t / 0.002f));
            }
        }

        /// <summary>Acorde em três vozes (toque de teclado/violão).</summary>
        private static void Chord(float[] buf, float start, float root, bool minor, float dur, float gain, int kind)
        {
            float[] ints = minor ? new float[] { 0f, 3f, 7f, 10f } : new float[] { 0f, 4f, 7f, 11f };

            foreach (float i in ints)
            {
                float hz = AudioMath.MidiToHz(root + i);
                int s = (int)(start * Rate);
                int n = (int)(dur * Rate * 1.2f);

                for (int k = 0; k < n; k++)
                {
                    float t = k / (float)Rate;
                    float p = TwoPi * hz * t;
                    float env = Math.Min(1f, t / 0.006f) * MathF.Exp(-t / (kind == 3 ? dur * 0.7f : dur * 0.35f));
                    float v = MathF.Sin(p) + (kind == 3 ? 0.18f : 0.5f) * MathF.Sin(2f * p) + 0.12f * MathF.Sin(3f * p);
                    Add(buf, s + k, v * env * gain * 0.5f);
                }
            }
        }

        private static void PowerChord(float[] buf, float start, float root, float dur)
        {
            int s = (int)(start * Rate);
            int n = (int)((dur + 0.05f) * Rate);
            float[] ints = { 0f, 7f, 12f };

            for (int k = 0; k < n; k++)
            {
                float t = k / (float)Rate;
                float v = 0f;

                foreach (float i in ints)
                {
                    float p = TwoPi * AudioMath.MidiToHz(root + i) * t;
                    v += MathF.Sin(p) + 0.5f * MathF.Sin(2f * p) + 0.33f * MathF.Sin(3f * p) + 0.25f * MathF.Sin(4f * p);
                }

                float env = Math.Min(1f, t / 0.003f) * (t < dur ? 1f : Math.Max(0f, 1f - (t - dur) / 0.05f));
                Add(buf, s + k, AudioMath.SoftClip(v * 0.5f) * env * 0.22f);
            }
        }

        // Soma no buffer com volta (o laço fica sem emenda).
        private static void Add(float[] buf, int index, float value)
        {
            buf[index % buf.Length] += value;
        }
    }
}
