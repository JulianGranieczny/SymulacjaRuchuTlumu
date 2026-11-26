using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HospitalSim.Import
{
    /// <summary>
    /// Minimalny parser JSON używany przez importer glTF.
    /// </summary>
    // Odwzorowanie typów: obiekt → Dictionary<string, object>, tablica → List<object>,
    // liczba → double, tekst → string, true/false → bool, null → null.
    public static class MiniJson
    {
        /// <summary>
        /// Parsuje tekst JSON i zwraca wartość najwyższego poziomu.
        /// </summary>
        /// <exception cref="FormatException">Gdy tekst nie jest poprawnym dokumentem JSON.</exception>
        public static object Parse(string text)
        {
            int i = 0;
            object v = ParseValue(text, ref i);
            return v;
        }

        static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        static object ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("json: nieoczekiwany koniec pliku");
            char c = s[i];
            if (c == '{') return ParseObject(s, ref i);
            if (c == '[') return ParseArray(s, ref i);
            if (c == '"') return ParseString(s, ref i);
            if (c == 't' && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (c == 'f' && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (c == 'n' && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            return ParseNumber(s, ref i);
        }

        static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var d = new Dictionary<string, object>();
            i++;
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return d; }
            while (true)
            {
                SkipWs(s, ref i);
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("json: brak dwukropka");
                i++;
                d[key] = ParseValue(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("json: niezamknięty obiekt");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return d; }
                throw new FormatException("json: błąd w obiekcie");
            }
        }

        static List<object> ParseArray(string s, ref int i)
        {
            var l = new List<object>();
            i++;
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return l; }
            while (true)
            {
                l.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("json: niezamknięta tablica");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return l; }
                throw new FormatException("json: błąd w tablicy");
            }
        }

        static string ParseString(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("json: oczekiwano tekstu");
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
            throw new FormatException("json: niezamknięty tekst");
        }

        /// <summary>
        /// Odczytuje liczbę jako ciąg znaków ze zbioru "+-0123456789.eE" i konwertuje go na double.
        /// </summary>
        static object ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (i == start) throw new FormatException("json: nieznany znak '" + s[i] + "'");
            return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        // Metody pomocnicze bezpiecznie odczytujące wartości z drzewa. Gdy węzeł nie jest obiektem
        // albo klucz nie istnieje, zwracają null lub wartość domyślną zamiast zgłaszać wyjątek.
        public static Dictionary<string, object> Obj(object o, string key)
        {
            var d = o as Dictionary<string, object>;
            object v;
            if (d != null && d.TryGetValue(key, out v)) return v as Dictionary<string, object>;
            return null;
        }

        public static List<object> Arr(object o, string key)
        {
            var d = o as Dictionary<string, object>;
            object v;
            if (d != null && d.TryGetValue(key, out v)) return v as List<object>;
            return null;
        }

        public static double Num(object o, string key, double def)
        {
            var d = o as Dictionary<string, object>;
            object v;
            if (d != null && d.TryGetValue(key, out v) && v is double) return (double)v;
            return def;
        }

        /// <summary>
        /// Odczytuje liczbę i obcina ją do typu int.
        /// </summary>
        public static int Int(object o, string key, int def)
        {
            return (int)Num(o, key, def);
        }

        public static string Str(object o, string key)
        {
            var d = o as Dictionary<string, object>;
            object v;
            if (d != null && d.TryGetValue(key, out v)) return v as string;
            return null;
        }

        public static bool Has(object o, string key)
        {
            var d = o as Dictionary<string, object>;
            return d != null && d.ContainsKey(key);
        }
    }
}
