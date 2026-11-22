using System;

namespace Entregador.Game
{
    /// <summary>Timbre de um modelo de moto: o mesmo sintetizador, parâmetros diferentes.</summary>
    public struct MotorVoice
    {
        /// <summary>Frequência do corpo grave de cada explosão (Hz) na marcha lenta.</summary>
        public float BodyHz;

        /// <summary>Brilho do escape (0 a 1,2).</summary>
        public float Brightness;

        /// <summary>Quanto de ruído de admissão/mecânico.</summary>
        public float Intake;

        /// <summary>Irregularidade da marcha lenta (0 = regular, 1 = bem "pocando").</summary>
        public float Lump;

        /// <summary>Quantidade de estalos ao soltar o acelerador.</summary>
        public float Crackle;

        public static MotorVoice Titan => new MotorVoice { BodyHz = 88f, Brightness = 0.85f, Intake = 0.8f, Lump = 0.9f, Crackle = 0.9f };

        public static MotorVoice Twister => new MotorVoice { BodyHz = 118f, Brightness = 1.1f, Intake = 1.25f, Lump = 0.5f, Crackle = 1.3f };
    }

    /// <summary>
    /// Sintetizador de um motor de moto monocilíndrico de 4 tempos (CG 160 Titan, Twister 250), puro C# (sem Unity),
    /// para rodar em OnAudioFilterRead ou offline.
    /// Cada explosão (rpm / 120 por segundo) dispara: um corpo grave ressonante com queda de tom (o "tum" do escape), uma rajada de ruído
    /// filtrado (o "pop"), um estalo mecânico de válvula. Em cima disso: ruído de admissão que cresce com o acelerador, estalos de
    /// freio-motor ao soltar o acelerador em giro alto, motor de arranque, corte do limitador, e um filtro de abafador que abre com a carga.
    /// Marcha lenta é irregular (força e tempo de cada explosão variam, de vez em quando falha uma).
    /// Os campos "alvo" podem ser escritos pela thread principal; a thread de áudio os suaviza (sem zíper).
    /// </summary>
    public sealed class MotorSynth
    {
        private readonly float _sr;
        private readonly AudioRng _rng;
        private MotorVoice _voice;

        /// <summary>Alvos (escritos de fora).</summary>
        public float Rpm = 1300f;
        public float Throttle;
        public float Gain = 1f;

        /// <summary>Motor de arranque, 0 a 1.</summary>
        public float Starter;

        /// <summary>1 enquanto o limitador de giro corta explosões (corte e estalo ~13 Hz).</summary>
        public float Limiter;

        /// <summary>1 com a embreagem puxada (giro livre, sem carga): som mais leve e rápido.</summary>
        public float Neutral;

        public float IdleRpm = 1300f;
        public float MaxRpm = 9000f;

        // Estado.
        private float _rpm;
        private float _thr;
        private float _gain;
        private float _phase;
        private float _bodyPhase;
        private float _bodyEnv;
        private float _chirp;
        private float _noiseEnv;
        private float _tickEnv;
        private float _crackEnv;
        private float _lpA;
        private float _lpB;
        private float _lpC;
        private float _intake;
        private float _muffler;
        private float _dc;
        private float _starterPhase;
        private float _neutral;
        private float _limClock;
        private bool _limCut;

        public MotorSynth(float sampleRate, MotorVoice voice, uint seed = 12345u)
        {
            _sr = sampleRate;
            _voice = voice;
            _rng = new AudioRng(seed);
            _rpm = Rpm;
            _gain = 0f;
        }

        public void SetVoice(MotorVoice voice)
        {
            _voice = voice;
        }

        /// <summary>Giro suavizado atual (a thread de áudio).</summary>
        public float CurrentRpm => _rpm;

