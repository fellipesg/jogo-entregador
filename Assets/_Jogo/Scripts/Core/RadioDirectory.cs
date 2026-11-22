using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Entregador.Core
{
    /// <summary>Uma estação de rádio da internet (da lista curada ou do Radio Browser).</summary>
    public sealed class RadioStationInfo
    {
        public string Uuid = string.Empty;
        public string Nome = string.Empty;
        public string Url = string.Empty;
        public string Homepage = string.Empty;
        public string Favicon = string.Empty;
        public string Tags = string.Empty;

        /// <summary>MP3, AAC, AAC+, OGG, HLS, FLV, UNKNOWN (como o Radio Browser escreve), em maiúsculas.</summary>
        public string Codec = string.Empty;

        public int Bitrate;
        public bool Hls;
        public bool Publica;
        public string Genero = RadioDirectory.GeneroOutras;
        public string Cidade = string.Empty;
        public int Votos;

        /// <summary>Veio da lista curada do repositório (vem antes das outras).</summary>
        public bool Curada;

        /// <summary>O Radio Browser diz que o stream respondeu na última verificação.</summary>
        public bool Ok = true;

        /// <summary>Chave estável para favoritos: o uuid do Radio Browser ou, sem ele, a URL.</summary>
        public string Chave => !string.IsNullOrEmpty(Uuid) ? Uuid : "url:" + Url;

        public RadioStationInfo Copia() => (RadioStationInfo)MemberwiseClone();
    }

    /// <summary>O que um backend de reprodução consegue tocar (Unity: MP3 e OGG; Android nativo: quase tudo; iOS: ainda nada).</summary>
    public sealed class RadioCaps
    {
        public bool Mp3;
        public bool Ogg;
        public bool Aac;
        public bool Hls;

        /// <summary>Aceita codec "UNKNOWN" (o player nativo descobre sozinho).</summary>
        public bool Desconhecido;

        public static RadioCaps Nenhum => new RadioCaps();

        public static RadioCaps UnityPadrao => new RadioCaps { Mp3 = true, Ogg = true };

        public static RadioCaps Nativo => new RadioCaps { Mp3 = true, Ogg = true, Aac = true, Hls = true, Desconhecido = true };

        public bool Pode(RadioStationInfo s)
        {
            if (s == null || string.IsNullOrEmpty(s.Url))
            {
                return false;
            }

            return Pode(s.Codec, s.Hls || RadioDirectory.EhHls(s.Url));
        }

        public bool Pode(string codec, bool hls)
        {
            if (hls)
            {
                return Hls;
            }

            switch ((codec ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "MP3":
                    return Mp3;
                case "OGG":
                case "VORBIS":
                    return Ogg;
                case "AAC":
                case "AAC+":
                    return Aac;
                case "HLS":
                    return Hls;
                case "FLV":
                    return false;
                default:
                    return Desconhecido;
            }
        }
    }

    /// <summary>
    /// Lógica pura do rádio pela internet: ler a resposta do Radio Browser e a lista curada, filtrar por codec e por "respondendo",
    /// classificar por gênero, juntar as listas, buscar, favoritos, cache com validade e a política de reconexão.
    /// </summary>
    public static class RadioDirectory
    {
        public const string GeneroNoticias = "Notícias";
        public const string GeneroSertanejo = "Sertanejo";
        public const string GeneroPop = "Pop";
        public const string GeneroRock = "Rock";
        public const string GeneroFunkRap = "Funk e rap";
        public const string GeneroSamba = "Samba e pagode";
        public const string GeneroGospel = "Gospel";
        public const string GeneroMpb = "MPB e clássica";
        public const string GeneroOutras = "Outras";

        /// <summary>Gêneros na ordem dos chips da interface.</summary>
        public static readonly string[] Generos =
        {
            GeneroNoticias, GeneroPop, GeneroSertanejo, GeneroRock, GeneroFunkRap, GeneroSamba, GeneroGospel, GeneroMpb, GeneroOutras,
        };

        /// <summary>Espelhos do Radio Browser, na ordem de tentativa.</summary>
        public static readonly string[] Hosts =
        {
            "https://de1.api.radio-browser.info", "https://de2.api.radio-browser.info", "https://fi1.api.radio-browser.info",
        };

        public const string UserAgent = "JogoEntregador/0.1";

        /// <summary>Validade da lista guardada: 1 dia.</summary>
        public static readonly TimeSpan CacheValidade = TimeSpan.FromDays(1);

        public static string UrlBusca(string host, int limite = 200)
        {
            return host + "/json/stations/search?state=" + Uri.EscapeDataString("São Paulo") + "&countrycode=BR&order=votes&reverse=true&hidebroken=true&limit=" +
                limite.ToString(CultureInfo.InvariantCulture);
        }

        public static string UrlPorUuid(string host, IEnumerable<string> uuids)
        {
            return host + "/json/stations/byuuid?uuids=" + string.Join(",", uuids);
        }

        /// <summary>A contagem de cliques só vai quando o jogador de fato ouve a estação (etiqueta do Radio Browser).</summary>
        public static string UrlClique(string host, string uuid)
        {
            return host + "/json/url/" + uuid;
        }

        public static bool EhHls(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return false;
            }

            int q = url.IndexOf('?');
            string path = q >= 0 ? url.Substring(0, q) : url;

            return path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase);
        }

        // ---------- Leitura ----------

        /// <summary>Resposta de /json/stations/search ou /byuuid: lista de estações (sem filtrar).</summary>
        public static List<RadioStationInfo> ParseApi(string json)
        {
            var list = new List<RadioStationInfo>();

            if (!(RadioJson.Parse(json) is List<object> items))
            {
                return list;
            }

            foreach (object item in items)
            {
                if (!(item is Dictionary<string, object> o))
                {
                    continue;
                }

                string url = RadioJson.Str(o, "url_resolved");

                if (string.IsNullOrWhiteSpace(url))
                {
                    url = RadioJson.Str(o, "url");
                }

                var s = new RadioStationInfo
                {
                    Uuid = RadioJson.Str(o, "stationuuid"),
                    Nome = Limpar(RadioJson.Str(o, "name")),
                    Url = url.Trim(),
                    Homepage = RadioJson.Str(o, "homepage").Trim(),
                    Favicon = RadioJson.Str(o, "favicon").Trim(),
                    Tags = RadioJson.Str(o, "tags").Trim(),
                    Codec = RadioJson.Str(o, "codec").Trim().ToUpperInvariant(),
                    Bitrate = RadioJson.Int(o, "bitrate"),
                    Hls = RadioJson.Int(o, "hls") == 1,
                    Votos = RadioJson.Int(o, "votes"),
                    Cidade = RadioJson.Str(o, "state").Trim(),
                    Ok = RadioJson.Int(o, "lastcheckok") == 1,
                };

                s.Hls = s.Hls || EhHls(s.Url);
                s.Genero = Classificar(s.Tags, s.Nome);
                list.Add(s);
            }

            return list;
        }

        /// <summary>Lista curada do repositório (e o formato do cache): { "estacoes": [ { uuid, nome, url, ... } ] }.</summary>
        public static List<RadioStationInfo> ParseCurada(string json)
        {
            var list = new List<RadioStationInfo>();

            if (!(RadioJson.Parse(json) is Dictionary<string, object> root) || !root.TryGetValue("estacoes", out object arr) || !(arr is List<object> items))
            {
                return list;
            }

            foreach (object item in items)
            {
                if (!(item is Dictionary<string, object> o))
                {
                    continue;
                }

                var s = new RadioStationInfo
                {
                    Uuid = RadioJson.Str(o, "uuid"),
                    Nome = Limpar(RadioJson.Str(o, "nome")),
                    Url = RadioJson.Str(o, "url").Trim(),
                    Homepage = RadioJson.Str(o, "homepage"),
                    Favicon = RadioJson.Str(o, "favicon"),
                    Tags = RadioJson.Str(o, "tags"),
                    Codec = RadioJson.Str(o, "codec").ToUpperInvariant(),
                    Bitrate = RadioJson.Int(o, "bitrate"),
                    Hls = RadioJson.Bool(o, "hls") || EhHls(RadioJson.Str(o, "url")),
                    Publica = RadioJson.Bool(o, "publica"),
                    Genero = RadioJson.Str(o, "genero", GeneroOutras),
                    Cidade = RadioJson.Str(o, "cidade"),
                    Votos = RadioJson.Int(o, "votos"),
                    Curada = RadioJson.Bool(o, "curada", true),
                    Ok = true,
                };

                if (!string.IsNullOrEmpty(s.Url))
                {
                    list.Add(s);
                }
            }

            return list;
        }

        private static string Limpar(string name)
        {
            return (name ?? string.Empty).Replace('\n', ' ').Replace('\r', ' ').Trim();
        }

        // ---------- Filtro, classificação e junção ----------

        /// <summary>Só estações que respondem, com URL http(s) e codec que o backend toca; sem repetidas (mesmo nome ou URL); mais votadas primeiro.</summary>
        public static List<RadioStationInfo> Filtrar(IEnumerable<RadioStationInfo> source, RadioCaps caps)
        {
            var kept = new List<RadioStationInfo>();

            foreach (RadioStationInfo s in source)
            {
                if (s == null || !s.Ok || !UrlValida(s.Url) || string.IsNullOrWhiteSpace(s.Nome) || !caps.Pode(s))
                {
                    continue;
                }

                kept.Add(s);
            }

            // Estável: mais votadas primeiro, a ordem original desempata.
            var indexed = new List<KeyValuePair<int, RadioStationInfo>>();

            for (int i = 0; i < kept.Count; i++)
            {
                indexed.Add(new KeyValuePair<int, RadioStationInfo>(i, kept[i]));
            }

            indexed.Sort((a, b) =>
            {
                int c = b.Value.Votos.CompareTo(a.Value.Votos);

                return c != 0 ? c : a.Key.CompareTo(b.Key);
            });

            var seenName = new HashSet<string>();
            var seenUrl = new HashSet<string>();
            var result = new List<RadioStationInfo>();

            foreach (KeyValuePair<int, RadioStationInfo> pair in indexed)
            {
                RadioStationInfo s = pair.Value;

                if (!seenName.Add(NormalizarNome(s.Nome)) || !seenUrl.Add(s.Url))
                {
                    continue;
                }

                result.Add(s);
            }

            return result;
        }

        public static bool UrlValida(string url)
        {
            return !string.IsNullOrEmpty(url) && (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Gênero (um dos <see cref="Generos"/>) a partir das tags do Radio Browser e do nome.</summary>
        public static string Classificar(string tags, string nome)
        {
            string t = " " + Sem(tags) + " " + Sem(nome) + " ";

            if (Tem(t, "news", "noticia", "jornal", "journalism", "talk", "bandnews", "cbn", "debates"))
            {
                return GeneroNoticias;
            }

            if (Tem(t, "gospel", "evangel", "catolic", "catholic", "christian", "louvor", "biblia"))
            {
                return GeneroGospel;
            }

            if (Tem(t, "funk", "rap", "hip hop", "hiphop", "trap", "soul", "r&b", "rnb"))
            {
                return GeneroFunkRap;
            }

            if (Tem(t, "samba", "pagode", "roda de samba"))
            {
                return GeneroSamba;
            }

            if (Tem(t, "sertanej", "country", "caipira", "moda de viola", "modao"))
            {
                return GeneroSertanejo;
            }

            if (Tem(t, "rock", "metal", "punk", "grunge"))
            {
                return GeneroRock;
            }

            if (Tem(t, "mpb", "classic", "jazz", "bossa", "instrumental", "erudit", "lounge", "adult contemporary", "adulta"))
            {
                return GeneroMpb;
            }

            if (Tem(t, "pop", "hits", "dance", "top 40", "chr", "flashback", "anos 80", "anos 90"))
            {
                return GeneroPop;
            }

            return GeneroOutras;
        }

        private static bool Tem(string haystack, params string[] needles)
        {
            foreach (string n in needles)
            {
                if (haystack.IndexOf(n, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Minúsculas e sem acento (para buscar e comparar).</summary>
        public static string Sem(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            string d = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(d.Length);

            foreach (char c in d)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(c);
                }
            }

            return sb.ToString().ToLowerInvariant();
        }

        private static string NormalizarNome(string nome)
        {
            var sb = new StringBuilder();

            foreach (char c in Sem(nome))
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        /// <summary>Curadas primeiro (públicas na frente, depois a ordem da lista), depois as da API, sem repetir (uuid, nome ou URL), até <paramref name="maxApi"/> da API.</summary>
        public static List<RadioStationInfo> Juntar(IList<RadioStationInfo> curadas, IList<RadioStationInfo> api, int maxApi)
        {
            var result = new List<RadioStationInfo>();
            var uuids = new HashSet<string>();
            var names = new HashSet<string>();
            var urls = new HashSet<string>();

            void Add(RadioStationInfo s)
            {
                result.Add(s);

                if (!string.IsNullOrEmpty(s.Uuid))
                {
                    uuids.Add(s.Uuid);
                }

                names.Add(NormalizarNome(s.Nome));
                urls.Add(s.Url);
            }

            if (curadas != null)
            {
                foreach (RadioStationInfo s in curadas)
                {
                    if (s.Publica)
                    {
                        s.Curada = true;
                        Add(s);
                    }
                }

                foreach (RadioStationInfo s in curadas)
                {
                    if (!s.Publica)
                    {
                        s.Curada = true;
                        Add(s);
                    }
                }
            }

            int taken = 0;

            if (api != null)
            {
                foreach (RadioStationInfo s in api)
                {
                    if (taken >= maxApi)
                    {
                        break;
                    }

                    if ((!string.IsNullOrEmpty(s.Uuid) && uuids.Contains(s.Uuid)) || names.Contains(NormalizarNome(s.Nome)) || urls.Contains(s.Url))
                    {
                        continue;
                    }

                    Add(s);
                    taken++;
                }
            }

            return result;
        }

        /// <summary>
        /// Atualiza as curadas com a resposta de /byuuid: some quem o Radio Browser marca como fora do ar; troca a URL só se a nova for tocável
        /// pelo backend (evita trocar o MP3 que o Unity toca por um AAC que ele não toca). Completa o ícone se faltava.
        /// </summary>
        public static void AtualizarCuradas(IList<RadioStationInfo> curadas, IList<RadioStationInfo> porUuid, RadioCaps caps)
        {
            var map = new Dictionary<string, RadioStationInfo>();

            foreach (RadioStationInfo s in porUuid)
            {
                if (!string.IsNullOrEmpty(s.Uuid))
                {
                    map[s.Uuid] = s;
                }
            }

            foreach (RadioStationInfo c in curadas)
            {
                if (string.IsNullOrEmpty(c.Uuid) || !map.TryGetValue(c.Uuid, out RadioStationInfo fresh))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(c.Favicon) && !string.IsNullOrEmpty(fresh.Favicon) && !fresh.Favicon.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
                {
                    c.Favicon = fresh.Favicon;
                }

                if (!fresh.Ok)
                {
                    // Só vale se a URL curada é a mesma que o Radio Browser testou; uma URL que escolhi (ex.: variante MP3) segue valendo.
                    if (c.Url == fresh.Url)
                    {
                        c.Ok = false;
                    }

                    continue;
                }

                if (c.Url != fresh.Url && UrlValida(fresh.Url) && caps.Pode(fresh) && !caps.Pode(c))
                {
                    c.Url = fresh.Url;
                    c.Codec = fresh.Codec;
                    c.Bitrate = fresh.Bitrate;
                    c.Hls = fresh.Hls;
                }
            }
        }

        // ---------- Busca ----------

        /// <summary>Filtra por gênero (null/"" = todos) e por texto (nome, tags, gênero, cidade; sem acento e sem maiúsculas).</summary>
        public static List<RadioStationInfo> Buscar(IEnumerable<RadioStationInfo> source, string texto, string genero)
        {
            var result = new List<RadioStationInfo>();
            string q = Sem(texto).Trim();

            foreach (RadioStationInfo s in source)
            {
                if (!string.IsNullOrEmpty(genero) && s.Genero != genero)
                {
                    continue;
                }

                if (q.Length > 0 && Sem(s.Nome + " " + s.Tags + " " + s.Genero + " " + s.Cidade).IndexOf(q, StringComparison.Ordinal) < 0)
                {
                    continue;
                }

                result.Add(s);
            }

            return result;
        }

        // ---------- Favoritos ----------

        public static HashSet<string> DecodificarFavoritos(string raw)
        {
            var set = new HashSet<string>();

            if (string.IsNullOrEmpty(raw))
            {
                return set;
            }

            foreach (string part in raw.Split('\n'))
            {
                if (part.Length > 0)
                {
                    set.Add(part);
                }
            }

            return set;
        }

        public static string CodificarFavoritos(IEnumerable<string> keys)
        {
            return string.Join("\n", keys);
        }

        /// <summary>Liga/desliga o favorito; devolve true se ficou favorito.</summary>
        public static bool AlternarFavorito(HashSet<string> favoritos, string chave)
        {
            if (favoritos.Remove(chave))
            {
                return false;
            }

            favoritos.Add(chave);

            return true;
        }

        // ---------- Cache ----------

        /// <summary>Guarda a lista com a hora da busca (UTC, ISO 8601).</summary>
        public static string ParaCache(IEnumerable<RadioStationInfo> list, DateTime buscadoEmUtc)
        {
            var sb = new StringBuilder();
            sb.Append("{\"buscadoEm\":").Append(RadioJson.Quote(buscadoEmUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)));
            sb.Append(",\"estacoes\":[");
            bool first = true;

            foreach (RadioStationInfo s in list)
            {
                if (!first)
                {
                    sb.Append(',');
                }

                first = false;
                sb.Append("{\"uuid\":").Append(RadioJson.Quote(s.Uuid));
                sb.Append(",\"nome\":").Append(RadioJson.Quote(s.Nome));
                sb.Append(",\"url\":").Append(RadioJson.Quote(s.Url));
                sb.Append(",\"homepage\":").Append(RadioJson.Quote(s.Homepage));
                sb.Append(",\"favicon\":").Append(RadioJson.Quote(s.Favicon));
                sb.Append(",\"tags\":").Append(RadioJson.Quote(s.Tags));
                sb.Append(",\"codec\":").Append(RadioJson.Quote(s.Codec));
                sb.Append(",\"bitrate\":").Append(s.Bitrate.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"hls\":").Append(s.Hls ? "true" : "false");
                sb.Append(",\"publica\":").Append(s.Publica ? "true" : "false");
                sb.Append(",\"genero\":").Append(RadioJson.Quote(s.Genero));
                sb.Append(",\"cidade\":").Append(RadioJson.Quote(s.Cidade));
                sb.Append(",\"votos\":").Append(s.Votos.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"curada\":").Append(s.Curada ? "true" : "false");
                sb.Append('}');
            }

            sb.Append("]}");

            return sb.ToString();
        }

        /// <summary>Lê o cache; false se estiver vazio ou estragado.</summary>
        public static bool LerCache(string json, out List<RadioStationInfo> list, out DateTime buscadoEmUtc)
        {
            list = new List<RadioStationInfo>();
            buscadoEmUtc = DateTime.MinValue;

            try
            {
                if (!(RadioJson.Parse(json) is Dictionary<string, object> root))
                {
                    return false;
                }

                if (!DateTime.TryParse(RadioJson.Str(root, "buscadoEm"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out buscadoEmUtc))
                {
                    return false;
                }

                list = ParseCurada(json);

                return list.Count > 0;
            }
            catch (FormatException)
            {
                list = new List<RadioStationInfo>();

                return false;
            }
        }

        public static bool CacheFresco(DateTime buscadoEmUtc, DateTime agoraUtc)
        {
            TimeSpan age = agoraUtc - buscadoEmUtc;

            return age >= TimeSpan.Zero && age < CacheValidade;
        }

        // ---------- Reconexão ----------

        /// <summary>Espera antes da tentativa n (0, 1, 2...) de reconectar: 1, 2, 4, 8 s, no máximo 15 s.</summary>
        public static float EsperaReconexao(int tentativa)
        {
            float s = (float)Math.Pow(2, Math.Max(0, tentativa));

            return Math.Min(15f, s);
        }

        /// <summary>Depois de tantas quedas seguidas a estação é dada como fora do ar (o jogo avisa e pula para a próxima).</summary>
        public const int MaxReconexoes = 2;
    }
}
