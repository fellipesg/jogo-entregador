using UnityEngine;

namespace Entregador.Game
{
    /// <summary>
    /// Som da moto: lê o ArcadeBikeController (Rpm, Gear, Throttle, Brake, SpeedKmh) e alimenta o MotorSynth, que roda em OnAudioFilterRead.
    /// Comportamentos: partida (motor de arranque) quando o primeiro turno libera a moto, marcha lenta irregular, aceleração (mais alto e
    /// brilhante), freio-motor ao soltar o acelerador (mais baixo, com estalos), corte rápido a cada troca de marcha, rebote do limitador
    /// de giro e rangido de pneu/freio nas freadas fortes.
    /// </summary>
    public sealed class MotoEngineAudio : MonoBehaviour
    {
        private ArcadeBikeController _bike;
        private MotorSynth _synth;
        private AudioSource _source;
        private AudioSource _squeal;
        private float[] _scratch = new float[4096];
        private int _lastGear;
        private float _shiftUntil;
        private bool _started;
        private float _startTime;
        private float _squealVolume;
        private MotoModelo _voiceModel = (MotoModelo)(-1);

        /// <summary>Giro que o sintetizador está seguindo (o alvo escrito a partir do controlador).</summary>
        public float RpmAlvo => _synth != null ? _synth.Rpm : 0f;

        public float ThrottleAlvo => _synth != null ? _synth.Throttle : 0f;

        public float GainAlvo => _synth != null ? _synth.Gain : 0f;

        public float LimiterAlvo => _synth != null ? _synth.Limiter : 0f;

        public float StarterAlvo => _synth != null ? _synth.Starter : 0f;

        public float VolumeSquealAlvo => _squealVolume;

        public bool Tocando => _source != null && _source.isPlaying;

        public bool Ligado => _started;

        public AudioSource Fonte => _source;

        public static MotoEngineAudio Criar(ArcadeBikeController bike)
        {
            var go = new GameObject("AudioMotor");
            go.transform.SetParent(bike.transform, false);
            var engine = go.AddComponent<MotoEngineAudio>();
            engine._bike = bike;

            return engine;
        }

        private void Awake()
        {
            int rate = AudioSettings.outputSampleRate;
            _synth = new MotorSynth(rate, MotorVoice.Titan) { Gain = 0f };
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            _source.loop = true;
            _source.priority = 20;

            // O clipe é só um "portador" constante; o OnAudioFilterRead troca o conteúdo pelo som sintetizado.
            var carrier = AudioClip.Create("MotorPortador", 1024, 1, rate, false);
            var ones = new float[1024];

            for (int i = 0; i < ones.Length; i++)
            {
                ones[i] = 1f;
            }

            carrier.SetData(ones, 0);
            _source.clip = carrier;
            _source.Play();

            var sq = new GameObject("RangidoFreio");
            sq.transform.SetParent(transform, false);
            _squeal = sq.AddComponent<AudioSource>();
            _squeal.clip = SoundBank.Squeal;
            _squeal.loop = true;
            _squeal.spatialBlend = 0f;
            _squeal.volume = 0f;
            _squeal.playOnAwake = false;
            _squeal.Play();
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            MotorSynth synth = _synth;

            if (synth == null)
            {
                return;
            }

            int frames = data.Length / channels;

            if (_scratch.Length < frames)
            {
                _scratch = new float[frames];
            }

            synth.RenderInterleaved(data, channels, _scratch);
        }

        private void Update()
        {
            if (_bike == null)
            {
                return;
            }

            if (_voiceModel != _bike.Modelo)
            {
                _voiceModel = _bike.Modelo;
                _synth.SetVoice(_voiceModel == MotoModelo.Twister ? MotorVoice.Twister : MotorVoice.Titan);
                _synth.IdleRpm = _bike.Perfil.RpmMarchaLenta;
                _synth.MaxRpm = _bike.RpmLimit;
            }

            float volume = AudioSettingsJogo.Efetivo(AudioBus.Motor);
            _source.volume = volume;

            // Motor desligado até a primeira liberação da moto (começo do turno): aí dá a partida.
            if (!_started && !_bike.Locked)
            {
                _started = true;
                _startTime = Time.time;
            }

            float rpm = _bike.Rpm;
            float throttle = _bike.Throttle;
            float gain = 1f;
            float starter = 0f;

            if (!_started)
            {
                gain = 0f;
            }
            else
            {
                float since = Time.time - _startTime;

                if (since < 1.15f)
                {
                    // Partida: arranque a ~350 rpm (motor falhando), depois pega e sobe até a marcha lenta.
                    float idle = _bike.Perfil.RpmMarchaLenta;

                    if (since < 0.6f)
                    {
                        rpm = 350f;
                        starter = 1f;
                    }
                    else
                    {
                        float u = (since - 0.6f) / 0.55f;
                        rpm = Mathf.Lerp(350f, idle * 1.35f, u);
                        starter = Mathf.Clamp01(1f - u * 2.5f);
                    }

                    throttle = 0f;
                }
            }

            // Troca de marcha: corte curto do acelerador e um mergulho no volume.
            if (_bike.Gear != _lastGear)
            {
                if (_lastGear != 0)
                {
                    _shiftUntil = Time.time + 0.13f;
                }

                _lastGear = _bike.Gear;
            }

            if (Time.time < _shiftUntil)
            {
                throttle = 0f;
                gain *= 0.6f;
            }

            _synth.Rpm = rpm;
            _synth.Throttle = throttle;
            _synth.Gain = gain;
            _synth.Starter = starter;
            bool limiter = _bike.NoLimitador && throttle > 0.5f && _started && Time.time >= _shiftUntil;
            _synth.Limiter = limiter ? 1f : 0f;
            _synth.Neutral = _bike.CortandoGiro ? 1f : 0f;

            if (limiter)
            {
                // O giro da moto quica; o sintetizador faz o corte sozinho, então segura o giro médio perto do limite.
                rpm = Mathf.Max(rpm, _bike.RpmLimit * 0.97f);
                _synth.Rpm = rpm;
            }

            // Rangido de pneu/freio: só em freada forte e rápida.
            float target = _bike.Brake > 0.75f && _bike.SpeedKmh > 18f ? Mathf.Clamp01((_bike.SpeedKmh - 18f) / 30f) * 0.25f : 0f;
            _squealVolume = AudioMath.Smooth(_squealVolume, target, Time.deltaTime, target > _squealVolume ? 0.06f : 0.2f);
            _squeal.volume = _squealVolume * volume;
            _squeal.pitch = 0.9f + 0.25f * Mathf.Clamp01(_bike.SpeedKmh / 80f);
        }
    }
}
