using System;
using Entregador.Core;
using UnityEngine;

namespace Entregador.Game
{
    public enum RadioStreamState
    {
        Parado,

        /// <summary>Abrindo a conexão ("Sintonizando…").</summary>
        Conectando,

        /// <summary>Conectado, mas sem áudio suficiente ainda (ou o buffer secou).</summary>
        Buffering,
        Tocando,
        Erro,
    }

    /// <summary>
    /// Um jeito de tocar um stream de rádio ao vivo (infinito) pela internet. O Unity toca MP3/OGG sozinho (UnityRadioBackend);
    /// no Android o MediaPlayer nativo toca também AAC e HLS (AndroidRadioBackend); no iOS fica um esqueleto até existir o plugin de AVPlayer.
    /// Tudo é chamado da thread principal.
    /// </summary>
    public interface IRadioStreamBackend
    {
        string Nome { get; }

        /// <summary>O que este backend toca (usado para filtrar a lista de estações).</summary>
        RadioCaps Caps { get; }

        RadioStreamState Estado { get; }

        /// <summary>Último erro, em texto (para o log).</summary>
        string Erro { get; }

        /// <summary>Título da música (metadados ICY), se o backend conseguir ler; senão null.</summary>
        string AgoraTocando { get; }

        void Tocar(string url);

        void Parar();

        /// <summary>Volume final de 0 a 1. O Android aplica por setVolume a cada mudança; o Unity usa o AudioSource.</summary>
        void SetVolume(float volume);

        /// <summary>Chamado todo quadro (tempo real, não escalado).</summary>
        void Tick(float dt);

        void Dispose();
    }

    /// <summary>
    /// Toca a estação sintonizada: escolhe o backend da plataforma, mostra "Sintonizando…" enquanto enche o buffer, reconecta se a conexão cai
    /// (1 s, 2 s...) e, passadas <see cref="RadioDirectory.MaxReconexoes"/> quedas seguidas, avisa que a estação está fora do ar.
    /// Para ao pausar o app (OnApplicationPause) e volta a sintonizar quando o jogador retorna.
    /// </summary>
    public sealed class RadioStream : MonoBehaviour
    {
        private IRadioStreamBackend _backend;
        private string _url;
        private bool _quer;
        private bool _pausado;
        private int _tentativa;
        private float _espera;
        private float _tocandoHa;
        private float _volume = -1f;
        private RadioStreamState _ultimo;

        public RadioStreamState Estado => _quer && _espera > 0f ? RadioStreamState.Conectando : (_backend != null ? _backend.Estado : RadioStreamState.Parado);

        public RadioCaps Caps => Backend.Caps;

        public string BackendNome => Backend.Nome;

        public string AgoraTocando => _backend?.AgoraTocando;

        public string UrlAtual => _url;

        /// <summary>Tentativas de reconexão feitas desde que a estação foi sintonizada (ou desde o último trecho estável).</summary>
        public int Tentativas => _tentativa;

        /// <summary>Segundos desde que o áudio começou a sair (0 se não está tocando).</summary>
        public float TocandoHa => Estado == RadioStreamState.Tocando ? _tocandoHa : 0f;

        public event Action<RadioStreamState> EstadoMudou;

        /// <summary>O stream caiu e as tentativas acabaram.</summary>
        public event Action ForaDoAr;

        /// <summary>O áudio começou a sair pela primeira vez nesta sintonia.</summary>
        public event Action Comecou;

        private bool _comecou;

        private IRadioStreamBackend Backend => _backend ?? (_backend = RadioBackends.Criar(gameObject));

        /// <summary>Troca o backend (os testes usam um de mentira).</summary>
        public void UsarBackend(IRadioStreamBackend backend)
        {
            _backend?.Dispose();
            _backend = backend;
            _volume = -1f;
        }

        public void Tocar(string url)
        {
            _url = url;
            _quer = true;
            _tentativa = 0;
            _espera = 0f;
            _comecou = false;
            _tocandoHa = 0f;

            if (!_pausado)
            {
                Backend.Tocar(url);
            }
        }

        public void Parar()
        {
            _quer = false;
            _url = null;
            _espera = 0f;
            _comecou = false;
            _backend?.Parar();
            Notificar();
        }

        /// <summary>Volume final (0 a 1), já com volume geral, barramento do rádio e ducking.</summary>
        public void SetVolume(float volume)
        {
            if (Mathf.Abs(volume - _volume) < 0.002f)
            {
                return;
            }

            _volume = volume;
            Backend.SetVolume(volume);
        }

        private void Update()
        {
            if (_backend == null)
            {
                return;
            }

            float dt = Time.unscaledDeltaTime;
            _backend.Tick(dt);

            if (_quer && !_pausado)
            {
                if (_espera > 0f)
                {
                    _espera -= dt;

                    if (_espera <= 0f)
                    {
                        _espera = 0f;
                        _backend.Tocar(_url);
                    }
                }
                else if (_backend.Estado == RadioStreamState.Erro)
                {
                    Debug.Log($"[Rádio] {_backend.Nome}: erro ({_backend.Erro}); tentativa {_tentativa + 1}");
                    _backend.Parar();

                    if (_tentativa >= RadioDirectory.MaxReconexoes)
                    {
                        _quer = false;
                        ForaDoAr?.Invoke();
                    }
                    else
                    {
                        _espera = RadioDirectory.EsperaReconexao(_tentativa);
                        _tentativa++;
                    }
                }
                else if (_backend.Estado == RadioStreamState.Tocando)
                {
                    _tocandoHa += dt;

                    if (!_comecou)
                    {
                        _comecou = true;
                        Comecou?.Invoke();
                    }

                    // Trecho estável: a próxima queda recomeça a contagem.
                    if (_tocandoHa > 20f)
                    {
                        _tentativa = 0;
                    }
                }
                else
                {
                    _tocandoHa = 0f;
                }
            }

            Notificar();
        }

        private void Notificar()
        {
            RadioStreamState now = Estado;

            if (now != _ultimo)
            {
                _ultimo = now;
                EstadoMudou?.Invoke(now);
            }
        }

        /// <summary>Segundo plano: stream ao vivo não "pausa", então para e, ao voltar, sintoniza de novo.</summary>
        private void OnApplicationPause(bool paused)
        {
            _pausado = paused;

            if (paused)
            {
                _backend?.Parar();
            }
            else if (_quer && !string.IsNullOrEmpty(_url))
            {
                _tentativa = 0;
                _espera = 0f;
                _backend.Tocar(_url);
            }
        }

        private void OnDestroy()
        {
            _backend?.Dispose();
        }
    }

    /// <summary>Escolhe o backend da plataforma.</summary>
    public static class RadioBackends
    {
        public static IRadioStreamBackend Criar(GameObject host)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidRadioBackend();
#elif UNITY_IOS && !UNITY_EDITOR
            return new IosRadioBackend(host);
#else
            return new UnityRadioBackend(host);
#endif
        }
    }
}
