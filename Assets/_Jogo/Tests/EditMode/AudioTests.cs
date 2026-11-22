using System;
using System.IO;
using NUnit.Framework;

namespace Entregador.Game.Tests
{
    /// <summary>Testes da matemática pura do som: motor, sirene, jingles, rádio ao vivo.</summary>
    public sealed class AudioTests
    {
        private static float Rms(float[] buf, int from, int count)
        {
            double sum = 0;

            for (int i = from; i < from + count; i++)
            {
                sum += buf[i] * buf[i];
            }

            return (float)Math.Sqrt(sum / count);
        }

        private static float[] RenderSteady(float rpm, float throttle, float seconds, bool settleFirst = true, MotorVoice? voice = null)
        {
            var synth = new MotorSynth(44100, voice ?? MotorVoice.Titan, 99u) { Rpm = rpm, Throttle = throttle };
            var warm = new float[settleFirst ? 44100 : 0];

            if (settleFirst)
            {
                synth.Render(warm, 0, warm.Length);
            }

            var buf = new float[(int)(seconds * 44100)];
            synth.Render(buf, 0, buf.Length);

            return buf;
        }

        [Test]
        public void FiringFrequencyIsRpmOver120()
        {
            Assert.AreEqual(10f, AudioMath.FiringHz(1200f), 1e-4f);
            Assert.AreEqual(75f, AudioMath.FiringHz(9000f), 1e-4f);
            Assert.AreEqual(0f, AudioMath.FiringHz(-50f));
        }

        [Test]
        public void SmoothingConvergesWithoutOvershoot()
        {
            float v = 0f;

            for (int i = 0; i < 200; i++)
            {
                float next = AudioMath.Smooth(v, 1f, 0.016f, 0.1f);
                Assert.GreaterOrEqual(next, v);
                Assert.LessOrEqual(next, 1f);
                v = next;
            }

            Assert.AreEqual(1f, v, 1e-3f);
            Assert.AreEqual(5f, AudioMath.Smooth(1f, 5f, 1f, 0f), "tau zero salta direto");
            float k = AudioMath.OnePole(0.03f, 44100f);
            Assert.Greater(k, 0f);
            Assert.Less(k, 1f);
        }

        [Test]
        public void MidiAndDopplerAreCorrect()
        {
            Assert.AreEqual(440f, AudioMath.MidiToHz(69f), 1e-3f);
            Assert.AreEqual(880f, AudioMath.MidiToHz(81f), 1e-2f);
            Assert.Greater(AudioMath.DopplerPitch(20f), 1f);
            Assert.Less(AudioMath.DopplerPitch(-20f), 1f);
            Assert.AreEqual(1f, AudioMath.DopplerPitch(0f), 1e-6f);
        }

        [Test]
        public void SirenAlternatesTwoTonesWithTheRightTiming()
        {
            Assert.AreEqual(SirenMath.HiHz, SirenMath.FrequencyAt(SirenMath.GlideSeconds + 0.1f), 0.01f);
            Assert.AreEqual(SirenMath.LoHz, SirenMath.FrequencyAt(SirenMath.ToneSeconds + SirenMath.GlideSeconds + 0.1f), 0.01f);
            Assert.AreEqual(SirenMath.FrequencyAt(0.3f), SirenMath.FrequencyAt(0.3f + SirenMath.Period), 1e-3f, "periódica");
            Assert.AreEqual(1f, SirenMath.Period, 1e-6f);

            float prev = SirenMath.FrequencyAt(0f);

            for (float t = 0.001f; t < 3f; t += 0.001f)
            {
                float f = SirenMath.FrequencyAt(t);
                Assert.Less(Math.Abs(f - prev), 40f, "a transição é suave, sem salto, em t=" + t);
                Assert.That(f, Is.InRange(SirenMath.LoHz - 0.01f, SirenMath.HiHz + 0.01f));
                prev = f;
            }
        }

        [Test]
        public void SirenSynthIsBoundedAndAudible()
        {
            var siren = new SirenSynth(44100);
            var buf = new float[44100];
            siren.Render(buf, 0, buf.Length);
            Assert.Greater(Rms(buf, 0, buf.Length), 0.1f);

            foreach (float v in buf)
            {
                Assert.That(Math.Abs(v), Is.LessThanOrEqualTo(1f));
            }
        }

