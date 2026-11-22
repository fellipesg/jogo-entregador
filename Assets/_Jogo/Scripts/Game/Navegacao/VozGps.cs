using Entregador.Core;
using UnityEngine;

namespace Entregador.Game
{
    /// <summary>
    /// A voz do GPS. No Android usa o TextToSpeech nativo (pt-BR, iniciado uma vez, QUEUE_FLUSH para a fala nova cortar a velha).
    /// No iOS ainda não há voz (TODO: AVSpeechSynthesizer por um plugin nativo). No Editor e no desktop só toca um "ping" de dois tons,
    /// gerado em código, e a mensagem aparece em UltimoTexto. Preferências: PlayerPrefs "gps_voz" (1 liga, 0 desliga; padrão 1) e
    /// "vol_gps" (0 a 1; padrão 0,8).
    /// </summary>
    public sealed class VozGps : MonoBehaviour
    {
        public const string ChaveVoz = "gps_voz";
        public const string ChaveVolume = "vol_gps";

        private AudioSource _source;
        private AudioClip _ping;
        private RouteGuide _guide;

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _tts;
        private bool _ttsPronto;
        private string _pendente;
#endif

        /// <summary>Última frase falada (ou que seria falada).</summary>
        public string UltimoTexto { get; private set; } = string.Empty;

        public int Falas { get; private set; }

        public static bool VozLigada
        {
            get => SafeGetInt(ChaveVoz, 1) != 0;
            set
            {
                PlayerPrefs.SetInt(ChaveVoz, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Volume do barramento do GPS (PlayerPrefs "vol_gps", pelo AudioSettingsJogo; padrão 0,8). O som final ainda multiplica o volume geral.</summary>
        public static float Volume
        {
            get => AudioSettingsJogo.Get(AudioBus.Gps);
            set
            {
                AudioSettingsJogo.Set(AudioBus.Gps, value);
                PlayerPrefs.Save();
            }
        }

        private static int SafeGetInt(string key, int fallback)
        {
            try
            {
                return PlayerPrefs.GetInt(key, fallback);
            }
            catch
            {
                return fallback;
            }
        }

        private static float SafeGetFloat(string key, float fallback)
        {
            try
            {
                return PlayerPrefs.GetFloat(key, fallback);
            }
            catch
            {
                return fallback;
            }
        }

        private void Awake()
        {
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            _ping = BuildPing();
            _guide = GetComponent<RouteGuide>();
        }

        private void OnEnable()
        {
            if (_guide != null)
            {
                _guide.Anunciou += OnAnunciou;
            }

            IniciarAndroid();
        }

        private void OnDisable()
        {
            if (_guide != null)
            {
                _guide.Anunciou -= OnAnunciou;
            }
        }

        private void OnDestroy()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_tts != null)
            {
                _tts.Call("shutdown");
                _tts.Dispose();
                _tts = null;
            }
#endif
        }

        private void OnAnunciou(string texto, Manobra manobra)
        {
            Falar(texto);
        }

        /// <summary>Fala a frase (ou toca o ping, onde não há voz). Respeita "gps_voz" e "vol_gps".</summary>
        public void Falar(string texto)
        {
            UltimoTexto = texto;
            Falas++;

            if (!VozLigada || AudioSettingsJogo.Efetivo(AudioBus.Gps) <= 0.001f)
            {
                return;
            }

            // O rádio abaixa enquanto o GPS fala (o AudioDirector lê isto).
            AudioSettingsJogo.GpsFalandoAte = Time.unscaledTime + Mathf.Clamp(0.5f + (texto != null ? texto.Length : 0) * 0.065f, 0.6f, 6f);

#if UNITY_ANDROID && !UNITY_EDITOR
            FalarAndroid(texto);
#elif UNITY_IOS && !UNITY_EDITOR
            // TODO(iOS): falar com AVSpeechSynthesizer (plugin nativo). Por enquanto só o ping.
            Ping();
#else
            Ping();
#endif
        }

        private void Ping()
        {
            if (_source == null || _ping == null)
            {
                return;
            }

            _source.volume = AudioSettingsJogo.Efetivo(AudioBus.Gps);
            _source.PlayOneShot(_ping);
        }

        /// <summary>Dois tons curtos (ré e lá agudos) com envelope suave.</summary>
        private static AudioClip BuildPing()
        {
            const int rate = 22050;
            const float tone = 0.11f;
            int perTone = Mathf.RoundToInt(rate * tone);
            var data = new float[perTone * 2];
            float[] freqs = { 880f, 1318.5f };

            for (int t = 0; t < 2; t++)
            {
                for (int i = 0; i < perTone; i++)
                {
                    float x = i / (float)perTone;
                    float envelope = Mathf.Sin(Mathf.PI * Mathf.Min(1f, x * 1.1f)) * (1f - x * 0.3f);
                    data[t * perTone + i] = Mathf.Sin(2f * Mathf.PI * freqs[t] * i / rate) * envelope * 0.35f;
                }
            }

            AudioClip clip = AudioClip.Create("GpsPing", data.Length, 1, rate, false);
            clip.SetData(data, 0);

            return clip;
        }

        // ---------- Android ----------

#if UNITY_ANDROID && !UNITY_EDITOR
        private sealed class InitListener : AndroidJavaProxy
        {
            private readonly VozGps _owner;

            public InitListener(VozGps owner) : base("android.speech.tts.TextToSpeech$OnInitListener")
            {
                _owner = owner;
            }

            public void onInit(int status)
            {
                _owner.AoIniciar(status);
            }
        }

        private void IniciarAndroid()
        {
            if (_tts != null)
            {
                return;
            }

            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    _tts = new AndroidJavaObject("android.speech.tts.TextToSpeech", activity, new InitListener(this));
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[VozGps] TextToSpeech indisponível: " + e.Message);
                _tts = null;
            }
        }

        private void AoIniciar(int status)
        {
            if (status != 0 || _tts == null)
            {
                return;
            }

            using (var locale = new AndroidJavaObject("java.util.Locale", "pt", "BR"))
            {
                _tts.Call<int>("setLanguage", locale);
            }

            _ttsPronto = true;

            if (!string.IsNullOrEmpty(_pendente))
            {
                FalarAndroid(_pendente);
                _pendente = null;
            }
        }

        private void FalarAndroid(string texto)
        {
            if (_tts == null || !_ttsPronto)
            {
                _pendente = texto;
                Ping();

                return;
            }

            using (var parametros = new AndroidJavaObject("android.os.Bundle"))
            {
                parametros.Call("putFloat", "volume", AudioSettingsJogo.Efetivo(AudioBus.Gps));
                _tts.Call<int>("speak", texto, 0, parametros, "gps");
            }
        }
#else
        private void IniciarAndroid()
        {
        }
#endif
    }
}
