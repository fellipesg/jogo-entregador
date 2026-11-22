using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Entregador.Game
{
    /// <summary>Uma estação de rádio: nome, gênero, cor e as faixas. Sem faixas, usa o laço sintetizado do estilo (provisório).</summary>
    [Serializable]
    public sealed class RadioEstacao
    {
        public string Nome = "Estação";
        public string Genero = "";
        public Color Cor = Color.white;
        public RadioEstilo Estilo;
        public List<AudioClip> Faixas = new List<AudioClip>();

        /// <summary>Vinheta (opcional); sem ela, usa a sintetizada.</summary>
        public AudioClip Vinheta;

        [NonSerialized] private List<AudioClip> _geradas;
        [NonSerialized] private AudioClip _vinhetaGerada;

        /// <summary>Faixas a tocar: as do catálogo ou, se não houver nenhuma, o laço provisório.</summary>
        public IReadOnlyList<AudioClip> FaixasEfetivas()
        {
            if (Faixas != null)
            {
                foreach (AudioClip clip in Faixas)
                {
                    if (clip != null)
                    {
                        return Faixas;
                    }
                }
            }

            if (_geradas == null)
            {
                float[] data = RadioSynth.RenderLoop(Estilo);
                var clip = AudioClip.Create("Radio_" + Nome, data.Length, 1, RadioSynth.Rate, false);
                clip.SetData(data, 0);
                _geradas = new List<AudioClip> { clip };
            }

            return _geradas;
        }

        public AudioClip VinhetaEfetiva()
        {
            if (Vinheta != null)
            {
                return Vinheta;
            }

            if (_vinhetaGerada == null)
            {
                float[] data = RadioSynth.RenderVinheta(Estilo);
                _vinhetaGerada = AudioClip.Create("Vinheta_" + Nome, data.Length, 1, RadioSynth.Rate, false);
                _vinhetaGerada.SetData(data, 0);
            }

            return _vinhetaGerada;
        }
    }

    /// <summary>Catálogo das estações (asset em Assets/_Jogo/Audio/Radio/Resources). Sem o asset, vale o padrão do código.</summary>
    [CreateAssetMenu(menuName = "Jogo/Catálogo do rádio", fileName = "RadioCatalogo")]
    public sealed class RadioCatalogo : ScriptableObject
    {
        public List<RadioEstacao> Estacoes = new List<RadioEstacao>();

        public static List<RadioEstacao> Padrao()
        {
            return new List<RadioEstacao>
            {
                new RadioEstacao { Nome = "Quebrada FM", Genero = "Funk e rap", Cor = new Color(0.95f, 0.75f, 0.1f), Estilo = RadioEstilo.Funk },
                new RadioEstacao { Nome = "Sertanejo 98", Genero = "Sertanejo", Cor = new Color(0.25f, 0.7f, 0.3f), Estilo = RadioEstilo.Sertanejo },
                new RadioEstacao { Nome = "Paredão Pagode", Genero = "Pagode", Cor = new Color(0.9f, 0.35f, 0.15f), Estilo = RadioEstilo.Pagode },
                new RadioEstacao { Nome = "Rock Marginal", Genero = "Rock", Cor = new Color(0.8f, 0.1f, 0.15f), Estilo = RadioEstilo.Rock },
                new RadioEstacao { Nome = "Lo-fi Corre", Genero = "Lo-fi", Cor = new Color(0.45f, 0.5f, 0.9f), Estilo = RadioEstilo.LoFi },
            };
        }

        public static List<RadioEstacao> Carregar()
        {
            var catalogo = Resources.Load<RadioCatalogo>("RadioCatalogo");

            if (catalogo != null && catalogo.Estacoes != null && catalogo.Estacoes.Count > 0)
            {
                return catalogo.Estacoes;
            }

            return Padrao();
        }
    }

    /// <summary>
    /// Rádio do jogo, estilo GTA: playlist embaralhada por estação, e a estação "continua ao vivo" em segundo plano (a posição avança com o
    /// tempo). Toca em 2D no barramento Radio, com um chiado de sintonia e a vinheta da estação ao trocar.
    /// R = próxima estação, Shift+R = anterior; depois da última vem "desligado".
    /// Liga-se sozinho pela diretoria de áudio. Tocar música de outro app (PlayerPrefs "musica_externa" = 1) desliga o rádio do jogo.
    /// </summary>
    public sealed class RadioPlayer : MonoBehaviour
    {
        public const string MusicaExternaKey = "musica_externa";

        private AudioSource _musica;
        private AudioSource _efeitos;
        private List<RadioEstacao> _estacoes;
        private int _atual = -1;
        private double _relogio;
        private int _faixaTocando = -1;
        private AudioClip _staticClip;
        private float _fade;

        public static RadioPlayer Instance { get; private set; }

        /// <summary>Avisa quando a estação muda (-1 = desligado). A UI do celular usa para a roda de estações.</summary>
        public event Action<int, RadioEstacao> EstacaoMudou;

        public IReadOnlyList<RadioEstacao> Estacoes => _estacoes;

        /// <summary>Índice da estação atual, -1 = desligado.</summary>
        public int IndiceAtual => _atual;

        /// <summary>Estação atual ou null se desligado.</summary>
        public RadioEstacao EstacaoAtual => _atual >= 0 && _atual < _estacoes.Count ? _estacoes[_atual] : null;

        public bool Tocando => _musica != null && _musica.isPlaying && _atual >= 0;

        /// <summary>A música de outro app está tocando: o rádio do jogo fica desligado.</summary>
        public static bool MusicaExterna
        {
            get => PlayerPrefs.GetInt(MusicaExternaKey, 0) != 0;
            set
            {
                PlayerPrefs.SetInt(MusicaExternaKey, value ? 1 : 0);

                if (value && Instance != null)
                {
                    Instance.Desligar();
                }
            }
        }

        public static RadioPlayer Garantir(GameObject host)
        {
            if (Instance != null)
            {
                return Instance;
            }

            return host.AddComponent<RadioPlayer>();
        }

        private void Awake()
        {
            Instance = this;
            _estacoes = RadioCatalogo.Carregar();
            _musica = gameObject.AddComponent<AudioSource>();
            _musica.playOnAwake = false;
            _musica.spatialBlend = 0f;
            _musica.loop = false;
            _efeitos = gameObject.AddComponent<AudioSource>();
            _efeitos.playOnAwake = false;
            _efeitos.spatialBlend = 0f;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void Proxima() => Sintonizar(RadioCore.Proxima(_atual, _estacoes.Count));

        public void Anterior() => Sintonizar(RadioCore.Anterior(_atual, _estacoes.Count));

        public void Desligar() => Sintonizar(-1);

        /// <summary>Vai direto para a estação (-1 desliga).</summary>
        public void Sintonizar(int indice)
        {
            if (MusicaExterna)
            {
                indice = -1;
            }

            if (indice < -1 || indice >= _estacoes.Count)
            {
                indice = -1;
            }

            if (indice == _atual)
            {
                return;
            }

            _atual = indice;
            _faixaTocando = -1;

            if (_atual < 0)
            {
                _musica.Stop();
            }
            else
            {
                PlayStatic();
                Acompanhar(true);
                _efeitos.PlayOneShot(_estacoes[_atual].VinhetaEfetiva(), 0.5f * AudioSettingsJogo.Efetivo(AudioBus.Radio));
            }

            EstacaoMudou?.Invoke(_atual, EstacaoAtual);
        }

        private void PlayStatic()
        {
            if (_staticClip == null)
            {
                _staticClip = SoundBank.Static;
            }

            _efeitos.PlayOneShot(_staticClip, 0.6f * AudioSettingsJogo.Efetivo(AudioBus.Radio));
        }

        private void Update()
        {
            _relogio += Time.unscaledDeltaTime;

            if (MusicaExterna && _atual >= 0)
            {
                Desligar();
            }

            HandleKeys();

            if (_atual >= 0)
            {
                Acompanhar(false);
            }

            float alvo = _atual >= 0 ? 0.5f * AudioSettingsJogo.Efetivo(AudioBus.Radio) : 0f;
            _fade = AudioMath.Smooth(_fade, alvo, Time.unscaledDeltaTime, 0.15f);
            _musica.volume = _fade;
        }

        private void HandleKeys()
        {
            Keyboard keyboard = Keyboard.current;

            if (keyboard == null || !keyboard[Key.R].wasPressedThisFrame)
            {
                return;
            }

            bool shift = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;

            if (shift)
            {
                Anterior();
            }
            else
            {
                Proxima();
            }
        }

        /// <summary>Mantém a faixa "ao vivo": se a faixa certa não está tocando (trocou de estação ou acabou), emenda no ponto certo.</summary>
        private void Acompanhar(bool force)
        {
            RadioEstacao estacao = _estacoes[_atual];
            IReadOnlyList<AudioClip> faixas = estacao.FaixasEfetivas();
            var duracoes = new float[faixas.Count];

            for (int i = 0; i < duracoes.Length; i++)
            {
                duracoes[i] = faixas[i] != null ? faixas[i].length : 0.01f;
            }

            uint semente = (uint)(_atual * 7919 + 13);
            RadioCore.Localizar(duracoes, semente, _relogio + _atual * 137.0, out int faixa, out float offset);

            if (faixa < 0)
            {
                return;
            }

            bool trocar = force || faixa != _faixaTocando || !_musica.isPlaying;

            if (!trocar)
            {
                return;
            }

            _faixaTocando = faixa;
            _musica.clip = faixas[faixa];
            _musica.loop = faixas.Count == 1;
            _musica.time = Mathf.Clamp(offset, 0f, Mathf.Max(0f, _musica.clip.length - 0.02f));
            _musica.Play();
        }
    }
}
