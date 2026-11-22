using UnityEngine;

namespace Entregador.Game
{
    /// <summary>
    /// Som de um carro do trânsito (TrafficLoop ou OncomingCar): zumbido grave de motor em 3D (com Doppler do Unity, rolloff de 5 a 60 m) e
    /// buzinas esporádicas ("fom-fom"), mais frequentes quando a moto anda na contramão, está colada no carro ou o carro está parado por ela.
    /// </summary>
    public sealed class CarAudio : MonoBehaviour
    {
        private const float OncomingSpeed = 9f;

        private static float _lastHornTime = -100f;

        private TrafficLoop _loop;
        private AudioSource _hum;
        private AudioSource _horn;
        private float _stoppedFor;
        private float _hornCooldown;
        private Vector3 _lastPos;
        private float _speed;

        public AudioSource Zumbido => _hum;

        public bool BuzinaTocando => _horn != null && _horn.isPlaying;

        private void Awake()
        {
            _loop = GetComponent<TrafficLoop>();
            _hum = NewSource("ZumbidoCarro", SoundBank.CarHum, true);
            _horn = NewSource("BuzinaCarro", SoundBank.Horn(Random.Range(0, 3)), false);
            _hum.Play();
            _hum.time = Random.value * 0.9f;
            _lastPos = transform.position;
        }

        private AudioSource NewSource(string name, AudioClip clip, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            AudioSource s = go.AddComponent<AudioSource>();
            s.clip = clip;
            s.loop = loop;
            s.playOnAwake = false;
            s.spatialBlend = 1f;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = 5f;
            s.maxDistance = 60f;
            s.dopplerLevel = 0.8f;
            s.priority = loop ? 150 : 100;

            return s;
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            if (dt <= 0f)
            {
                return;
            }

            Vector3 pos = transform.position;
            float measured = (pos - _lastPos).magnitude / dt;
            _lastPos = pos;
            _speed = _loop != null ? _loop.CurrentSpeed : OncomingSpeed;

            if (_loop == null)
            {
                _speed = Mathf.Max(_speed, measured);
            }

            float cruise = _loop != null ? Mathf.Max(1f, _loop.CruiseSpeed) : OncomingSpeed;
            float ratio = Mathf.Clamp01(_speed / cruise);
            _hum.pitch = 0.75f + 0.55f * ratio;
            _hum.volume = AudioSettingsJogo.Efetivo(AudioBus.Transito) * (0.4f + 0.6f * ratio) * 0.9f;
            _horn.volume = AudioSettingsJogo.Efetivo(AudioBus.Transito) * 0.8f;

            _hornCooldown -= dt;
            AudioDirector director = AudioDirector.Instance;

            if (director == null || director.Bike == null)
            {
                return;
            }

            float dist = Vector3.Distance(pos, director.Bike.Position);
            bool stoppedByPlayer = _loop != null && _loop.Stopped && dist < 14f;
            _stoppedFor = stoppedByPlayer ? _stoppedFor + dt : 0f;

            if (_hornCooldown > 0f || dist > 90f || _horn.isPlaying || Time.time - _lastHornTime < 1.5f)
            {
                return;
            }

            // Probabilidade de buzinar por segundo.
            float rate = 0.012f;

            if (dist < 35f)
            {
                rate *= 2f;
            }

            if (director.Monitor != null && director.Monitor.WrongWay && dist < 40f)
            {
                rate += 0.25f;
            }

            if (stoppedByPlayer && _stoppedFor > 1.2f)
            {
                rate += 0.5f;
            }

            if (dist < 7f && director.Bike.SpeedKmh > 15f)
            {
                rate += 0.35f;
            }

            if (Random.value < rate * dt)
            {
                Buzinar();
            }
        }

        public void Buzinar()
        {
            _horn.pitch = 0.94f + Random.value * 0.12f;
            _horn.Play();
            _hornCooldown = 8f;
            _lastHornTime = Time.time;
        }
    }

    /// <summary>
    /// Sirene da viatura (3D): perseguição = sirene cheia; blitz parada com a barra de luz ligada = sirene mais baixa. Liga e desliga sozinha
    /// pelo estado público da PoliceUnit. O pitch sobe e desce com a aproximação (Doppler manual).
    /// </summary>
    public sealed class SirenAudio : MonoBehaviour
    {
        private PoliceUnit _unit;
        private SirenSynth _synth;
        private AudioSource _source;
        private float[] _scratch = new float[4096];
        private float _volume;
        private float _lastDist = -1f;
        private float _pitch = 1f;
        private PoliceMode _lastMode = PoliceMode.Inativa;

        public bool Tocando => _source != null && _source.isPlaying && _volume > 0.001f;

        public bool Perseguicao => _unit != null && _unit.Mode == PoliceMode.Perseguicao;

        public float Volume => _volume;

        public static SirenAudio Anexar(PoliceUnit unit)
        {
            var go = new GameObject("AudioSirene");
            go.transform.SetParent(unit.transform, false);
            var siren = go.AddComponent<SirenAudio>();
            siren._unit = unit;

            return siren;
        }

        private void Awake()
        {
            int rate = AudioSettings.outputSampleRate;
            _synth = new SirenSynth(rate);
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = true;
            _source.spatialBlend = 1f;
            _source.rolloffMode = AudioRolloffMode.Logarithmic;
            _source.minDistance = 12f;
            _source.maxDistance = 220f;
            _source.dopplerLevel = 0f;
            _source.priority = 30;
            var carrier = AudioClip.Create("SirenePortador", 1024, 1, rate, false);
            var ones = new float[1024];

            for (int i = 0; i < ones.Length; i++)
            {
                ones[i] = 1f;
            }

            carrier.SetData(ones, 0);
            _source.clip = carrier;
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            SirenSynth synth = _synth;

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
            if (_unit == null)
            {
                return;
            }

            float target = 0f;

            if (_unit.Mode == PoliceMode.Perseguicao)
            {
                target = 1f;
            }
            else if (_unit.Mode == PoliceMode.Parada && _unit.LightsOn)
            {
                target = 0.4f;
            }

            if (_unit.Mode != _lastMode)
            {
                if (target > 0f && _volume < 0.01f)
                {
                    _synth.Reset();
                }

                _lastMode = _unit.Mode;
            }

            _volume = AudioMath.Smooth(_volume, target, Time.deltaTime, target > _volume ? 0.12f : 0.4f);

            if (target > 0f || _volume > 0.002f)
            {
                if (!_source.isPlaying)
                {
                    _source.Play();
                }
            }
            else if (_source.isPlaying)
            {
                _source.Stop();
            }

            _source.volume = _volume * 0.9f * AudioSettingsJogo.Efetivo(AudioBus.Transito);

            AudioDirector director = AudioDirector.Instance;

            if (director != null && director.Bike != null)
            {
                float dist = Vector3.Distance(transform.position, director.Bike.Position);

                if (_lastDist >= 0f && Time.deltaTime > 0f)
                {
                    float approach = (_lastDist - dist) / Time.deltaTime;
                    _pitch = AudioMath.Smooth(_pitch, AudioMath.DopplerPitch(approach), Time.deltaTime, 0.25f);
                    _synth.PitchMul = _pitch;
                }

                _lastDist = dist;
            }
        }
    }
}