        /// <summary>Gera frames amostras mono em buffer[offset...].</summary>
        public void Render(float[] buffer, int offset, int frames)
        {
            float kRpm = AudioMath.OnePole(0.035f, _sr);
            float kThr = AudioMath.OnePole(0.05f, _sr);
            float kGain = AudioMath.OnePole(0.02f, _sr);
            float twoPi = 2f * MathF.PI;

            for (int i = 0; i < frames; i++)
            {
                _rpm += (Rpm - _rpm) * kRpm;
                _thr += (Throttle - _thr) * kThr;
                _gain += (Gain - _gain) * kGain;
                _neutral += (Neutral - _neutral) * kThr;

                // Limitador: corta as explosões em ~13 Hz; a cada corte, um estalo seco no escape.
                bool wasCut = _limCut;

                if (Limiter > 0.5f)
                {
                    _limClock += 13.5f / _sr;

                    if (_limClock >= 1f)
                    {
                        _limClock -= 1f;
                    }

                    _limCut = _limClock < 0.5f;
                }
                else
                {
                    _limCut = false;
                    _limClock = 0f;
                }

                if (_limCut && !wasCut)
                {
                    _crackEnv = 1f;
                    _bodyEnv += 0.55f;
                    _chirp = 1f;
                    _noiseEnv = Math.Max(_noiseEnv, 0.9f);
                }

                float rpmN = AudioMath.RpmNormalizado(_rpm, IdleRpm, MaxRpm);
                float thr = _thr;

                // Explosões.
                _phase += AudioMath.FiringHz(_rpm) / _sr;

                if (_phase >= 1f)
                {
                    _phase -= 1f;
                    Fire(rpmN);
                }

                // Corpo: seno amortecido com queda de tom, 3 harmônicas.
                float tauBody = AudioMath.Lerp(0.055f, 0.017f, rpmN) * (1f - 0.35f * _neutral);
                _bodyEnv *= MathF.Exp(-1f / (tauBody * _sr));
                _chirp *= MathF.Exp(-1f / (0.012f * _sr));
                float bodyHz = _voice.BodyHz * (1f + 0.55f * rpmN) * (1f + 0.7f * _chirp);
                _bodyPhase += bodyHz / _sr;

                if (_bodyPhase > 1f)
                {
                    _bodyPhase -= 1f;
                }

                float bp = _bodyPhase * twoPi;
                float body = (MathF.Sin(bp) + 0.45f * MathF.Sin(2f * bp) + 0.22f * MathF.Sin(3f * bp)) * _bodyEnv;

                // Ruído do pop (passa-banda por diferença de dois passa-baixas) e estalo mecânico (passa-alta).
                float n = _rng.Signed();
                float aN = AudioMath.Lerp(0.06f, 0.42f, AudioMath.Clamp01(_voice.Brightness * (0.35f + 0.65f * thr) * (0.5f + 0.5f * rpmN) + 0.12f * _neutral));
                _lpA += (n - _lpA) * aN;
                _lpB += (_lpA - _lpB) * aN;
                _lpC += (n - _lpC) * 0.45f;
                _noiseEnv *= MathF.Exp(-1f / (AudioMath.Lerp(0.02f, 0.008f, rpmN) * _sr));
                _tickEnv *= MathF.Exp(-1f / (0.0018f * _sr));
                _crackEnv *= MathF.Exp(-1f / (0.0035f * _sr));
                float pop = (_lpA - _lpB) * 5f * _noiseEnv;
                float tick = (n - _lpC) * _tickEnv * 0.22f;

                // Admissão: ruído grave que cresce com o acelerador e o giro.
                _intake += (_rng.Signed() - _intake) * 0.09f;
                float intake = _intake * (0.015f + 0.22f * thr * (0.25f + rpmN)) * _voice.Intake * 1.6f;

                // Estalos de freio-motor: acelerador solto com giro alto.
                if (thr < 0.12f && _rpm > IdleRpm * 1.8f)
                {
                    float p = _voice.Crackle * (1.5f + 9f * rpmN) / _sr;

                    if (_rng.Value() < p)
                    {
                        _crackEnv = 0.45f + 0.55f * _rng.Value();
                        _bodyEnv += 0.25f * _crackEnv;
                        _chirp = 0.6f;
                    }
                }

                float crack = (n - _lpC) * _crackEnv * 1.1f;

                // Motor de arranque: zumbido que sobe com o giro.
                float starter = 0f;

                if (Starter > 0.001f)
                {
                    _starterPhase += (110f + _rpm * 0.22f) / _sr;

                    if (_starterPhase > 1f)
                    {
                        _starterPhase -= 1f;
                    }

                    float sp = _starterPhase * twoPi;
                    starter = (MathF.Sin(sp) + 0.35f * MathF.Sin(3f * sp) + 0.2f * n) * 0.1f * Starter;
                }

                float mix = body * 0.95f + pop + tick + intake + crack + starter;

                // Abafador: abre com carga e giro.
                float cm = AudioMath.Lerp(0.15f, 0.55f, AudioMath.Clamp01(_voice.Brightness * (0.28f + 0.72f * thr) * (0.6f + 0.4f * rpmN)));
                _muffler += (mix - _muffler) * cm;

                // Volume: marcha lenta baixa, cheio com acelerador e giro.
                float loud = AudioMath.Lerp(0.6f, 1f, AudioMath.Clamp01(0.75f * thr + 0.25f * rpmN));
                float y = AudioMath.SoftClip(_muffler * loud * (1.5f - 0.2f * _neutral));

                // Tira o nível contínuo (DC).
                _dc += (y - _dc) * 0.004f;
                y -= _dc;

                buffer[offset + i] = y * _gain * 0.62f;
            }

        }

