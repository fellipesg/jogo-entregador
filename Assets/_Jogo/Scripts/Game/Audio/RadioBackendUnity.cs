using System;
using System.Collections.Generic;
using System.IO;
using Entregador.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace Entregador.Game
{
    /// <summary>
    /// Backend do Unity para streams MP3 ao vivo (Icecast/Shoutcast), só com o que o próprio Unity oferece.
    /// O caminho óbvio (DownloadHandlerAudioClip com streamAudio) NÃO serve: ele exige saber o tamanho do arquivo e um stream infinito não
    /// tem ("Playback of audio clip not yet possible; ... size is still not known" e depois "Cannot create FMOD::Sound instance"; ver o teste
    /// de rede). Então: um DownloadHandlerScript recebe os bytes crus, o <see cref="Mp3Segmenter"/> recorta em trechos de ~2 s que terminam
    /// em fronteira de quadro, cada trecho vira um arquivo MP3 pequeno que o Unity decodifica normalmente (GetAudioClip), e os clipes são
    /// agendados emendados com PlayScheduled em duas fontes alternadas (sem falha: os quadros do preâmbulo são pulados). O tamanho do
    /// buffer inicial (~6 s) cobre a oscilação da rede. Metadados ICY ("agora tocando") são lidos pelo <see cref="IcyDemux"/>.
    /// Só MP3 (AAC e HLS ficam para o backend nativo do Android e o futuro do iOS).
    /// </summary>
    public sealed class UnityRadioBackend : IRadioStreamBackend
    {
        /// <summary>Duração de cada trecho recortado.</summary>
        public const double SegundosPorTrecho = 2.0;

        /// <summary>Áudio já decodificado e pronto antes de começar a tocar (e de voltar depois de uma falta).</summary>
        public const double ChumboInicial = 6.0;

        /// <summary>Sem bytes novos por tanto tempo, o stream é dado como caído.</summary>
        public const float TempoSemDados = 8f;

        /// <summary>Tempo máximo esperando o primeiro áudio.</summary>
        public const float TempoParaComecar = 25f;

        private sealed class Trecho
        {
            public string Caminho;
            public double Segundos;
            public int Preambulo;
            public AudioClip Clip;
            public double FimDsp;
        }

        /// <summary>Recebe os bytes do stream (chamado pelo Unity); a fila é lida no Tick.</summary>
        private sealed class Sink : DownloadHandlerScript
        {
            private readonly object _lock = new object();
            private readonly Queue<byte[]> _fila = new Queue<byte[]>();

            public long Total;

            public Sink() : base(new byte[32 * 1024])
            {
            }

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                if (data == null || dataLength <= 0)
                {
                    return false;
                }

                var copy = new byte[dataLength];
                Buffer.BlockCopy(data, 0, copy, 0, dataLength);

                lock (_lock)
                {
                    _fila.Enqueue(copy);
                    Total += dataLength;
                }

                return true;
            }

            protected override byte[] GetData() => null;

            protected override float GetProgress() => 0f;

            public bool TryDequeue(out byte[] chunk)
            {
                lock (_lock)
                {
                    if (_fila.Count > 0)
                    {
                        chunk = _fila.Dequeue();

                        return true;
                    }
                }

                chunk = null;

                return false;
            }
        }

        private static int _contador;

        private readonly GameObject _host;
        private readonly AudioSource[] _fontes = new AudioSource[2];
        private readonly double[] _fonteLivreEm = new double[2];
        private readonly Queue<Trecho> _paraCarregar = new Queue<Trecho>();
        private readonly Queue<Trecho> _prontos = new Queue<Trecho>();
        private readonly List<Trecho> _agendados = new List<Trecho>();
        private readonly List<byte> _audio = new List<byte>();
        private readonly string _prefixo;
        private UnityWebRequest _req;
        private Sink _sink;
        private Mp3Segmenter _seg;
        private IcyDemux _icy;
        private bool _icyLido;
        private UnityWebRequest _loadReq;
        private Trecho _carregando;
        private long _bytes;
        private float _semDados;
        private float _desdeInicio;
        private float _volume = 1f;
        private bool _iniciou;
        private double _fimAgendado;
        private double _segundosProntos;
        private int _proxFonte;
        private int _seq;

        public UnityRadioBackend(GameObject host)
        {
            _host = host;
            _prefixo = "radio_" + (++_contador) + "_";

            for (int i = 0; i < 2; i++)
            {
                _fontes[i] = host.AddComponent<AudioSource>();
                _fontes[i].playOnAwake = false;
                _fontes[i].spatialBlend = 0f;
                _fontes[i].loop = false;
                _fontes[i].volume = 0f;
            }
        }

        public string Nome => "Unity (MP3 em trechos)";

        public RadioCaps Caps => RadioCaps.UnityPadrao;

        public RadioStreamState Estado { get; private set; }

        public string Erro { get; private set; }

        /// <summary>Título da música (metadados ICY), se o servidor mandar.</summary>
        public string AgoraTocando => _icy != null && _icy.Titulo.Length > 0 ? _icy.Titulo : null;

        /// <summary>Bytes recebidos do stream.</summary>
        public long BytesRecebidos => _bytes;

        /// <summary>Taxa de amostragem e bitrate vistos nos quadros (0 se nenhum ainda).</summary>
        public int BitrateVisto => _seg != null ? _seg.Bitrate : 0;

        public void Tocar(string url)
        {
            Parar();
            Erro = null;
            _sink = new Sink();
            _req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET, _sink, null);
            _req.SetRequestHeader("User-Agent", RadioDirectory.UserAgent);
            _req.SetRequestHeader("Icy-MetaData", "1");
            _req.redirectLimit = 8;
            _req.SendWebRequest();
            _seg = new Mp3Segmenter(SegundosPorTrecho);
            _icy = null;
            _icyLido = false;
            _bytes = 0;
            _semDados = 0f;
            _desdeInicio = 0f;
            _iniciou = false;
            _segundosProntos = 0;
            Estado = RadioStreamState.Conectando;
        }

        public void Parar()
        {
            if (_req != null)
            {
                _req.Abort();
                _req.Dispose();
                _req = null;
            }

            _sink = null;

            if (_loadReq != null)
            {
                _loadReq.Dispose();
                _loadReq = null;
            }

            foreach (AudioSource f in _fontes)
            {
                if (f != null)
                {
                    f.Stop();
                    f.clip = null;
                }
            }

            if (_carregando != null)
            {
                Limpar(_carregando);
                _carregando = null;
            }

            while (_paraCarregar.Count > 0)
            {
                Limpar(_paraCarregar.Dequeue());
            }

            while (_prontos.Count > 0)
            {
                Limpar(_prontos.Dequeue());
            }

            foreach (Trecho t in _agendados)
            {
                Limpar(t);
            }

            _agendados.Clear();
            _audio.Clear();
            _iniciou = false;
            _fonteLivreEm[0] = _fonteLivreEm[1] = 0;
            _proxFonte = 0;
            Estado = RadioStreamState.Parado;
        }

        public void SetVolume(float volume)
        {
            _volume = volume;

            foreach (AudioSource f in _fontes)
            {
                if (f != null)
                {
                    f.volume = volume;
                }
            }
        }

        public void Tick(float dt)
        {
            if (_req == null || Estado == RadioStreamState.Erro)
            {
                return;
            }

            _desdeInicio += dt;
            long antes = _bytes;
            Bombear();

            if (_bytes > antes)
            {
                _semDados = 0f;
            }
            else
            {
                _semDados += dt;
            }

            if (_req.isDone)
            {
                // Um stream "infinito" não termina: terminar é queda (ou erro de HTTP).
                Falhar(_req.result == UnityWebRequest.Result.Success ? "o stream terminou" : _req.error);

                return;
            }

            if (_semDados > TempoSemDados)
            {
                Falhar("sem dados há " + TempoSemDados + " s");

                return;
            }

            Carregar();

            if (Estado == RadioStreamState.Erro)
            {
                return;
            }

            Agendar();

            if (!_iniciou && _desdeInicio > TempoParaComecar)
            {
                Falhar("o áudio não chegou em " + TempoParaComecar + " s");
            }
        }

        /// <summary>Passa os bytes recebidos pelo desmontador de ICY e pelo recortador de MP3; cada trecho pronto vira um arquivo.</summary>
        private void Bombear()
        {
            while (_sink != null && _sink.TryDequeue(out byte[] chunk))
            {
                _bytes += chunk.Length;

                if (!_icyLido)
                {
                    _icyLido = true;
                    int.TryParse(_req.GetResponseHeader("icy-metaint"), out int metaint);
                    _icy = new IcyDemux(metaint);
                }

                _audio.Clear();
                _icy.Processar(chunk, chunk.Length, _audio);

                if (_audio.Count == 0)
                {
                    continue;
                }

                byte[] bytes = _audio.ToArray();
                _seg.Push(bytes, 0, bytes.Length);

                while (_seg.TryTake(out byte[] segmento, out double segundos, out int preambulo))
                {
                    var trecho = new Trecho { Caminho = Path.Combine(Application.temporaryCachePath, _prefixo + (_seq++) + ".mp3"), Segundos = segundos, Preambulo = preambulo };

                    try
                    {
                        File.WriteAllBytes(trecho.Caminho, segmento);
                    }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                    {
                        Falhar("não consegui gravar o trecho: " + e.Message);

                        return;
                    }

                    _paraCarregar.Enqueue(trecho);
                }
            }
        }

        /// <summary>Decodifica um trecho por vez (o Unity lê o arquivo MP3 de tamanho conhecido).</summary>
        private void Carregar()
        {
            if (_loadReq != null)
            {
                if (!_loadReq.isDone)
                {
                    return;
                }

                Trecho trecho = _carregando;
                bool ok = _loadReq.result == UnityWebRequest.Result.Success;
                AudioClip clip = ok ? DownloadHandlerAudioClip.GetContent(_loadReq) : null;
                string erro = _loadReq.error;
                _loadReq.Dispose();
                _loadReq = null;
                _carregando = null;

                if (clip != null && clip.samples > 0)
                {
                    trecho.Clip = clip;
                    _prontos.Enqueue(trecho);
                    _segundosProntos += trecho.Segundos;
                    Limpar(trecho, false);
                }
                else
                {
                    Debug.LogWarning("[Rádio] trecho não decodificou (" + trecho.Caminho + "): " + erro);
                }
            }

            if (_loadReq == null && _paraCarregar.Count > 0)
            {
                _carregando = _paraCarregar.Dequeue();
                _loadReq = UnityWebRequestMultimedia.GetAudioClip("file://" + _carregando.Caminho, AudioType.MPEG);
                ((DownloadHandlerAudioClip)_loadReq.downloadHandler).streamAudio = false;
                _loadReq.SendWebRequest();
            }
        }

        /// <summary>Agenda os trechos prontos, emendados, nas duas fontes alternadas.</summary>
        private void Agendar()
        {
            double agora = AudioSettings.dspTime;

            for (int i = _agendados.Count - 1; i >= 0; i--)
            {
                if (_agendados[i].FimDsp + 0.1 < agora)
                {
                    Limpar(_agendados[i]);
                    _agendados.RemoveAt(i);
                }
            }

            if (_iniciou && _prontos.Count == 0 && agora > _fimAgendado)
            {
                // O buffer secou: volta a encher antes de continuar.
                _iniciou = false;
                _segundosProntos = 0;
            }

            if (!_iniciou && _segundosProntos >= ChumboInicial && _prontos.Count > 0)
            {
                _iniciou = true;
                _fimAgendado = agora + 0.25;
            }

            while (_iniciou && _prontos.Count > 0 && _fonteLivreEm[_proxFonte] <= agora)
            {
                Trecho t = _prontos.Dequeue();
                AudioSource fonte = _fontes[_proxFonte];
                double inicio = Math.Max(_fimAgendado, agora + 0.05);
                fonte.clip = t.Clip;
                fonte.timeSamples = Mathf.Min(t.Preambulo, Mathf.Max(0, t.Clip.samples - 1));
                fonte.volume = _volume;
                fonte.PlayScheduled(inicio);
                double duracao = (t.Clip.samples - fonte.timeSamples) / (double)t.Clip.frequency;
                _fimAgendado = inicio + duracao;
                t.FimDsp = _fimAgendado;
                _fonteLivreEm[_proxFonte] = _fimAgendado;
                _agendados.Add(t);
                _proxFonte ^= 1;
            }

            if (_iniciou)
            {
                Estado = agora < _fimAgendado ? RadioStreamState.Tocando : RadioStreamState.Buffering;
            }
            else
            {
                Estado = _bytes > 0 ? RadioStreamState.Buffering : RadioStreamState.Conectando;
            }
        }

        private static void Limpar(Trecho t, bool destruirClip = true)
        {
            if (destruirClip && t.Clip != null)
            {
                UnityEngine.Object.Destroy(t.Clip);
                t.Clip = null;
            }

            try
            {
                if (t.Caminho != null && File.Exists(t.Caminho))
                {
                    File.Delete(t.Caminho);
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // Arquivo temporário.
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

            foreach (AudioSource f in _fontes)
            {
                if (f != null)
                {
                    UnityEngine.Object.Destroy(f);
                }
            }
        }
    }
}
