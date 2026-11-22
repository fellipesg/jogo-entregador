using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Entregador.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;

namespace Entregador.Game
{
    /// <summary>
    /// Uma estação de rádio: nome, gênero, cor e as faixas. Sem faixas, usa o laço sintetizado do estilo (provisório, "Rádio do jogo").
    /// Com <see cref="Info"/>, é uma rádio real da internet (stream ao vivo).
    /// </summary>
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

        /// <summary>Dados da rádio da internet; null nas estações sintetizadas do jogo.</summary>
        [NonSerialized] public RadioStationInfo Info;

        [NonSerialized] private List<AudioClip> _geradas;
        [NonSerialized] private AudioClip _vinhetaGerada;

        public bool EhStream => Info != null;

        /// <summary>Pública (Cultura, USP, EBC, Câmara...), ao vivo (comercial) ou "Rádio do jogo" (sintetizada).</summary>
        public string Origem => Info == null ? "Rádio do jogo" : (Info.Publica ? "Rádio pública" : "Ao vivo");

        /// <summary>Chave estável (favoritos).</summary>
        public string Chave => Info != null ? Info.Chave : "jogo:" + Nome;

        public static RadioEstacao DeInfo(RadioStationInfo info)
        {
            return new RadioEstacao
            {
                Nome = info.Nome,
                Genero = info.Genero,
                Cor = CorDoNome(info.Nome),
                Info = info,
            };
        }

        /// <summary>Cor estável por nome (círculo de inicial quando falta o ícone).</summary>
        public static Color CorDoNome(string nome)
        {
            uint h = 2166136261u;

            foreach (char c in nome ?? string.Empty)
            {
                h = (h ^ c) * 16777619u;
            }

            return Color.HSVToRGB((h % 360u) / 360f, 0.55f, 0.78f);
        }

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

    /// <summary>Catálogo das estações sintetizadas (asset em Assets/_Jogo/Audio/Radio/Resources). Sem o asset, vale o padrão do código.</summary>
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
    /// Rádio do jogo, estilo GTA. Estações: as rádios reais de São Paulo pela internet (lista curada + Radio Browser, ver RadioDirectory) e,
    /// no fim, as sintetizadas "Rádio do jogo" (playlist embaralhada que "continua ao vivo" em segundo plano), que ficam de reserva sem rede.
    /// Toca em 2D no barramento Radio, com um chiado de sintonia entre estações.
    /// R = próxima, Shift+R = anterior (a roda tem os favoritos, ou as primeiras da lista, mais as do jogo); depois da última vem "desligado".
    /// Tocar música de outro app (PlayerPrefs "musica_externa" = 1) ou desligar "Rádio pela internet" tira as rádios da internet.
    /// </summary>
    public sealed class RadioPlayer : MonoBehaviour
    {
        public const string MusicaExternaKey = "musica_externa";
        public const string InternetKey = "radio_internet";
        public const string FavoritosKey = "radio_favoritos";

        /// <summary>Quantas estações da lista entram na roda do R quando não há favoritos.</summary>
        public const int TamanhoRoda = 8;

        /// <summary>Máximo de estações do Radio Browser somadas às curadas.</summary>
        public const int MaxApi = 60;

        private AudioSource _musica;
        private AudioSource _efeitos;
        private RadioStream _stream;
        private List<RadioEstacao> _estacoes;
        private List<RadioEstacao> _jogo;
        private readonly Dictionary<string, RadioEstacao> _porChave = new Dictionary<string, RadioEstacao>();
        private List<RadioStationInfo> _infos = new List<RadioStationInfo>();
        private List<int> _roleta = new List<int>();
        private HashSet<string> _favoritos;
        private int _atual = -1;
        private double _relogio;
        private int _faixaTocando = -1;
        private AudioClip _staticClip;
        private float _fade;
        private int _falhasSeguidas;
        private bool _carregando;
        private string _statusLista = string.Empty;

