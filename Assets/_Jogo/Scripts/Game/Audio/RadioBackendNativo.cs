using Entregador.Core;
using UnityEngine;

namespace Entregador.Game
{
#if UNITY_ANDROID && !UNITY_EDITOR
    /// <summary>
    /// Backend do Android: android.media.MediaPlayer por AndroidJavaObject (setDataSource, prepareAsync, start, stop, setVolume).
    /// Toca MP3, AAC (ADTS) e HLS (.m3u8) sem biblioteca nenhuma. O som sai pelo mixer do sistema, fora do mixer do Unity: o volume do
    /// barramento do rádio e o ducking chegam por setVolume (RadioStream chama SetVolume a cada mudança).
    /// Os ouvintes (prepared, error, info) são chamados em outra thread: só gravam números, e o Tick lê na thread principal.
    /// ATENÇÃO: não foi possível testar num aparelho neste ambiente (sem dispositivo Android); a lógica segue a API documentada do MediaPlayer.
    /// </summary>
    public sealed class AndroidRadioBackend : IRadioStreamBackend
    {
        private const int InfoBufferingStart = 701;
        private const int InfoBufferingEnd = 702;
        private const float TempoParaComecar = 20f;

        private AndroidJavaObject _player;
        private float _volume = 1f;
        private float _desdeInicio;
        private volatile int _prepared;
        private volatile int _erro;
        private volatile int _buffering;
        private volatile int _geracao;

        public string Nome => "Android MediaPlayer";

        public RadioCaps Caps => RadioCaps.Nativo;

        public RadioStreamState Estado { get; private set; }

        public string Erro { get; private set; }

        public string AgoraTocando => null;

        private sealed class Ouvinte : AndroidJavaProxy
        {
            private readonly AndroidRadioBackend _dono;
            private readonly int _geracao;

            public Ouvinte(AndroidRadioBackend dono, string iface, int geracao) : base(iface)
            {
                _dono = dono;
                _geracao = geracao;
            }

            // MediaPlayer.OnPreparedListener
            public void onPrepared(AndroidJavaObject mp)
            {
                if (_geracao == _dono._geracao)
                {
                    _dono._prepared = 1;
                }
            }

            // MediaPlayer.OnErrorListener (devolve true: tratamos o erro)
            public bool onError(AndroidJavaObject mp, int what, int extra)
            {
                if (_geracao == _dono._geracao)
                {
                    _dono._erro = what * 1000 + (extra & 0x3FF) + 1;
                }

                return true;
            }

            // MediaPlayer.OnInfoListener
            public bool onInfo(AndroidJavaObject mp, int what, int extra)
            {
                if (_geracao == _dono._geracao)
                {
                    if (what == InfoBufferingStart)
                    {
                        _dono._buffering = 1;
                    }
                    else if (what == InfoBufferingEnd)
                    {
                        _dono._buffering = 0;
                    }
                }

                return false;
            }

            // MediaPlayer.OnCompletionListener: um stream ao vivo não "completa"; se completou, caiu.
            public void onCompletion(AndroidJavaObject mp)
            {
                if (_geracao == _dono._geracao)
                {
                    _dono._erro = -1;
                }
            }
        }

