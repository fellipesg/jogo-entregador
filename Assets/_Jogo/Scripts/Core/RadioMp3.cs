using System;
using System.Collections.Generic;
using System.Text;

namespace Entregador.Core
{
    /// <summary>Cabeçalho de um quadro MPEG de áudio (MP3 e irmãos).</summary>
    public struct Mp3Header
    {
        public int Comprimento;
        public int Amostras;
        public int TaxaAmostragem;
        public int Bitrate;

        /// <summary>Duração do quadro em segundos.</summary>
        public double Segundos => TaxaAmostragem > 0 ? (double)Amostras / TaxaAmostragem : 0.0;

        private static readonly int[] BitrateV1L1 = { 0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448 };
        private static readonly int[] BitrateV1L2 = { 0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384 };
        private static readonly int[] BitrateV1L3 = { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320 };
        private static readonly int[] BitrateV2L1 = { 0, 32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256 };
        private static readonly int[] BitrateV2L23 = { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160 };
        private static readonly int[] RateV1 = { 44100, 48000, 32000 };
        private static readonly int[] RateV2 = { 22050, 24000, 16000 };
        private static readonly int[] RateV25 = { 11025, 12000, 8000 };

        /// <summary>Lê o cabeçalho em <paramref name="i"/>; false se não for um cabeçalho MPEG de áudio válido.</summary>
        public static bool TryParse(byte[] b, int i, int count, out Mp3Header h)
        {
            h = default;

            if (i < 0 || i + 4 > count || b[i] != 0xFF || (b[i + 1] & 0xE0) != 0xE0)
            {
                return false;
            }

            int versionBits = (b[i + 1] >> 3) & 3; // 0 = 2.5, 1 = reservado, 2 = MPEG2, 3 = MPEG1
            int layerBits = (b[i + 1] >> 1) & 3; // 1 = III, 2 = II, 3 = I
            int bitrateIndex = (b[i + 2] >> 4) & 15;
            int rateIndex = (b[i + 2] >> 2) & 3;
            int padding = (b[i + 2] >> 1) & 1;

            if (versionBits == 1 || layerBits == 0 || bitrateIndex == 0 || bitrateIndex == 15 || rateIndex == 3)
            {
                return false;
            }

            bool v1 = versionBits == 3;
            int layer = 4 - layerBits;
            int[] table = layer == 1 ? (v1 ? BitrateV1L1 : BitrateV2L1) : (layer == 2 ? (v1 ? BitrateV1L2 : BitrateV2L23) : (v1 ? BitrateV1L3 : BitrateV2L23));
            int kbps = table[bitrateIndex];
            int rate = v1 ? RateV1[rateIndex] : (versionBits == 2 ? RateV2[rateIndex] : RateV25[rateIndex]);
            int length;
            int samples;

            if (layer == 1)
            {
                length = (12 * kbps * 1000 / rate + padding) * 4;
                samples = 384;
            }
            else
            {
                int factor = layer == 3 && !v1 ? 72 : 144;
                length = factor * kbps * 1000 / rate + padding;
                samples = layer == 3 && !v1 ? 576 : 1152;
            }

            h = new Mp3Header { Comprimento = length, Amostras = samples, TaxaAmostragem = rate, Bitrate = kbps };

            return length > 4;
        }
    }

    /// <summary>
    /// Recorta um stream de MP3 ao vivo (sem fim, sem tamanho) em trechos curtos que terminam em fronteira de quadro, cada um um arquivo MP3
    /// completo. Serve ao backend do Unity: o Unity só decodifica áudio de tamanho conhecido, então o stream infinito vira uma fila de trechos.
    /// Ressincroniza sozinho depois de lixo (tags ID3, anúncios, perda de bytes): só aceita um quadro quando o seguinte também tem cabeçalho válido.
    /// </summary>
    public sealed class Mp3Segmenter
    {
        private byte[] _buf = new byte[64 * 1024];
        private int _len;
        private readonly double _alvoSegundos;
        private readonly int _quadrosPreambulo;
        private byte[] _cauda = Array.Empty<byte>();
        private int _caudaAmostras;

        /// <summary>Quadros de silêncio no começo de cada trecho (veja o construtor).</summary>
        public const int QuadrosSilenciosos = 2;

        /// <summary>
        /// alvoSegundos: duração de cada trecho. quadrosPreambulo: quantos quadros do fim do trecho anterior vão repetidos no começo do
        /// próximo. O MP3 tem uma "reserva de bits": o primeiro quadro de um trecho recortado costuma apontar até 511 bytes para trás, para
        /// dados que não estão no arquivo, e o FMOD então recusa o arquivo inteiro ("Error loading file"). Por isso cada trecho começa com
        /// <see cref="QuadrosSilenciosos"/> quadros de silêncio válidos (mesmo cabeçalho do stream, corpo zerado) seguidos dos quadros
        /// repetidos: o lixo da reserva faltando cai neles. O chamador pula <c>preambuloAmostras</c> ao tocar, então a emenda fica sem falha.
        /// Com quadrosPreambulo = 0 o trecho sai puro, sem silêncio nem repetição.
        /// </summary>
        public Mp3Segmenter(double alvoSegundos, int quadrosPreambulo = 3)
        {
            _alvoSegundos = alvoSegundos;
            _quadrosPreambulo = quadrosPreambulo;
        }