        [Test]
        public void EngineSynthIsFiniteBoundedAndDeterministic()
        {
            float[] a = RenderSteady(4000f, 1f, 0.5f);
            float[] b = RenderSteady(4000f, 1f, 0.5f);

            for (int i = 0; i < a.Length; i++)
            {
                Assert.IsFalse(float.IsNaN(a[i]) || float.IsInfinity(a[i]));
                Assert.LessOrEqual(Math.Abs(a[i]), 1f);
                Assert.AreEqual(a[i], b[i]);
            }
        }

        [Test]
        public void EngineIsLouderWithThrottleAndRevs()
        {
            float idle = Rms(RenderSteady(1300f, 0f, 1f), 0, 44100);
            float rev = Rms(RenderSteady(7000f, 1f, 1f), 0, 44100);
            float coast = Rms(RenderSteady(7000f, 0f, 1f), 0, 44100);

            Assert.Greater(idle, 0.01f, "a marcha lenta é audível");
            Assert.Greater(rev, idle * 1.6f, "acelerando alto é bem mais forte que a marcha lenta");
            Assert.Greater(rev, coast * 1.3f, "acelerador solto (freio-motor) é claramente mais baixo que acelerador cheio");
        }

        [Test]
        public void ThrottleOffAtHighRpmHasCrackles()
        {
            float[] coast = RenderSteady(7000f, 0f, 2f);
            float[] steadyLow = RenderSteady(1300f, 0f, 2f);

            // Estalos = picos esparsos bem acima do nível médio.
            float PeakToRms(float[] b) => Peak(b) / Math.Max(1e-6f, Rms(b, 0, b.Length));

            Assert.Greater(PeakToRms(coast), 2.5f);
            Assert.Greater(Peak(coast), 0.05f);
            Assert.Greater(Peak(steadyLow), 0.02f);
        }

        private static float Peak(float[] b)
        {
            float m = 0f;

            foreach (float v in b)
            {
                m = Math.Max(m, Math.Abs(v));
            }

            return m;
        }

        [Test]
        public void StarterAddsEnergyAndLimiterCutsFirings()
        {
            var plain = new MotorSynth(44100, MotorVoice.Titan, 5u) { Rpm = 400f, Throttle = 0f };
            var crank = new MotorSynth(44100, MotorVoice.Titan, 5u) { Rpm = 400f, Throttle = 0f, Starter = 1f };
            var a = new float[22050];
            var b = new float[22050];
            plain.Render(a, 0, a.Length);
            crank.Render(b, 0, b.Length);
            Assert.Greater(Rms(b, 0, b.Length), Rms(a, 0, a.Length));

            float full = Rms(RenderSteady(9000f, 1f, 1f), 0, 44100);
            var lim = new MotorSynth(44100, MotorVoice.Titan, 99u) { Rpm = 9000f, Throttle = 1f, Limiter = 1f };
            var warm = new float[44100];
            lim.Render(warm, 0, warm.Length);
            var limited = new float[44100];
            lim.Render(limited, 0, limited.Length);
            Assert.Less(Rms(limited, 0, limited.Length), full, "o limitador corta explosões e o som cai");
        }

        [Test]
        public void TornadoSoundsDifferentFromTitan()
        {
            float[] titan = RenderSteady(5000f, 0.8f, 0.5f, true, MotorVoice.Titan);
            float[] tornado = RenderSteady(5000f, 0.8f, 0.5f, true, MotorVoice.Twister);
            bool different = false;

            for (int i = 0; i < titan.Length; i++)
            {
                if (Math.Abs(titan[i] - tornado[i]) > 1e-4f)
                {
                    different = true;

                    break;
                }
            }

            Assert.IsTrue(different);
        }

        [Test]
        public void JinglesAreOriginalAndShaped()
        {
            Jingle rango = JingleCatalog.Oferta("Rangô");
            Jingle outro = JingleCatalog.Oferta("Entrega Ligeira");
            Assert.AreEqual(4, rango.Midi.Length);
            Assert.AreEqual(4, outro.Midi.Length);
            CollectionAssert.AreNotEqual(rango.Midi, outro.Midi, "cada app tem o seu jingle");
            CollectionAssert.AreEqual(rango.Midi, JingleCatalog.Oferta("Rangô").Midi, "determinístico");

            for (int i = 1; i < rango.Tempo.Length; i++)
            {
                Assert.Greater(rango.Tempo[i], rango.Tempo[i - 1]);
            }

            Jingle perdido = JingleCatalog.Perdido("Rangô");

            for (int i = 1; i < perdido.Midi.Length; i++)
            {
                Assert.Less(perdido.Midi[i], perdido.Midi[i - 1], "o do pedido perdido desce");
            }

            float[] offer = SoundDesign.RenderJingle(rango);
            float duration = offer.Length / 44100f;
            Assert.That(duration, Is.InRange(1.0f, 3.5f));
            Assert.AreEqual(rango.Peak, Peak(offer), 0.01f);
            Assert.Less(SoundDesign.RenderJingle(JingleCatalog.Aceito("Rangô")).Length, offer.Length);
        }

