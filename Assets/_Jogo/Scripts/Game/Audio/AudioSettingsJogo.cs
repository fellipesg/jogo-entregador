using System;
using UnityEngine;

namespace Entregador.Game
{
    /// <summary>Os barramentos (buses) de volume do jogo.</summary>
    public enum AudioBus
    {
        Motor,
        Transito,
        Ambiente,
        Ui,
        Radio,

        /// <summary>Voz e ping do GPS (PlayerPrefs "vol_gps", padrão 0,8).</summary>
        Gps,
    }

    /// <summary>
    /// Volumes do jogo (0 a 1), gravados no PlayerPrefs, para uma tela de opções ligar depois.
    /// Cada fonte de som multiplica o próprio volume por Efetivo(bus): volume geral x volume do barramento x ducking.
    /// </summary>
    public static class AudioSettingsJogo
    {
        private const string Prefix = "jogo.audio.";

        private static readonly float[] BusDefault = { 1f, 1f, 1f, 1f, 1f, 0.8f };
        private static readonly float[] BusVolume = { 1f, 1f, 1f, 1f, 1f, 0.8f };
        private static readonly string[] BusNames = { "vol_motor", "vol_transito", "vol_ambiente", "vol_ui", "vol_radio", "vol_gps" };
        private static float _master = 1f;
        private static bool _loaded;

        /// <summary>Avisa quando algum volume muda.</summary>
        public static event Action Changed;

        /// <summary>Multiplicador de ducking do trânsito (a diretoria de áudio baixa quando há um modal ou um jingle).</summary>
        public static float DuckTransito { get; set; } = 1f;

        /// <summary>Multiplicador de ducking do ambiente.</summary>
        public static float DuckAmbiente { get; set; } = 1f;

        /// <summary>Multiplicador de ducking do rádio (baixa sob o jingle de pedido e a sirene).</summary>
        public static float DuckRadio { get; set; } = 1f;

        /// <summary>Instante (tempo não escalado) até o qual o GPS está falando; o rádio abaixa enquanto isso.</summary>
        public static float GpsFalandoAte { get; set; }

        public static bool GpsFalando => Time.unscaledTime < GpsFalandoAte;

        public static float Master
        {
            get
            {
                Load();

                return _master;
            }
            set
            {
                Load();
                _master = Mathf.Clamp01(value);
                Save(Prefix + "master", _master);
            }
        }

        public static float Get(AudioBus bus)
        {
            Load();

            return BusVolume[(int)bus];
        }

        public static void Set(AudioBus bus, float volume)
        {
            Load();
            BusVolume[(int)bus] = Mathf.Clamp01(volume);
            Save(BusNames[(int)bus], BusVolume[(int)bus]);
        }

        /// <summary>Volume final de um barramento: geral x barramento x ducking.</summary>
        public static float Efetivo(AudioBus bus)
        {
            Load();
            float duck = bus == AudioBus.Ambiente ? DuckAmbiente : (bus == AudioBus.Transito ? DuckTransito : (bus == AudioBus.Radio ? DuckRadio : 1f));

            return _master * BusVolume[(int)bus] * duck;
        }

        /// <summary>Volta tudo ao padrão, sem gravar (os testes usam).</summary>
        public static void Reset()
        {
            _master = 1f;

            for (int i = 0; i < BusVolume.Length; i++)
            {
                BusVolume[i] = BusDefault[i];
            }

            GpsFalandoAte = 0f;

            DuckAmbiente = 1f;
            DuckTransito = 1f;
            DuckRadio = 1f;
            _loaded = true;
        }

        private static void Load()
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            _master = PlayerPrefs.GetFloat(Prefix + "master", 1f);

            for (int i = 0; i < BusVolume.Length; i++)
            {
                BusVolume[i] = PlayerPrefs.GetFloat(BusNames[i], BusDefault[i]);
            }
        }

        private static void Save(string key, float value)
        {
            PlayerPrefs.SetFloat(key, value);
            Changed?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _loaded = false;
            DuckAmbiente = 1f;
            DuckTransito = 1f;
            DuckRadio = 1f;
            Changed = null;
        }
    }
}
