using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Entregador.Editor
{
    /// <summary>
    /// Leitor mínimo de JSON (o JsonUtility da Unity não lê dicionários de tags). Devolve Dictionary&lt;string, object&gt;,
    /// List&lt;object&gt;, double, string, bool ou null.
    /// </summary>
    public static class OsmJson
    {
        public static object Parse(string text)
        {
            int i = 0;
            object value = ReadValue(text, ref i);
            SkipSpace(text, ref i);

            if (i != text.Length)
            {
                throw new System.FormatException("JSON com texto sobrando na posição " + i);
            }

            return value;
        }

        private static void SkipSpace(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i]))
            {
                i++;
            }
        }

        private static object ReadValue(string s, ref int i)
        {
            SkipSpace(s, ref i);

            if (i >= s.Length)
            {
                throw new System.FormatException("JSON terminou antes da hora.");
            }

            char c = s[i];

            if (c == '{')
            {
                var map = new Dictionary<string, object>();
                i++;
                SkipSpace(s, ref i);

                if (s[i] == '}')
                {
                    i++;

                    return map;
                }

                while (true)
                {
                    SkipSpace(s, ref i);
                    string key = ReadString(s, ref i);
                    SkipSpace(s, ref i);
                    Expect(s, ref i, ':');
                    map[key] = ReadValue(s, ref i);
                    SkipSpace(s, ref i);

                    if (s[i] == ',')
                    {
                        i++;

                        continue;
                    }

                    Expect(s, ref i, '}');

                    return map;
                }
            }

            if (c == '[')
            {
                var list = new List<object>();
                i++;
                SkipSpace(s, ref i);

                if (s[i] == ']')
                {
                    i++;

                    return list;
                }

                while (true)
                {
                    list.Add(ReadValue(s, ref i));
                    SkipSpace(s, ref i);

                    if (s[i] == ',')
                    {
                        i++;

                        continue;
                    }

                    Expect(s, ref i, ']');

                    return list;
                }
            }

            if (c == '"')
            {
                return ReadString(s, ref i);
            }

            if (string.CompareOrdinal(s, i, "true", 0, 4) == 0)
            {
                i += 4;

                return true;
            }

            if (string.CompareOrdinal(s, i, "false", 0, 5) == 0)
            {
                i += 5;

                return false;
            }

            if (string.CompareOrdinal(s, i, "null", 0, 4) == 0)
            {
                i += 4;

                return null;
            }

            int start = i;

            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0)
            {
                i++;
            }

            return double.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
        }

        private static void Expect(string s, ref int i, char c)
        {
            if (s[i] != c)
            {
                throw new System.FormatException($"JSON: esperava '{c}' na posição {i}.");
            }

            i++;
        }

        private static string ReadString(string s, ref int i)
        {
            Expect(s, ref i, '"');
            var sb = new StringBuilder();

            while (s[i] != '"')
            {
                char c = s[i++];

                if (c != '\\')
                {
                    sb.Append(c);

                    continue;
                }

                char e = s[i++];

                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: sb.Append(e); break;
                }
            }

            i++;

            return sb.ToString();
        }
    }
}
