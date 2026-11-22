using System;
using System.IO;

namespace Entregador.Game
{
    /// <summary>Grava amostras float mono em WAV PCM de 16 bits.</summary>
    public static class WavWriter
    {
        public static void Write(string path, float[] mono, int sampleRate)
        {
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var w = new BinaryWriter(stream))
            {
                int bytes = mono.Length * 2;
                w.Write(new[] { 'R', 'I', 'F', 'F' });
                w.Write(36 + bytes);
                w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                w.Write(16);
                w.Write((short)1);
                w.Write((short)1);
                w.Write(sampleRate);
                w.Write(sampleRate * 2);
                w.Write((short)2);
                w.Write((short)16);
                w.Write(new[] { 'd', 'a', 't', 'a' });
                w.Write(bytes);

                for (int i = 0; i < mono.Length; i++)
                {
                    float v = Math.Max(-1f, Math.Min(1f, mono[i]));
                    w.Write((short)Math.Round(v * 32767f));
                }
            }
        }
    }

    /// <summary>Renderiza fora do jogo amostras dos sons (motor, jingles, buzina, sirene, rádio) em WAV, para ouvir e revisar.</summary>
    public static class AudioPreviewRender
    {
        public const int Rate = 44100;

        /// <summary>Roteiro do motor: partida, marcha lenta, arrancada com trocas de marcha, limitador, freio-motor, frenagem e marcha lenta.</summary>
        public static float[] RenderMotor(MotorVoice voice, float idle = 1300f, float limit = 9000f)
        {
            float total = 21.5f;
            var buf = new float[(int)(total * Rate)];
            var synth = new MotorSynth(Rate, voice, 4242u) { IdleRpm = idle, MaxRpm = limit, Gain = 1f };
            const int Block = 256;
            var tmp = new float[Block];
            _neutralRpm = idle;

            for (int pos = 0; pos < buf.Length; pos += Block)
            {
                float t = pos / (float)Rate;
                Script(t, idle, limit, out float rpm, out float thr, out float starter, out float lim, out float gain, out float neutral);
                synth.Rpm = rpm;
                synth.Throttle = thr;
                synth.Starter = starter;
                synth.Limiter = lim;
                synth.Gain = gain;
                synth.Neutral = neutral;
                int n = Math.Min(Block, buf.Length - pos);
                synth.Render(tmp, 0, n);
                Array.Copy(tmp, 0, buf, pos, n);
            }

            return buf;
        }

        private static float _neutralRpm = 1300f;

        private static void Script(float t, float idle, float limit, out float rpm, out float thr, out float starter, out float lim, out float gain, out float neutral)
        {
            neutral = 0f;
            rpm = idle;
            thr = 0f;
            starter = 0f;
            lim = 0f;
            gain = 1f;

            if (t < 0.6f)
            {
                rpm = 350f;
                starter = 1f;
            }
            else if (t < 1.15f)
            {
                float u = (t - 0.6f) / 0.55f;
                rpm = AudioMath.Lerp(350f, idle * 1.35f, u);
                starter = Math.Max(0f, 1f - u * 2.5f);
            }
            else if (t < 3.5f)
            {
                rpm = idle;
            }
            else if (t < 9.2f)
            {
                // Arrancada: 5 trocas (rpm sobe até ~85% e cai para ~55%), acelerador cheio, corte curto na troca.
                float u = t - 3.5f;
                float[] gearTime = { 1.1f, 1.1f, 1.2f, 1.2f, 1.1f };
                float acc = 0f;
                int g = 0;

                while (g < gearTime.Length - 1 && u > acc + gearTime[g])
                {
                    acc += gearTime[g];
                    g++;
                }

                float within = (u - acc) / gearTime[g];
                float lowRpm = g == 0 ? idle * 1.4f : limit * 0.55f;
                float highRpm = limit * (g == gearTime.Length - 1 ? 1f : 0.85f);
                rpm = AudioMath.Lerp(lowRpm, highRpm, Math.Min(1f, within));
                thr = 1f;

                if (g > 0 && within < 0.1f)
                {
                    thr = 0f;
                    gain = 0.6f;
                }

                lim = g == gearTime.Length - 1 && within > 0.9f ? 1f : 0f;
            }
            else if (t < 10.2f)
            {
                rpm = limit;
                thr = 1f;
                lim = 1f;
            }
            else if (t < 13f)
            {
                // Solta o acelerador: freio-motor, giro cai devagar com estalos.
                float u = (t - 10.2f) / 2.8f;
                rpm = AudioMath.Lerp(limit, idle * 1.2f, u * u * 0.6f + u * 0.4f);
                thr = 0f;
            }
            else if (t < 15f)
            {
                rpm = AudioMath.Lerp(idle * 1.2f, idle, Math.Min(1f, (t - 13f) / 0.8f));
            }
            else if (t < 21.5f)
            {
                // Cortando giro: parado, embreagem puxada. Toques curtos (blips), depois segura no limitador, depois solta.
                neutral = 1f;
                float u = t - 15f;
                float x = 0f;

                if (u < 2.2f)
                {
                    x = (u % 0.55f) < 0.22f ? 1f : 0f;
                }
                else if (u < 4.6f)
                {
                    x = 1f;
                }

                thr = x;
                float goalRpm = x > 0.5f ? limit * 1.02f : idle;
                _neutralRpm = AudioMath.Lerp(_neutralRpm, goalRpm, goalRpm > _neutralRpm ? 0.05f : 0.02f);
                rpm = Math.Min(_neutralRpm, limit);
                lim = x > 0.5f && rpm >= limit * 0.97f ? 1f : 0f;
            }
            else
            {
                rpm = idle;
            }
        }

        /// <summary>Grava todas as amostras em dir. Devolve os caminhos gerados.</summary>
        public static string[] RenderAll(string dir)
        {
            Directory.CreateDirectory(dir);
            var paths = new System.Collections.Generic.List<string>();

            void Save(string name, float[] data, int rate = Rate)
            {
                string path = Path.Combine(dir, name);
                WavWriter.Write(path, data, rate);
                paths.Add(path);
            }

            Save("moto_titan_ciclo.wav", RenderMotor(MotorVoice.Titan, 1300f, 9000f));
            Save("moto_tornado_ciclo.wav", RenderMotor(MotorVoice.Twister, 1300f, 10000f));
            Save("jingle_pedido_rango.wav", SoundDesign.RenderJingle(JingleCatalog.Oferta("Rangô")));
            Save("jingle_pedido_outro_app.wav", SoundDesign.RenderJingle(JingleCatalog.Oferta("Entrega Ligeira")));
            Save("jingle_aceito.wav", SoundDesign.RenderJingle(JingleCatalog.Aceito("Rangô")));
            Save("jingle_perdido.wav", SoundDesign.RenderJingle(JingleCatalog.Perdido("Rangô")));
            Save("buzina.wav", SoundDesign.Horn());
            Save("ui_click_ping_plim_radar.wav", Concat(SoundDesign.Click(), Silence(0.4f), SoundDesign.Ping(), Silence(0.3f), SoundDesign.Plim(), Silence(0.3f), SoundDesign.RadarClick(), Silence(0.3f), SoundDesign.Whoop()));
            Save("squeal_freio.wav", Repeat(SoundDesign.Squeal(), 2));
            Save("carro_zumbido.wav", Repeat(SoundDesign.CarHum(), 4));
            Save("chuva.wav", SoundDesign.Rain());

            var siren = new SirenSynth(Rate);
            var sirenBuf = new float[Rate * 5];
            siren.Render(sirenBuf, 0, sirenBuf.Length);
            Save("sirene.wav", sirenBuf);

            foreach (RadioEstilo estilo in Enum.GetValues(typeof(RadioEstilo)))
            {
                Save("radio_" + estilo.ToString().ToLowerInvariant() + ".wav", RadioSynth.RenderLoop(estilo), RadioSynth.Rate);
            }

            return paths.ToArray();
        }

        private static float[] Silence(float seconds) => new float[(int)(seconds * Rate)];

        private static float[] Repeat(float[] a, int times)
        {
            var r = new float[a.Length * times];

            for (int i = 0; i < times; i++)
            {
                Array.Copy(a, 0, r, i * a.Length, a.Length);
            }

            return r;
        }

        private static float[] Concat(params float[][] parts)
        {
            int n = 0;

            foreach (float[] p in parts)
            {
                n += p.Length;
            }

            var r = new float[n];
            int pos = 0;

            foreach (float[] p in parts)
            {
                Array.Copy(p, 0, r, pos, p.Length);
                pos += p.Length;
            }

            return r;
        }
    }
}