        private void Fire(float rpmN)
        {
            float load = Math.Min(1f, 0.32f + 0.68f * _thr + 0.4f * (1f - rpmN) * (1f - rpmN));
            float lump = _voice.Lump * (1f - rpmN) * (1f - rpmN);
            float amp = load * (1f - lump * 0.55f * _rng.Value());

            if (lump > 0.15f && _rng.Value() < 0.10f * lump)
            {
                amp *= 0.35f;
            }

            if (_limCut)
            {
                return;
            }

            // Tempo da próxima explosão varia um pouco (mais na marcha lenta).
            _phase -= _rng.Signed() * 0.5f * (0.06f * lump + 0.008f);

            _bodyEnv = amp;
            _chirp = 1f;
            _noiseEnv = amp * (0.45f + 0.55f * _thr);
            _tickEnv = 1f;
            _bodyPhase = 0f;
        }

        /// <summary>Atalho para o OnAudioFilterRead: gera mono e copia para todos os canais.</summary>
        public void RenderInterleaved(float[] data, int channels, float[] scratch)
        {
            int frames = data.Length / channels;
            Render(scratch, 0, frames);

            for (int f = 0; f < frames; f++)
            {
                float v = scratch[f];

                for (int c = 0; c < channels; c++)
                {
                    data[f * channels + c] = v;
                }
            }
        }
    }

    /// <summary>Sirene da polícia: dois tons que se alternam (ver SirenMath), com harmônicos e distorção leve, puro C#.</summary>
    public sealed class SirenSynth
    {
        private readonly float _sr;
        private float _time;
        private float _phase;

        /// <summary>Fator de pitch (Doppler), escrito de fora.</summary>
        public float PitchMul = 1f;

        public SirenSynth(float sampleRate)
        {
            _sr = sampleRate;
        }

        public void Reset()
        {
            _time = 0f;
            _phase = 0f;
        }

        public void Render(float[] buffer, int offset, int frames)
        {
            float twoPi = 2f * MathF.PI;

            for (int i = 0; i < frames; i++)
            {
                float f = SirenMath.FrequencyAt(_time) * PitchMul;
                _phase += f / _sr;

                if (_phase > 1f)
                {
                    _phase -= 1f;
                }

                float p = _phase * twoPi;
                float s = MathF.Sin(p) + 0.45f * MathF.Sin(2f * p) + 0.3f * MathF.Sin(3f * p) + 0.12f * MathF.Sin(5f * p);
                buffer[offset + i] = AudioMath.SoftClip(s * 0.9f) * 0.5f;
                _time += 1f / _sr;
            }
        }

        public void RenderInterleaved(float[] data, int channels, float[] scratch)
        {
            int frames = data.Length / channels;
            Render(scratch, 0, frames);

            for (int f = 0; f < frames; f++)
            {
                for (int c = 0; c < channels; c++)
                {
                    data[f * channels + c] = scratch[f];
                }
            }
        }
    }
}