        /// <summary>Bytes parados no buffer, ainda sem virar trecho.</summary>
        public int Pendentes => _len;

        /// <summary>Taxa de amostragem e bitrate do último quadro visto (0 se nenhum).</summary>
        public int TaxaAmostragem { get; private set; }

        public int Bitrate { get; private set; }

        public void Push(byte[] data, int offset, int count)
        {
            if (count <= 0)
            {
                return;
            }

            if (_len + count > _buf.Length)
            {
                Array.Resize(ref _buf, Math.Max(_buf.Length * 2, _len + count));
            }

            Buffer.BlockCopy(data, offset, _buf, _len, count);
            _len += count;
        }

        public void Reset()
        {
            _len = 0;
            TaxaAmostragem = 0;
            Bitrate = 0;
            _cauda = Array.Empty<byte>();
            _caudaAmostras = 0;
        }

        /// <summary>Pega o próximo trecho pronto (quadros inteiros somando pelo menos o alvo de segundos); false se ainda não há dados.</summary>
        public bool TryTake(out byte[] segmento, out double segundos)
        {
            return TryTake(out segmento, out segundos, out _);
        }

        /// <summary>
        /// Como <see cref="TryTake(out byte[], out double)"/>, mas o trecho começa com quadros repetidos do anterior:
        /// <paramref name="preambuloAmostras"/> é quanto pular (em amostras por canal) ao tocar. <paramref name="segundos"/> não conta o preâmbulo.
        /// </summary>
        public bool TryTake(out byte[] segmento, out double segundos, out int preambuloAmostras)
        {
            segmento = null;
            segundos = 0;
            preambuloAmostras = 0;
            int pos = 0;

            // Pula uma tag ID3v2 no começo, se houver (10 bytes de cabeçalho + tamanho "synchsafe").
            if (_len >= 10 && _buf[0] == 'I' && _buf[1] == 'D' && _buf[2] == '3')
            {
                int size = ((_buf[6] & 0x7F) << 21) | ((_buf[7] & 0x7F) << 14) | ((_buf[8] & 0x7F) << 7) | (_buf[9] & 0x7F);
                int total = 10 + size;

                if (_len < total)
                {
                    return false;
                }

                pos = total;
            }

            int start = -1;
            double acc = 0;
            int p = pos;
            bool perdeuSincronia = false;
            var inicios = new int[Math.Max(1, _quadrosPreambulo)];
            var amostrasDoQuadro = new int[inicios.Length];
            int nQuadros = 0;

            while (p + 4 <= _len)
            {
                if (!Mp3Header.TryParse(_buf, p, _len, out Mp3Header h))
                {
                    if (start >= 0)
                    {
                        perdeuSincronia = true;

                        break;
                    }

                    p++;

                    continue;
                }

                int next = p + h.Comprimento;

                if (next > _len)
                {
                    break; // quadro incompleto: espera mais bytes
                }

                if (start < 0)
                {
                    if (next + 4 > _len)
                    {
                        break; // ainda não dá para confirmar o primeiro quadro
                    }

                    if (!Mp3Header.TryParse(_buf, next, _len, out _))
                    {
                        p++; // falso cabeçalho (bytes de áudio que parecem um)

                        continue;
                    }

                    start = p;
                }

                TaxaAmostragem = h.TaxaAmostragem;
                Bitrate = h.Bitrate;
                acc += h.Segundos;

                // Guarda os começos dos últimos quadros (anel) para o preâmbulo do próximo trecho.
                int slot = nQuadros % inicios.Length;
                inicios[slot] = p;
                amostrasDoQuadro[slot] = h.Amostras;
                nQuadros++;
                p = next;

                if (acc >= _alvoSegundos)
                {
                    break;
                }
            }

            if (start < 0)
            {
                // Sem quadro confirmado: o que veio antes de p é lixo e pode ir embora.
                if (p > 0)
                {
                    Consume(p);
                }

                return false;
            }

            if (acc < _alvoSegundos && !perdeuSincronia)
            {
                return false; // falta chegar mais áudio
            }

            int silencios = _quadrosPreambulo > 0 ? QuadrosSilenciosos : 0;
            Mp3Header.TryParse(_buf, start, _len, out Mp3Header primeiro0);
            int tamSilencio = primeiro0.Comprimento;
            segmento = new byte[silencios * tamSilencio + _cauda.Length + p - start];

            for (int k = 0; k < silencios; k++)
            {
                // Quadro de silêncio: cabeçalho do stream (sem CRC), lado e corpo zerados.
                Buffer.BlockCopy(_buf, start, segmento, k * tamSilencio, 4);
                segmento[k * tamSilencio + 1] |= 1;
            }

            Buffer.BlockCopy(_cauda, 0, segmento, silencios * tamSilencio, _cauda.Length);
            Buffer.BlockCopy(_buf, start, segmento, silencios * tamSilencio + _cauda.Length, p - start);
            segundos = acc;
            preambuloAmostras = silencios * primeiro0.Amostras + _caudaAmostras;

            // Preâmbulo do próximo: os últimos quadros deste trecho.
            int usar = _quadrosPreambulo <= 0 ? 0 : Math.Min(nQuadros, inicios.Length);
            int primeiro = nQuadros - usar;
            int de = usar == 0 ? p : inicios[primeiro % inicios.Length];
            _cauda = new byte[p - de];
            Buffer.BlockCopy(_buf, de, _cauda, 0, _cauda.Length);
            _caudaAmostras = 0;

            for (int k = primeiro; k < nQuadros; k++)
            {
                _caudaAmostras += amostrasDoQuadro[k % inicios.Length];
            }

            Consume(p);

            return true;
        }