        public static RadioPlayer Instance { get; private set; }

        /// <summary>Para os testes: não busca nada na internet (nem lê o cache) ao nascer.</summary>
        public static bool NaoCarregarInternet { get; set; }

        /// <summary>Avisa quando a estação muda (-1 = desligado). A UI do celular usa para a roda de estações.</summary>
        public event Action<int, RadioEstacao> EstacaoMudou;

        /// <summary>A estação da internet caiu e não voltou: o jogo mostra "Rádio fora do ar" e passa para a próxima.</summary>
        public event Action<RadioEstacao> ForaDoAr;

        /// <summary>A lista de estações, os favoritos ou o estado mudaram (a tela do celular recarrega).</summary>
        public event Action ListaMudou;

        public IReadOnlyList<RadioEstacao> Estacoes => _estacoes;

        /// <summary>Índice da estação atual, -1 = desligado.</summary>
        public int IndiceAtual => _atual;

        /// <summary>Estação atual ou null se desligado.</summary>
        public RadioEstacao EstacaoAtual => _atual >= 0 && _atual < _estacoes.Count ? _estacoes[_atual] : null;

        /// <summary>Tocando de fato (stream com áudio saindo, ou faixa do jogo).</summary>
        public bool Tocando
        {
            get
            {
                if (_atual < 0)
                {
                    return false;
                }

                return EstacaoAtual.EhStream ? _stream.Estado == RadioStreamState.Tocando : _musica != null && _musica.isPlaying;
            }
        }

        /// <summary>Uma rádio da internet está abrindo a conexão ou enchendo o buffer ("Sintonizando…").</summary>
        public bool Sintonizando
        {
            get
            {
                if (_atual < 0 || !EstacaoAtual.EhStream)
                {
                    return false;
                }

                RadioStreamState s = _stream.Estado;

                return s == RadioStreamState.Conectando || s == RadioStreamState.Buffering || s == RadioStreamState.Erro;
            }
        }

        /// <summary>Texto de status para a interface: "Sintonizando…", "Rádio do jogo", o título da música ou a origem.</summary>
        public string Status
        {
            get
            {
                RadioEstacao e = EstacaoAtual;

                if (e == null)
                {
                    return "Desligado";
                }

                if (Sintonizando)
                {
                    return "Sintonizando…";
                }

                if (!e.EhStream)
                {
                    return "Rádio do jogo";
                }

                string musica = _stream.AgoraTocando;

                return string.IsNullOrEmpty(musica) ? e.Genero : musica;
            }
        }

        public RadioStream Stream => _stream;

        /// <summary>Texto sobre a busca da lista da internet (para a tela do celular): vazio, "Atualizando…", "Offline: lista guardada" etc.</summary>
        public string StatusLista => _statusLista;

        public bool Carregando => _carregando;

        /// <summary>Índices (em <see cref="Estacoes"/>) da roda do R.</summary>
        public IReadOnlyList<int> Roleta => _roleta;

        /// <summary>A opção "Rádio pela internet" como o jogador deixou (padrão ligado), mesmo que a música externa a esteja calando.</summary>
        public static bool InternetPreferida => PlayerPrefs.GetInt(InternetKey, 1) != 0;

        /// <summary>"Rádio pela internet" nas opções (padrão ligado). Gasta dados móveis. Com música de outro app ligada, vale desligado.</summary>
        public static bool InternetAtiva
        {
            get => InternetPreferida && !MusicaExterna;
            set
            {
                PlayerPrefs.SetInt(InternetKey, value ? 1 : 0);

                if (Instance != null)
                {
                    Instance.AplicarInternet();
                }
            }
        }

        /// <summary>A música de outro app está tocando: o rádio do jogo fica desligado.</summary>
        public static bool MusicaExterna
        {
            get => PlayerPrefs.GetInt(MusicaExternaKey, 0) != 0;
            set
            {
                PlayerPrefs.SetInt(MusicaExternaKey, value ? 1 : 0);

                if (Instance != null)
                {
                    if (value)
                    {
                        Instance.Desligar();
                    }

                    Instance.AplicarInternet();
                }
            }
        }

