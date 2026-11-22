using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Entregador.Game
{
    /// <summary>
    /// Maestro do áudio. Nasce sozinho depois de carregar a cena (RuntimeInitializeOnLoadMethod), acha a moto, o trânsito, a polícia, o
    /// clima e a interface, e liga os sons por observação (sem editar esses sistemas): motor da moto, zumbido e buzina dos carros, sirenes,
    /// ambiente da cidade (rumor, passarinhos, chuva, vento, moto passando, buzina distante), cliques de botão, sininho de pedido, plim de
    /// pagamento, flash do radar, "uiu" da abordagem, e o rádio. Também faz o ducking (modal aberto, jingle, sirene).
    /// </summary>
    public sealed class AudioDirector : MonoBehaviour
    {
        private const float ScanInterval = 0.5f;

        private AudioSource _ui;
        private AudioSource _oneShot;
        private AudioSource _distantHorn;
        private AudioSource _city;
        private AudioSource _rain;
        private AudioSource _wind;

        private GameFlow _flow;
        private TransitoMonitor _monitor;
        private WeatherLook _look;
        private RainEffect _rainEffect;
        private DeliveryRunTracker _tracker;
        private GameUI _gameUi;

        private float _scanTimer;
        private float _fullScanTimer;
        private FlowState _lastState = (FlowState)(-1);
        private Entregador.Core.Order _lastOrder;
        private long _lastBalance = long.MinValue;
        private object _lastSession;
        private int _lastRadar;
        private string _lastToast = string.Empty;
        private float _jingleUntil;
        private bool _pendingOfferChime;
        private GameFlow _subscribedFlow;
        private float _birdTimer = 5f;
        private float _hornTimer = 25f;
        private float _passTimer = 30f;
        private float _rainVolume;
        private float _windVolume;
        private float _cityVolume;

        public static AudioDirector Instance { get; private set; }

        public ArcadeBikeController Bike { get; private set; }

        public TransitoMonitor Monitor => _monitor;

        public MotoEngineAudio Motor { get; private set; }

        public RadioPlayer Radio { get; private set; }

        /// <summary>Quantas buzinas distantes, pássaros etc. já foram tocados (para testes de diagnóstico).</summary>
        public int AmbientEvents { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Criar()
        {
            if (Instance != null || !Application.isPlaying)
            {
                return;
            }

            var go = new GameObject("AudioDirector");
            DontDestroyOnLoad(go);
            go.AddComponent<AudioDirector>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);

                return;
            }

            Instance = this;
            _ui = NewSource("Ui", false);
            _oneShot = NewSource("Ambiente1x", false);
            _distantHorn = NewSource("BuzinaDistante", false);
            _distantHorn.gameObject.AddComponent<AudioLowPassFilter>().cutoffFrequency = 1100f;
            _city = NewLoop("Cidade", SoundBank.CityBed);
            _rain = NewLoop("Chuva", SoundBank.Rain);
            _wind = NewLoop("Vento", SoundBank.Wind);
            Radio = RadioPlayer.Garantir(gameObject);
        }

        /// <summary>Oferta recusada ou vencida (parada ou no meio da corrida): o jingle de "perdido".</summary>
        private void OnOfferRefused()
        {
            Jingle(SoundBank.Perdido(AppName()), 0.6f, 1f);
        }

        /// <summary>Uma oferta apareceu (parada ou no meio da corrida): o sininho espera o "perdido" anterior terminar.</summary>
        private void OnOfertaApareceu()
        {
            _pendingOfferChime = true;
        }

        /// <summary>Oferta aceita (parada, no meio da corrida ou pedido duplo): o jingle de "aceito".</summary>
        private void OnOfertaAceita()
        {
            Jingle(SoundBank.Aceito(AppName()), 0.6f, 0.5f);
        }

        private void OnDestroy()
        {
            if (_subscribedFlow != null)
            {
                _subscribedFlow.OfferRefused -= OnOfferRefused;
                _subscribedFlow.OfertaApareceu -= OnOfertaApareceu;
                _subscribedFlow.OfertaAceita -= OnOfertaAceita;
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }

        private AudioSource NewSource(string name, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            AudioSource s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = loop;
            s.spatialBlend = 0f;

            return s;
        }

        private AudioSource NewLoop(string name, AudioClip clip)
        {
            AudioSource s = NewSource(name, true);
            s.clip = clip;
            s.volume = 0f;
            s.Play();

            return s;
        }

        /// <summary>Toca um som de interface (2D, barramento Ui).</summary>
        public void PlayUi(AudioClip clip, float volume = 1f)
        {
            if (clip != null)
            {
                _ui.PlayOneShot(clip, volume * AudioSettingsJogo.Efetivo(AudioBus.Ui));
            }
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _scanTimer -= dt;
            _fullScanTimer -= dt;

            if (_scanTimer <= 0f)
            {
                _scanTimer = ScanInterval;
                FindReferences();
                AttachButtons();
            }

            if (_fullScanTimer <= 0f)
            {
                _fullScanTimer = 1f;
                AttachWorld();
            }

            PollGame();
            UpdateDucking(dt);
            UpdateAmbience(dt);
        }

        private void FindReferences()
        {
            if (Bike == null)
            {
                Bike = FindFirstObjectByType<ArcadeBikeController>();
            }

            if (Bike != null && (Motor == null))
            {
                Motor = Bike.GetComponentInChildren<MotoEngineAudio>();

                if (Motor == null)
                {
                    Motor = MotoEngineAudio.Criar(Bike);
                }
            }

            if (_flow == null)
            {
                _flow = FindFirstObjectByType<GameFlow>();
            }

            if (_monitor == null)
            {
                _monitor = FindFirstObjectByType<TransitoMonitor>();
            }

            if (_look == null)
            {
                _look = FindFirstObjectByType<WeatherLook>();
            }

            if (_rainEffect == null)
            {
                _rainEffect = FindFirstObjectByType<RainEffect>();
            }

            if (_flow != null && _subscribedFlow != _flow)
            {
                _subscribedFlow = _flow;
                _flow.OfferRefused += OnOfferRefused;
                _flow.OfertaApareceu += OnOfertaApareceu;
                _flow.OfertaAceita += OnOfertaAceita;
            }

            if (_flow != null)
            {
                _tracker = _flow.Tracker;
                _gameUi = _flow.Ui;
            }
        }

        private void AttachWorld()
        {
            foreach (TrafficLoop car in FindObjectsByType<TrafficLoop>(FindObjectsSortMode.None))
            {
                if (car.GetComponent<CarAudio>() == null)
                {
                    car.gameObject.AddComponent<CarAudio>();
                }
            }

            foreach (OncomingCar car in FindObjectsByType<OncomingCar>(FindObjectsSortMode.None))
            {
                if (car.GetComponent<CarAudio>() == null)
                {
                    car.gameObject.AddComponent<CarAudio>();
                }
            }

            foreach (PoliceUnit unit in FindObjectsByType<PoliceUnit>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (unit.GetComponentInChildren<SirenAudio>(true) == null)
                {
                    SirenAudio.Anexar(unit);
                }
            }
        }

        private void AttachButtons()
        {
            foreach (Button button in FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (button.GetComponent<ClickSfx>() == null && button.GetComponent<TouchZone>() == null)
                {
                    button.gameObject.AddComponent<ClickSfx>();
                }
            }
        }

        private string AppName()
        {
            return _flow != null && _flow.Session != null ? _flow.Session.App.Config.Name : "Rangô";
        }

        private void Jingle(AudioClip clip, float volume, float duckSeconds)
        {
            PlayUi(clip, volume);
            _jingleUntil = Mathf.Max(_jingleUntil, Time.unscaledTime + duckSeconds);
        }

        private void PollGame()
        {
            if (_flow != null && _flow.Session != null)
            {
                FlowState state = _flow.State;
                bool newSession = !ReferenceEquals(_lastSession, _flow.Session);
                long balance = _flow.Session.Wallet.Balance.Cents;

                if (newSession)
                {
                    _lastSession = _flow.Session;
                    _lastState = state;
                    _lastBalance = balance;
                    _lastOrder = _flow.CurrentOrder;
                }
                else
                {
                    if (state == FlowState.Abordagem && _lastState != FlowState.Abordagem)
                    {
                        PlayUi(SoundBank.Whoop, 0.7f);
                    }

                    if (balance > _lastBalance && _lastBalance != long.MinValue)
                    {
                        PlayUi(SoundBank.Plim, 0.9f);
                    }

                    _lastState = state;
                    _lastBalance = balance;
                    _lastOrder = _flow.CurrentOrder;
                }
            }

            // O sininho do pedido espera o "perdido" da recusa terminar (a próxima oferta chega no mesmo quadro).
            if (_pendingOfferChime && Time.unscaledTime >= _jingleUntil)
            {
                _pendingOfferChime = false;
                Jingle(SoundBank.Oferta(AppName()), 0.9f, 1.5f);
            }

            if (_tracker != null)
            {
                int radar = _tracker.RadarCaptures;

                if (radar > _lastRadar)
                {
                    PlayUi(SoundBank.RadarClick, 0.9f);
                }

                _lastRadar = radar;
            }

            if (_gameUi != null)
            {
                string toast = _gameUi.ToastMessage;

                if (!string.IsNullOrEmpty(toast) && toast != _lastToast)
                {
                    PlayUi(SoundBank.Ping, 0.6f);
                }

                _lastToast = toast;
            }
        }

        private void UpdateDucking(float dt)
        {
            bool modal = _gameUi != null && _gameUi.ModalVisible;
            bool jingle = Time.unscaledTime < _jingleUntil;
            bool siren = false;

            foreach (PoliceUnit unit in PoliceUnit.Active)
            {
                if (unit != null && unit.SirenActive)
                {
                    siren = true;

                    break;
                }
            }

            float ambient = (modal ? 0.5f : 1f) * (jingle ? 0.45f : 1f);
            float traffic = (modal ? 0.75f : 1f) * (jingle ? 0.55f : 1f);
            float radio = (jingle ? 0.3f : 1f) * (siren ? 0.4f : 1f) * (AudioSettingsJogo.GpsFalando ? 0.3f : 1f);
            AudioSettingsJogo.DuckAmbiente = AudioMath.Smooth(AudioSettingsJogo.DuckAmbiente, ambient, dt, 0.15f);
            AudioSettingsJogo.DuckTransito = AudioMath.Smooth(AudioSettingsJogo.DuckTransito, traffic, dt, 0.15f);
            AudioSettingsJogo.DuckRadio = AudioMath.Smooth(AudioSettingsJogo.DuckRadio, radio, dt, 0.2f);
        }

        private void UpdateAmbience(float dt)
        {
            float bus = AudioSettingsJogo.Efetivo(AudioBus.Ambiente);
            float speedKmh = Bike != null ? Bike.SpeedKmh : 0f;
            bool raining = (_rainEffect != null && _rainEffect.Raining) || (_look != null && _look.Current == LookKind.Rain);

            _cityVolume = AudioMath.Smooth(_cityVolume, 0.55f, dt, 1f);
            _city.volume = _cityVolume * bus;
            _rainVolume = AudioMath.Smooth(_rainVolume, raining ? 0.7f : 0f, dt, 1.5f);
            _rain.volume = _rainVolume * bus;

            float windTarget = Mathf.Pow(Mathf.Clamp01(speedKmh / 110f), 1.3f) * 0.6f;
            _windVolume = AudioMath.Smooth(_windVolume, windTarget, dt, 0.4f);
            _wind.volume = _windVolume * bus;
            _wind.pitch = 0.8f + 0.6f * Mathf.Clamp01(speedKmh / 110f);

            if (Bike == null)
            {
                return;
            }

            bool daytime = _look == null || _look.Current == LookKind.Sunny;
            _birdTimer -= dt;
            _hornTimer -= dt;
            _passTimer -= dt;

            if (_birdTimer <= 0f)
            {
                _birdTimer = Random.Range(7f, 16f);

                if (daytime && !raining)
                {
                    _oneShot.panStereo = Random.Range(-0.8f, 0.8f);
                    _oneShot.PlayOneShot(SoundBank.Bird(Random.Range(0, 3)), 0.5f * bus);
                    AmbientEvents++;
                }
            }

            if (_hornTimer <= 0f)
            {
                _hornTimer = Random.Range(18f, 40f);
                _distantHorn.panStereo = Random.Range(-0.7f, 0.7f);
                _distantHorn.pitch = Random.Range(0.85f, 1.05f);
                _distantHorn.PlayOneShot(SoundBank.Horn(Random.Range(0, 3)), 0.3f * bus);
                AmbientEvents++;
            }

            if (_passTimer <= 0f)
            {
                _passTimer = Random.Range(25f, 50f);
                SpawnPassBy();
                AmbientEvents++;
            }
        }

        private void SpawnPassBy()
        {
            float angle = Random.value * 360f;
            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            Vector3 start = Bike.Position - dir * 80f + side * Random.Range(10f, 25f);
            var go = new GameObject("MotoPassando");
            go.transform.position = start;
            AudioSource s = go.AddComponent<AudioSource>();
            s.clip = SoundBank.Passagem;
            s.spatialBlend = 1f;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = 6f;
            s.maxDistance = 100f;
            s.dopplerLevel = 1f;
            s.volume = 0.7f * AudioSettingsJogo.Efetivo(AudioBus.Ambiente);
            s.Play();
            go.AddComponent<PassBy>().Begin(dir * 24f, 8f);
        }
    }

    /// <summary>Anda em linha reta e some (a moto que passa ao longe).</summary>
    public sealed class PassBy : MonoBehaviour
    {
        private Vector3 _velocity;
        private float _until;

        public void Begin(Vector3 velocity, float seconds)
        {
            _velocity = velocity;
            _until = Time.time + seconds;
        }

        private void Update()
        {
            transform.position += _velocity * Time.deltaTime;

            if (Time.time >= _until)
            {
                Destroy(gameObject);
            }
        }
    }

    /// <summary>Toca um clique quando o botão (Button) é tocado. Sobrevive ao RemoveAllListeners da interface porque é um handler de ponteiro.</summary>
    [RequireComponent(typeof(Button))]
    public sealed class ClickSfx : MonoBehaviour, IPointerClickHandler
    {
        public void OnPointerClick(PointerEventData eventData)
        {
            Button button = GetComponent<Button>();

            if (button != null && !button.IsInteractable())
            {
                return;
            }

            if (AudioDirector.Instance != null)
            {
                AudioDirector.Instance.PlayUi(SoundBank.Click, 0.8f);
            }
        }
    }
}