        public void Tocar(string url)
        {
            Parar();
            Erro = null;
            _geracao++;
            _prepared = 0;
            _erro = 0;
            _buffering = 0;
            _desdeInicio = 0f;
            Estado = RadioStreamState.Conectando;

            try
            {
                _player = new AndroidJavaObject("android.media.MediaPlayer");

                using (var builder = new AndroidJavaObject("android.media.AudioAttributes$Builder"))
                {
                    builder.Call<AndroidJavaObject>("setContentType", 2).Dispose(); // CONTENT_TYPE_MUSIC
                    builder.Call<AndroidJavaObject>("setUsage", 1).Dispose(); // USAGE_MEDIA

                    using (AndroidJavaObject attrs = builder.Call<AndroidJavaObject>("build"))
                    {
                        _player.Call("setAudioAttributes", attrs);
                    }
                }

                _player.Call("setOnPreparedListener", new Ouvinte(this, "android.media.MediaPlayer$OnPreparedListener", _geracao));
                _player.Call("setOnErrorListener", new Ouvinte(this, "android.media.MediaPlayer$OnErrorListener", _geracao));
                _player.Call("setOnInfoListener", new Ouvinte(this, "android.media.MediaPlayer$OnInfoListener", _geracao));
                _player.Call("setOnCompletionListener", new Ouvinte(this, "android.media.MediaPlayer$OnCompletionListener", _geracao));
                _player.Call("setVolume", _volume, _volume);
                _player.Call("setDataSource", url);
                _player.Call("prepareAsync");
            }
            catch (System.Exception e)
            {
                Falhar(e.Message);
            }
        }

        public void Parar()
        {
            _geracao++;

            if (_player != null)
            {
                try
                {
                    _player.Call("reset");
                    _player.Call("release");
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[Rádio] MediaPlayer.release: " + e.Message);
                }

                _player.Dispose();
                _player = null;
            }

            Estado = RadioStreamState.Parado;
        }

        public void SetVolume(float volume)
        {
            _volume = volume;

            if (_player != null)
            {
                try
                {
                    _player.Call("setVolume", volume, volume);
                }
                catch (System.Exception)
                {
                    // Player em estado inválido (já caiu): o próximo Tocar recria.
                }
            }
        }

        public void Tick(float dt)
        {
            if (_player == null || Estado == RadioStreamState.Erro)
            {
                return;
            }

            if (_erro != 0)
            {
                Falhar("MediaPlayer erro " + _erro);

                return;
            }

            _desdeInicio += dt;

            if (_prepared == 0)
            {
                if (_desdeInicio > TempoParaComecar)
                {
                    Falhar("não preparou em " + TempoParaComecar + " s");
                }

                return;
            }

            if (Estado == RadioStreamState.Conectando)
            {
                try
                {
                    _player.Call("start");
                    Estado = RadioStreamState.Buffering;
                }
                catch (System.Exception e)
                {
                    Falhar(e.Message);

                    return;
                }
            }

            try
            {
                Estado = _buffering == 1 || !_player.Call<bool>("isPlaying") ? RadioStreamState.Buffering : RadioStreamState.Tocando;
            }
            catch (System.Exception e)
            {
                Falhar(e.Message);
            }
        }

        private void Falhar(string motivo)
        {
            Erro = motivo;
            Estado = RadioStreamState.Erro;
        }

        public void Dispose()
        {
            Parar();
        }
    }
#endif

    /// <summary>
    /// iOS: esqueleto. Por enquanto toca só o que o Unity toca (MP3/OGG) pelo UnityRadioBackend.
    /// TODO: plugin nativo com AVPlayer (AVPlayerItem com a URL do stream; cobre AAC e HLS) exposto por um .mm com DllImport("__Internal"),
    /// no mesmo contrato de IRadioStreamBackend (Tocar, Parar, SetVolume, estado por KVO de timeControlStatus/AVPlayerItem.status).
    /// </summary>
    public sealed class IosRadioBackend : IRadioStreamBackend
    {
        private readonly UnityRadioBackend _unity;

        public IosRadioBackend(GameObject host)
        {
            _unity = new UnityRadioBackend(host);
        }

        public string Nome => "iOS (Unity, AVPlayer pendente)";

        public RadioCaps Caps => _unity.Caps;

        public RadioStreamState Estado => _unity.Estado;

        public string Erro => _unity.Erro;

        public string AgoraTocando => null;

        public void Tocar(string url) => _unity.Tocar(url);

        public void Parar() => _unity.Parar();

        public void SetVolume(float volume) => _unity.SetVolume(volume);

        public void Tick(float dt) => _unity.Tick(dt);

        public void Dispose() => _unity.Dispose();
    }
}