        private void Consume(int n)
        {
            _len -= n;

            if (_len > 0)
            {
                Buffer.BlockCopy(_buf, n, _buf, 0, _len);
            }
        }
    }

    /// <summary>
    /// Separa os metadados ICY (Shoutcast/Icecast) do áudio. Com o cabeçalho "Icy-MetaData: 1" o servidor intercala, a cada
    /// <c>icy-metaint</c> bytes de áudio, um byte de tamanho (x16) e o bloco "StreamTitle='...';". Aqui o áudio sai limpo e o título vira
    /// <see cref="Titulo"/> ("agora tocando").
    /// </summary>
    public sealed class IcyDemux
    {
        private readonly int _metaint;
        private int _ate;
        private int _faltaMeta = -1;
        private readonly List<byte> _meta = new List<byte>();

        /// <summary>Último título lido (vazio até chegar o primeiro).</summary>
        public string Titulo { get; private set; } = string.Empty;

        /// <summary>metaint = 0 deixa tudo passar (servidor sem metadados).</summary>
        public IcyDemux(int metaint)
        {
            _metaint = metaint;
            _ate = metaint;
        }

        /// <summary>Processa um pedaço do stream; o áudio puro (sem metadados) é acrescentado em <paramref name="audio"/>.</summary>
        public void Processar(byte[] data, int count, List<byte> audio)
        {
            if (_metaint <= 0)
            {
                for (int k = 0; k < count; k++)
                {
                    audio.Add(data[k]);
                }

                return;
            }

            int i = 0;

            while (i < count)
            {
                if (_faltaMeta >= 0)
                {
                    // Dentro do bloco de metadados.
                    if (_faltaMeta == 0)
                    {
                        FecharMeta();
                        continue;
                    }

                    int take = Math.Min(_faltaMeta, count - i);

                    for (int k = 0; k < take; k++)
                    {
                        _meta.Add(data[i + k]);
                    }

                    i += take;
                    _faltaMeta -= take;

                    if (_faltaMeta == 0)
                    {
                        FecharMeta();
                    }

                    continue;
                }

                if (_ate == 0)
                {
                    // Byte de tamanho dos metadados.
                    _faltaMeta = data[i] * 16;
                    i++;
                    _meta.Clear();

                    if (_faltaMeta == 0)
                    {
                        FecharMeta();
                    }

                    continue;
                }

                int n = Math.Min(_ate, count - i);

                for (int k = 0; k < n; k++)
                {
                    audio.Add(data[i + k]);
                }

                i += n;
                _ate -= n;
            }
        }

        private void FecharMeta()
        {
            if (_meta.Count > 0)
            {
                string text = Encoding.UTF8.GetString(_meta.ToArray()).TrimEnd('\0');
                string title = LerTitulo(text);

                if (title != null)
                {
                    Titulo = title;
                }
            }

            _meta.Clear();
            _faltaMeta = -1;
            _ate = _metaint;
        }

        /// <summary>Extrai StreamTitle='...'; devolve null se não houver.</summary>
        public static string LerTitulo(string meta)
        {
            const string key = "StreamTitle='";
            int i = meta.IndexOf(key, StringComparison.Ordinal);

            if (i < 0)
            {
                return null;
            }

            i += key.Length;
            int end = meta.IndexOf("';", i, StringComparison.Ordinal);

            if (end < 0)
            {
                end = meta.LastIndexOf('\'');

                if (end < i)
                {
                    end = meta.Length;
                }
            }

            return meta.Substring(i, end - i).Trim();
        }
    }
}
