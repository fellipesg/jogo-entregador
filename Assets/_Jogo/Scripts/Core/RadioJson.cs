using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Entregador.Core
{
    /// <summary>
    /// Leitor e escritor mínimo de JSON (objetos, listas, textos, números, true/false/null) para o rádio pela internet:
    /// o Core não depende do UnityEngine, então não dá para usar JsonUtility. Objetos viram Dictionary, listas viram List.
    /// </summary>
    public static class RadioJson
    {
        public static object Parse(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            int pos = 0;
            object value = ReadValue(json, ref pos);
            SkipSpace(json, ref pos);

            return value;
        }

        /// <summary>Lê um texto da chave (ou o padrão).</summary>
        public static string Str(Dictionary<string, object> obj, string key, string fallback = "")
        {
            if (obj != null && obj.TryGetValue(key, out object v) && v != null)
            {
                return v is string s ? s : Convert.ToString(v, CultureInfo.InvariantCulture);
            }

            return fallback;
        }

        /// <summary>Lê um inteiro; aceita número, "123" e true/false (1/0).</summary>
        public static int Int(Dictionary<string, object> obj, string key, int fallback = 0)
        {
            if (obj == null || !obj.TryGetValue(key, out object v) || v == null)
            {
                return fallback;
            }

            if (v is double d)
            {
                return (int)d;
            }

            if (v is bool b)
            {
                return b ? 1 : 0;
            }

            return int.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : fallback;
        }

        public static bool Bool(Dictionary<string, object> obj, string key, bool fallback = false)
        {
            if (obj != null && obj.TryGetValue(key, out object v) && v != null)
            {
                if (v is bool b)
                {
                    return b;
                }

                if (v is double d)
                {
                    return d != 0;
                }

                return string.Equals(Convert.ToString(v, CultureInfo.InvariantCulture), "true", StringComparison.OrdinalIgnoreCase);
            }

            return fallback;
        }

        public static string Quote(string s)
        {
            var sb = new StringBuilder(s == null ? 2 : s.Length + 2);
            sb.Append('"');

            foreach (char c in s ?? string.Empty)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }

                        break;
                }
            }

            sb.Append('"');

            return sb.ToString();
        }

        private static void SkipSpace(string s, ref int pos)
        {
            while (pos < s.Length && (s[pos] == ' ' || s[pos] == '\n' || s[pos] == '\r' || s[pos] == '\t' || s[pos] == '﻿'))
            {
                pos++;
            }
        }

        private static object ReadValue(string s, ref int pos)
        {
            SkipSpace(s, ref pos);

            if (pos >= s.Length)
            {
                throw new FormatException("JSON terminou antes do esperado.");
            }

            char c = s[pos];

            if (c == '{')
            {
                return ReadObject(s, ref pos);
            }

            if (c == '[')
            {
                return ReadArray(s, ref pos);
            }

            if (c == '"')
            {
                return ReadString(s, ref pos);
            }

            if (string.CompareOrdinal(s, pos, "true", 0, 4) == 0)
            {
                pos += 4;

                return true;
            }

            if (string.CompareOrdinal(s, pos, "false", 0, 5) == 0)
            {
                pos += 5;

                return false;
            }

            if (string.CompareOrdinal(s, pos, "null", 0, 4) == 0)
            {
                pos += 4;

                return null;
            }

            int start = pos;

            while (pos < s.Length && "+-0123456789.eE".IndexOf(s[pos]) >= 0)
            {
                pos++;
            }

            if (pos == start || !double.TryParse(s.Substring(start, pos - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
            {
                throw new FormatException("JSON inválido na posição " + start);
            }

            return number;
        }

        private static Dictionary<string, object> ReadObject(string s, ref int pos)
        {
            var obj = new Dictionary<string, object>();
            pos++;
            SkipSpace(s, ref pos);

            if (pos < s.Length && s[pos] == '}')
            {
                pos++;

                return obj;
            }

            while (true)
            {
                SkipSpace(s, ref pos);
                string key = ReadString(s, ref pos);
                SkipSpace(s, ref pos);

                if (pos >= s.Length || s[pos] != ':')
                {
                    throw new FormatException("JSON: faltou ':' na posição " + pos);
                }

                pos++;
                obj[key] = ReadValue(s, ref pos);
                SkipSpace(s, ref pos);

                if (pos < s.Length && s[pos] == ',')
                {
                    pos++;

                    continue;
                }

                if (pos < s.Length && s[pos] == '}')
                {
                    pos++;

                    return obj;
                }

                throw new FormatException("JSON: objeto mal fechado na posição " + pos);
            }
        }

        private static List<object> ReadArray(string s, ref int pos)
        {
            var list = new List<object>();
            pos++;
            SkipSpace(s, ref pos);

            if (pos < s.Length && s[pos] == ']')
            {
                pos++;

                return list;
            }

            while (true)
            {
                list.Add(ReadValue(s, ref pos));
                SkipSpace(s, ref pos);

                if (pos < s.Length && s[pos] == ',')
                {
                    pos++;

                    continue;
                }

                if (pos < s.Length && s[pos] == ']')
                {
                    pos++;

                    return list;
                }

                throw new FormatException("JSON: lista mal fechada na posição " + pos);
            }
        }

        private static string ReadString(string s, ref int pos)
        {
            if (pos >= s.Length || s[pos] != '"')
            {
                throw new FormatException("JSON: esperava texto na posição " + pos);
            }

            pos++;
            var sb = new StringBuilder();

            while (pos < s.Length)
            {
                char c = s[pos++];

                if (c == '"')
                {
                    return sb.ToString();
                }

                if (c != '\\')
                {
                    sb.Append(c);

                    continue;
                }

                if (pos >= s.Length)
                {
                    break;
                }

                char e = s[pos++];

                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (pos + 4 > s.Length || !int.TryParse(s.Substring(pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                        {
                            throw new FormatException("JSON: escape \\u inválido");
                        }

                        sb.Append((char)code);
                        pos += 4;
                        break;
                    default: sb.Append(e); break;
                }
            }

            throw new FormatException("JSON: texto sem fim");
        }
    }
}