        /// <summary>Linha dos créditos: as rádios da lista curada.</summary>
        public static string TextoCreditos()
        {
            var asset = Resources.Load<TextAsset>("RadioCurada");
            var names = new List<string>();

            if (asset != null)
            {
                try
                {
                    foreach (RadioStationInfo s in RadioDirectory.ParseCurada(asset.text))
                    {
                        names.Add(NomeCurto(s.Nome));
                    }
                }
                catch (FormatException)
                {
                    // Sem a lista, só o aviso geral.
                }
            }

            return "Rádios ao vivo · " + (names.Count > 0 ? string.Join(", ", names) + " " : string.Empty) +
                "(streams públicos das emissoras; lista via radio-browser.info)";
        }

        /// <summary>Nome para os créditos: sem "Rádio" na frente e sem a frequência ("Cultura FM 103.3" vira "Cultura FM").</summary>
        public static string NomeCurto(string nome)
        {
            string s = nome.StartsWith("Rádio ", StringComparison.Ordinal) ? nome.Substring(6) : nome;
            var parts = new List<string>();

            foreach (string w in s.Split(' '))
            {
                bool freq = w.Length > 0 && char.IsDigit(w[0]) && (w.Contains(".") || w.Contains(","));

                if (!freq)
                {
                    parts.Add(w);
                }
            }

            string joined = string.Join(" ", parts);

            return joined.EndsWith(" AM", StringComparison.Ordinal) && joined.StartsWith("Cultura", StringComparison.Ordinal) ? joined.Substring(0, joined.Length - 3) : joined;
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
            _jogo = RadioCatalogo.Carregar();
            _estacoes = new List<RadioEstacao>(_jogo);
            _favoritos = RadioDirectory.DecodificarFavoritos(PlayerPrefs.GetString(FavoritosKey, string.Empty));
            _musica = gameObject.AddComponent<AudioSource>();
            _musica.playOnAwake = false;
            _musica.spatialBlend = 0f;
            _musica.loop = false;
            _efeitos = gameObject.AddComponent<AudioSource>();
            _efeitos.playOnAwake = false;
            _efeitos.spatialBlend = 0f;
            _stream = gameObject.AddComponent<RadioStream>();
            _stream.ForaDoAr += OnStreamForaDoAr;
            _stream.Comecou += OnStreamComecou;
            RecalcularRoleta();
        }