        [Test]
        public void NoiseLoopsCloseWithoutAClick()
        {
            float[] loop = SoundDesign.NoiseLoop(1f, 500f, 40f, 3);
            float step = Math.Abs(loop[0] - loop[loop.Length - 1]);
            float typical = 0f;

            for (int i = 1; i < loop.Length; i++)
            {
                typical = Math.Max(typical, Math.Abs(loop[i] - loop[i - 1]));
            }

            Assert.LessOrEqual(step, typical * 1.5f + 1e-4f);
        }

        // ---------- Rádio ao vivo ----------

        [Test]
        public void ShuffleIsAPermutationAndDeterministic()
        {
            int[] a = RadioCore.Embaralhar(7, 3u);
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3, 4, 5, 6 }, a);
            CollectionAssert.AreEqual(a, RadioCore.Embaralhar(7, 3u));
        }

        [Test]
        public void LivePositionAdvancesWithTimeAndStaysInsideTracks()
        {
            float[] dur = { 100f, 60f, 80f };
            RadioCore.Localizar(dur, 5u, 0.0, out int t0, out float o0);
            Assert.GreaterOrEqual(t0, 0);
            Assert.AreEqual(0f, o0, 1e-3f);

            RadioCore.Localizar(dur, 5u, 10.0, out int t1, out float o1);
            Assert.AreEqual(t0, t1, "ainda na mesma faixa");
            Assert.AreEqual(10f, o1, 1e-3f);

            // Os tempos "ao vivo" independem de quem escutou: mesma pergunta, mesma resposta.
            RadioCore.Localizar(dur, 5u, 10.0, out int t2, out float o2);
            Assert.AreEqual(t1, t2);
            Assert.AreEqual(o1, o2);

            for (double t = 0; t < 1500; t += 7.3)
            {
                RadioCore.Localizar(dur, 5u, t, out int faixa, out float offset);
                Assert.That(faixa, Is.InRange(0, 2));
                Assert.That(offset, Is.InRange(0f, dur[faixa]));
            }
        }

        [Test]
        public void EveryTrackPlaysOncePerCycle()
        {
            float[] dur = { 30f, 30f, 30f, 30f };
            var seen = new System.Collections.Generic.HashSet<int>();

            for (double t = 1; t < 120; t += 30)
            {
                RadioCore.Localizar(dur, 9u, t, out int faixa, out _);
                seen.Add(faixa);
            }

            Assert.AreEqual(4, seen.Count);
        }

        [Test]
        public void SingleTrackLoopsForever()
        {
            RadioCore.Localizar(new[] { 20f }, 1u, 45.0, out int faixa, out float offset);
            Assert.AreEqual(0, faixa);
            Assert.AreEqual(5f, offset, 1e-3f);
        }

        [Test]
        public void StationWheelCyclesThroughOff()
        {
            Assert.AreEqual(0, RadioCore.Proxima(-1, 5));
            Assert.AreEqual(4, RadioCore.Anterior(-1, 5));
            Assert.AreEqual(-1, RadioCore.Proxima(4, 5));
            Assert.AreEqual(-1, RadioCore.Anterior(0, 5));
            Assert.AreEqual(-1, RadioCore.Proxima(-1, 0));
        }

        [Test]
        public void PlaceholderLoopsHaveWholeBarsAboutTwentySeconds()
        {
            foreach (RadioEstilo e in Enum.GetValues(typeof(RadioEstilo)))
            {
                Assert.That(RadioSynth.Duracao(e), Is.InRange(14f, 26f), e.ToString());
            }

            float[] loop = RadioSynth.RenderLoop(RadioEstilo.Funk);
            Assert.AreEqual((int)(RadioSynth.Duracao(RadioEstilo.Funk) * RadioSynth.Rate), loop.Length);
            Assert.Greater(Rms(loop, 0, loop.Length), 0.05f);
        }

        [Test]
        public void BusVolumesMultiplyAndReset()
        {
            AudioSettingsJogo.Reset();
            AudioSettingsJogo.DuckAmbiente = 0.5f;
            Assert.AreEqual(0.5f, AudioSettingsJogo.Efetivo(AudioBus.Ambiente), 1e-5f);
            Assert.AreEqual(1f, AudioSettingsJogo.Efetivo(AudioBus.Motor), 1e-5f);
            AudioSettingsJogo.Reset();
            Assert.AreEqual(1f, AudioSettingsJogo.Efetivo(AudioBus.Ambiente), 1e-5f);
        }

        // ---------- Cortando giro ----------

        [Test]
        public void ClutchRevReachesTheLimiterInHalfASecondAndBounces()
        {
            MotoFisicaPerfil perfil = MotoFisicaCatalogo.Titan;
            var t = new Transmissao(perfil);
            float dt = 0.02f;
            float time = 0f;

            while (t.Rpm < perfil.RpmLimite - 30f && time < 2f)
            {
                t.StepNeutro(1f, dt);
                time += dt;
            }

            Assert.That(time, Is.InRange(0.3f, 0.7f), "sobe até o limitador em ~0,5 s");
            Assert.IsTrue(t.Embreagem);

            float min = float.MaxValue;
            float max = 0f;
            int cuts = 0;
            bool wasLow = false;

            for (int i = 0; i < 50; i++)
            {
                t.StepNeutro(1f, dt);
                min = Math.Min(min, t.Rpm);
                max = Math.Max(max, t.Rpm);
                bool low = t.Rpm < perfil.RpmLimite - 300f;

                if (low && !wasLow)
                {
                    cuts++;
                }

                wasLow = low;
            }

            Assert.Greater(max, perfil.RpmLimite - 50f);
            Assert.Less(min, perfil.RpmLimite - 400f, "cada corte derruba mais de 400 rpm");
            Assert.GreaterOrEqual(cuts, 8, "quica em ~12 a 15 Hz (1 s = 8 a 15 cortes)");
            Assert.LessOrEqual(cuts, 16);
            Assert.IsTrue(t.NoLimitador);

            for (int i = 0; i < 60; i++)
            {
                t.StepNeutro(0f, dt);
            }

            Assert.Less(t.Rpm, perfil.RpmMarchaLenta + 200f, "solto o acelerador, o giro cai");
            Assert.IsFalse(t.NoLimitador);
        }

        [Test]
        public void ClutchBlipsFollowThrottlePulses()
        {
            MotoFisicaPerfil perfil = MotoFisicaCatalogo.Titan;
            var t = new Transmissao(perfil);

            for (int i = 0; i < 12; i++)
            {
                t.StepNeutro(1f, 0.02f);
            }

            float peak = t.Rpm;
            Assert.Greater(peak, perfil.RpmMarchaLenta + 3000f);

            for (int i = 0; i < 12; i++)
            {
                t.StepNeutro(0f, 0.02f);
            }

            Assert.Less(t.Rpm, peak - 2000f);
        }

        [Test]
        public void RevLimiterAlsoBouncesInGear()
        {
            MotoFisicaPerfil perfil = MotoFisicaCatalogo.Titan;
            var t = new Transmissao(perfil);
            float top = perfil.MarchaVmax[perfil.Marchas - 1];
            float min = float.MaxValue;

            for (int i = 0; i < 200; i++)
            {
                t.Step(top * 1.2f, 1f, 0.02f);

                if (i > 60)
                {
                    min = Math.Min(min, t.Rpm);
                }
            }

            Assert.IsFalse(t.Embreagem);
            Assert.Less(min, perfil.RpmLimite - 400f, "no limite em qualquer marcha o giro quica");
        }

        [Test]
        public void RenderPreviewWavFiles()
        {
            string dir = Environment.GetEnvironmentVariable("JOGO_AUDIO_RENDER_DIR");

            if (string.IsNullOrEmpty(dir))
            {
                dir = Path.Combine(Path.GetTempPath(), "jogo_audio_preview");
            }

            string[] files = AudioPreviewRender.RenderAll(dir);
            Assert.GreaterOrEqual(files.Length, 10);

            foreach (string f in files)
            {
                Assert.Greater(new FileInfo(f).Length, 1000, f);
            }

            TestContext.WriteLine("Amostras em " + dir);
        }
    }
}