        private void Start()
        {
            AplicarInternet();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        // ---------- Lista de estações da internet ----------

        /// <summary>Liga ou desliga as rádios da internet conforme a opção: ligando, carrega a lista; desligando, tira da lista e volta ao jogo.</summary>
        public void AplicarInternet()
        {
            if (InternetAtiva)
            {
                if (_infos.Count == 0 && !_carregando && !NaoCarregarInternet)
                {
                    StartCoroutine(CarregarInternet());
                }
                else if (_infos.Count > 0)
                {
                    Reconstruir();
                }
            }
            else
            {
                _infos = new List<RadioStationInfo>();
                _statusLista = string.Empty;
                Reconstruir();
            }
        }

        /// <summary>Volta ao estado de partida: desligado, sem favoritas, sem lista da internet. O RadioPlayer mora no AudioDirector, que sobrevive às trocas de cena; os testes usam.</summary>
        public void ReiniciarParaTestes()
        {
            StopAllCoroutines();
            _carregando = false;
            _falhasSeguidas = 0;
            Desligar();
            _favoritos.Clear();
            PlayerPrefs.DeleteKey(FavoritosKey);
            _infos = new List<RadioStationInfo>();
            _statusLista = string.Empty;
            Reconstruir();
        }

        /// <summary>Define a lista da internet na mão (testes e a carga normal usam).</summary>
        public void DefinirEstacoesInternet(IList<RadioStationInfo> infos)
        {
            _infos = new List<RadioStationInfo>(infos);
            Reconstruir();
        }

        private void Reconstruir()
        {
            RadioEstacao atual = EstacaoAtual;
            var novo = new List<RadioEstacao>();

            if (InternetAtiva)
            {
                foreach (RadioStationInfo info in _infos)
                {
                    if (!_porChave.TryGetValue(info.Chave, out RadioEstacao e))
                    {
                        e = RadioEstacao.DeInfo(info);
                        _porChave[info.Chave] = e;
                    }
                    else
                    {
                        e.Info = info;
                    }

                    novo.Add(e);
                }
            }

            novo.AddRange(_jogo);

            // Uma estação da internet que acabou de sair da lista (opção desligada) para de tocar.
            if (atual != null && !novo.Contains(atual))
            {
                _estacoes = novo;
                _atual = -1;
                _stream.Parar();
                _musica.Stop();
                RecalcularRoleta();
                EstacaoMudou?.Invoke(-1, null);
                ListaMudou?.Invoke();

                return;
            }

            _estacoes = novo;
            _atual = atual != null ? novo.IndexOf(atual) : -1;
            RecalcularRoleta();
            ListaMudou?.Invoke();
        }

        private static string CaminhoCache => Path.Combine(Application.persistentDataPath, "radio_cache.json");

        private IEnumerator CarregarInternet()
        {
            _carregando = true;
            _statusLista = "Atualizando…";
            RadioCaps caps = _stream.Caps;

            // 1. A lista curada do repositório (sempre disponível, mesmo sem rede).
            List<RadioStationInfo> curadas = new List<RadioStationInfo>();
            TextAsset asset = Resources.Load<TextAsset>("RadioCurada");

            if (asset != null)
            {
                try
                {
                    curadas = RadioDirectory.ParseCurada(asset.text);
                }
                catch (FormatException e)
                {
                    Debug.LogWarning("[Rádio] lista curada inválida: " + e.Message);
                }
            }

            // 2. O cache do Radio Browser (mesmo vencido serve de reserva sem rede).
            List<RadioStationInfo> daApi = new List<RadioStationInfo>();
            DateTime buscadoEm = DateTime.MinValue;
            bool cacheOk = false;

            try
            {
                if (File.Exists(CaminhoCache))
                {
                    cacheOk = RadioDirectory.LerCache(File.ReadAllText(CaminhoCache), out daApi, out buscadoEm);
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning("[Rádio] cache ilegível: " + e.Message);
            }

            Publicar(curadas, daApi, caps);
            bool fresco = cacheOk && RadioDirectory.CacheFresco(buscadoEm, DateTime.UtcNow);

            if (fresco)
            {
                _statusLista = "Lista do dia";
                _carregando = false;
                ListaMudou?.Invoke();

                yield break;
            }

            // 3. Busca no Radio Browser (um espelho; se falhar, tenta o próximo). Só a lista: nenhum dado do jogador vai junto.
            bool buscou = false;

            foreach (string host in RadioDirectory.Hosts)
            {
                if (!InternetAtiva)
                {
                    break;
                }

                using (UnityWebRequest req = UnityWebRequest.Get(RadioDirectory.UrlBusca(host)))
                {
                    req.SetRequestHeader("User-Agent", RadioDirectory.UserAgent);
                    req.timeout = 15;
                    yield return req.SendWebRequest();

                    if (req.result != UnityWebRequest.Result.Success)
                    {
                        continue;
                    }

                    try
                    {
                        List<RadioStationInfo> todas = RadioDirectory.ParseApi(req.downloadHandler.text);
                        daApi = RadioDirectory.Filtrar(todas, RadioCaps.Nativo);
                        buscou = daApi.Count > 0;
                    }
                    catch (FormatException e)
                    {
                        Debug.LogWarning("[Rádio] resposta inválida de " + host + ": " + e.Message);
                    }
                }

                if (!buscou)
                {
                    continue;
                }

                // Atualiza as URLs e o "no ar" das curadas pelo uuid.
                var uuids = new List<string>();

                foreach (RadioStationInfo c in curadas)
                {
                    if (!string.IsNullOrEmpty(c.Uuid))
                    {
                        uuids.Add(c.Uuid);
                    }
                }

                using (UnityWebRequest req = UnityWebRequest.Get(RadioDirectory.UrlPorUuid(host, uuids)))
                {
                    req.SetRequestHeader("User-Agent", RadioDirectory.UserAgent);
                    req.timeout = 15;
                    yield return req.SendWebRequest();

                    if (req.result == UnityWebRequest.Result.Success)
                    {
                        try
                        {
                            RadioDirectory.AtualizarCuradas(curadas, RadioDirectory.ParseApi(req.downloadHandler.text), caps);
                        }
                        catch (FormatException e)
                        {
                            Debug.LogWarning("[Rádio] byuuid inválido: " + e.Message);
                        }
                    }
                }

                try
                {
                    Directory.CreateDirectory(Application.persistentDataPath);
                    File.WriteAllText(CaminhoCache, RadioDirectory.ParaCache(daApi, DateTime.UtcNow));
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    Debug.LogWarning("[Rádio] não consegui guardar o cache: " + e.Message);
                }

                break;
            }

            _statusLista = buscou ? "Lista atualizada" : (cacheOk ? "Sem rede: lista guardada" : "Sem rede: só as rádios curadas");
            Publicar(curadas, daApi, caps);
            _carregando = false;
            ListaMudou?.Invoke();
        }

        private void Publicar(List<RadioStationInfo> curadas, List<RadioStationInfo> daApi, RadioCaps caps)
        {
            if (!InternetAtiva)
            {
                return;
            }

            List<RadioStationInfo> c = RadioDirectory.Filtrar(curadas, caps);
            List<RadioStationInfo> a = RadioDirectory.Filtrar(daApi, caps);
            _infos = RadioDirectory.Juntar(c, a, MaxApi);
            Reconstruir();
        }

        /// <summary>Conta um clique no Radio Browser, uma vez por vez que a estação é de fato ouvida (etiqueta da API). Não leva nenhum dado pessoal.</summary>
        private IEnumerator EnviarClique(string uuid)
        {
            using (UnityWebRequest req = UnityWebRequest.Get(RadioDirectory.UrlClique(RadioDirectory.Hosts[0], uuid)))
            {
                req.SetRequestHeader("User-Agent", RadioDirectory.UserAgent);
                req.timeout = 10;
                yield return req.SendWebRequest();
            }
        }

        // ---------- Favoritos e roda ----------

        public bool EhFavorito(RadioEstacao e) => e != null && _favoritos.Contains(e.Chave);

        public bool AlternarFavorito(RadioEstacao e)
        {
            if (e == null)
            {
                return false;
            }

            bool agora = RadioDirectory.AlternarFavorito(_favoritos, e.Chave);
            PlayerPrefs.SetString(FavoritosKey, RadioDirectory.CodificarFavoritos(_favoritos));
            RecalcularRoleta();
            ListaMudou?.Invoke();

            return agora;
        }

        private void RecalcularRoleta()
        {
            int atual = _atual;
            RadioEstacao atualEstacao = atual >= 0 && atual < _estacoes.Count ? _estacoes[atual] : null;
            _roleta = new List<int>();
            var favoritas = new List<int>();
            int nNet = 0;

            for (int i = 0; i < _estacoes.Count; i++)
            {
                if (_estacoes[i].EhStream)
                {
                    nNet++;

                    if (_favoritos.Contains(_estacoes[i].Chave))
                    {
                        favoritas.Add(i);
                    }
                }
            }

            if (favoritas.Count > 0)
            {
                _roleta.AddRange(favoritas);
            }
            else
            {
                for (int i = 0; i < nNet && i < TamanhoRoda; i++)
                {
                    _roleta.Add(i);
                }
            }

            for (int i = nNet; i < _estacoes.Count; i++)
            {
                _roleta.Add(i);
            }

            // A que está tocando sempre cabe na roda, para o R seguir dela.
            if (atualEstacao != null)
            {
                int idx = _estacoes.IndexOf(atualEstacao);

                if (idx >= 0 && !_roleta.Contains(idx))
                {
                    _roleta.Insert(0, idx);
                }
            }
        }

        // ---------- Sintonia ----------

        public void Proxima()
        {
            int pos = _roleta.IndexOf(_atual);
            int next = RadioCore.Proxima(_atual < 0 ? -1 : pos, _roleta.Count);
            Sintonizar(next < 0 ? -1 : _roleta[next]);
        }

        public void Anterior()
        {
            int pos = _roleta.IndexOf(_atual);
            int prev = RadioCore.Anterior(_atual < 0 ? -1 : (pos < 0 ? 0 : pos), _roleta.Count);
            Sintonizar(prev < 0 ? -1 : _roleta[prev]);
        }

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
            _stream.Parar();
            _musica.Stop();

            if (_atual >= 0)
            {
                PlayStatic();
                RadioEstacao e = _estacoes[_atual];

                if (e.EhStream)
                {
                    _stream.Tocar(e.Info.Url);
                }
                else
                {
                    Acompanhar(true);
                    _efeitos.PlayOneShot(e.VinhetaEfetiva(), 0.5f * AudioSettingsJogo.Efetivo(AudioBus.Radio));
                }
            }
            else
            {
                _falhasSeguidas = 0;
            }

            EstacaoMudou?.Invoke(_atual, EstacaoAtual);
        }

        /// <summary>Sintoniza pela estação (a tela do celular usa).</summary>
        public void Sintonizar(RadioEstacao estacao)
        {
            Sintonizar(estacao == null ? -1 : _estacoes.IndexOf(estacao));
        }

        private void OnStreamComecou()
        {
            _falhasSeguidas = 0;
            RadioEstacao e = EstacaoAtual;

            if (e != null && e.EhStream && !string.IsNullOrEmpty(e.Info.Uuid) && !NaoCarregarInternet)
            {
                StartCoroutine(EnviarClique(e.Info.Uuid));
            }
        }

        private void OnStreamForaDoAr()
        {
            RadioEstacao e = EstacaoAtual;
            _falhasSeguidas++;

            // Passa para a próxima; se a roda inteira estiver fora do ar (sem rede), desliga em vez de ficar rodando.
            if (_falhasSeguidas >= Mathf.Max(2, _roleta.Count))
            {
                Desligar();
            }
            else
            {
                Proxima();
            }

            // O aviso vem por último, para não ser coberto pelo nome da nova estação.
            ForaDoAr?.Invoke(e);
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

            bool stream = _atual >= 0 && EstacaoAtual.EhStream;

            if (_atual >= 0 && !stream)
            {
                Acompanhar(false);
            }

            float bus = AudioSettingsJogo.Efetivo(AudioBus.Radio);
            float alvo = _atual >= 0 ? (stream ? 0.7f : 0.5f) * bus : 0f;
            _fade = AudioMath.Smooth(_fade, alvo, Time.unscaledDeltaTime, 0.15f);

            if (stream)
            {
                _musica.volume = 0f;
                _stream.SetVolume(_fade);
            }
            else
            {
                _musica.volume = _fade;
            }
        }

        private void HandleKeys()
        {
            Keyboard keyboard = Keyboard.current;

            if (keyboard == null || !keyboard[Key.R].wasPressedThisFrame || GameUI.DigitandoTexto)
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
