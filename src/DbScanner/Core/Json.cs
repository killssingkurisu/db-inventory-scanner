using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DbScanner.Core
{
    /// <summary>
    /// A small JSON reader and writer, so the scanner needs nothing beyond .NET Framework.
    /// Objects read as Dictionary&lt;string, object&gt;, arrays as List&lt;object&gt;, numbers as double.
    /// </summary>
    public static class Json
    {
        public static object Parse(string text)
        {
            var p = new Parser(text);
            p.SkipWs();
            object v = p.Value();
            p.SkipWs();
            if (p.Pos != text.Length) throw new FormatException("Unexpected text after JSON at " + p.Pos);
            return v;
        }

        sealed class Parser
        {
            readonly string s;
            public int Pos;
            public Parser(string text) { s = text; }

            public void SkipWs()
            {
                while (Pos < s.Length && (s[Pos] == ' ' || s[Pos] == '\t' || s[Pos] == '\n' || s[Pos] == '\r' || s[Pos] == '﻿')) Pos++;
            }

            char Peek() { return Pos < s.Length ? s[Pos] : '\0'; }

            void Expect(char c)
            {
                if (Peek() != c) throw new FormatException("Expected '" + c + "' at " + Pos);
                Pos++;
            }

            public object Value()
            {
                SkipWs();
                char c = Peek();
                if (c == '{') return Obj();
                if (c == '[') return Arr();
                if (c == '"') return Str();
                if (c == 't') { Word("true"); return true; }
                if (c == 'f') { Word("false"); return false; }
                if (c == 'n') { Word("null"); return null; }
                return Num();
            }

            void Word(string w)
            {
                if (string.CompareOrdinal(s, Pos, w, 0, w.Length) != 0) throw new FormatException("Bad literal at " + Pos);
                Pos += w.Length;
            }

            Dictionary<string, object> Obj()
            {
                var d = new Dictionary<string, object>(StringComparer.Ordinal);
                Expect('{');
                SkipWs();
                if (Peek() == '}') { Pos++; return d; }
                while (true)
                {
                    SkipWs();
                    string k = Str();
                    SkipWs();
                    Expect(':');
                    d[k] = Value();
                    SkipWs();
                    if (Peek() == ',') { Pos++; continue; }
                    Expect('}');
                    return d;
                }
            }

            List<object> Arr()
            {
                var a = new List<object>();
                Expect('[');
                SkipWs();
                if (Peek() == ']') { Pos++; return a; }
                while (true)
                {
                    a.Add(Value());
                    SkipWs();
                    if (Peek() == ',') { Pos++; continue; }
                    Expect(']');
                    return a;
                }
            }

            string Str()
            {
                Expect('"');
                var sb = new StringBuilder();
                while (true)
                {
                    if (Pos >= s.Length) throw new FormatException("Unterminated string");
                    char c = s[Pos++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    char e = s[Pos++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            sb.Append((char)int.Parse(s.Substring(Pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            Pos += 4;
                            break;
                        default: throw new FormatException("Bad escape at " + Pos);
                    }
                }
            }

            object Num()
            {
                int start = Pos;
                while (Pos < s.Length && "+-0123456789.eE".IndexOf(s[Pos]) >= 0) Pos++;
                if (start == Pos) throw new FormatException("Unexpected character '" + Peek() + "' at " + Pos);
                return double.Parse(s.Substring(start, Pos - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            }
        }

        /* ---------- writing ---------- */

        public static string Write(object value, bool indent)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value, indent, 0);
            return sb.ToString();
        }

        static void NewLine(StringBuilder sb, bool indent, int depth)
        {
            if (!indent) return;
            sb.Append('\n');
            sb.Append(' ', depth * 2);
        }

        static void WriteValue(StringBuilder sb, object v, bool indent, int depth)
        {
            if (v == null) { sb.Append("null"); return; }
            if (v is string) { WriteString(sb, (string)v); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is int || v is long || v is short || v is byte) { sb.Append(Convert.ToInt64(v, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)); return; }
            if (v is double || v is float || v is decimal)
            {
                double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                if (double.IsNaN(d) || double.IsInfinity(d)) { sb.Append("null"); return; }
                sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            var dict = v as IDictionary;
            if (dict != null)
            {
                sb.Append('{');
                bool first = true;
                foreach (DictionaryEntry kv in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    NewLine(sb, indent, depth + 1);
                    WriteString(sb, Convert.ToString(kv.Key, CultureInfo.InvariantCulture));
                    sb.Append(indent ? ": " : ":");
                    WriteValue(sb, kv.Value, indent, depth + 1);
                }
                if (!first) NewLine(sb, indent, depth);
                sb.Append('}');
                return;
            }
            var list = v as IEnumerable;
            if (list != null)
            {
                sb.Append('[');
                bool first = true;
                foreach (object item in list)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    NewLine(sb, indent, depth + 1);
                    WriteValue(sb, item, indent, depth + 1);
                }
                if (!first) NewLine(sb, indent, depth);
                sb.Append(']');
                return;
            }
            WriteString(sb, v.ToString());
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        /* ---------- reading helpers ---------- */

        public static string Str(Dictionary<string, object> o, string key)
        {
            object v;
            return o != null && o.TryGetValue(key, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : "";
        }

        public static int Int(Dictionary<string, object> o, string key)
        {
            object v;
            return o != null && o.TryGetValue(key, out v) && v is double ? (int)(double)v : 0;
        }

        public static List<object> List(Dictionary<string, object> o, string key)
        {
            object v;
            return o != null && o.TryGetValue(key, out v) ? v as List<object> ?? new List<object>() : new List<object>();
        }
    }

    /// <summary>An insertion-ordered string-keyed map, so written JSON keeps a stable field order.</summary>
    public sealed class JObject : IDictionary
    {
        readonly List<string> keys = new List<string>();
        readonly Dictionary<string, object> map = new Dictionary<string, object>(StringComparer.Ordinal);

        public JObject Add(string key, object value)
        {
            if (!map.ContainsKey(key)) keys.Add(key);
            map[key] = value;
            return this;
        }

        public object this[object key]
        {
            get { object v; return map.TryGetValue((string)key, out v) ? v : null; }
            set { Add((string)key, value); }
        }

        public ICollection Keys { get { return keys; } }
        public ICollection Values { get { var l = new List<object>(); foreach (var k in keys) l.Add(map[k]); return l; } }
        public bool IsReadOnly { get { return false; } }
        public bool IsFixedSize { get { return false; } }
        public int Count { get { return keys.Count; } }
        public object SyncRoot { get { return this; } }
        public bool IsSynchronized { get { return false; } }
        public void Add(object key, object value) { Add((string)key, value); }
        public void Clear() { keys.Clear(); map.Clear(); }
        public bool Contains(object key) { return map.ContainsKey((string)key); }
        public void Remove(object key) { if (map.Remove((string)key)) keys.Remove((string)key); }
        public void CopyTo(Array array, int index) { throw new NotSupportedException(); }

        public IDictionaryEnumerator GetEnumerator() { return new Enumerator(this); }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }

        sealed class Enumerator : IDictionaryEnumerator
        {
            readonly JObject o;
            int i = -1;
            public Enumerator(JObject owner) { o = owner; }
            public bool MoveNext() { i++; return i < o.keys.Count; }
            public void Reset() { i = -1; }
            public DictionaryEntry Entry { get { return new DictionaryEntry(o.keys[i], o.map[o.keys[i]]); } }
            public object Key { get { return o.keys[i]; } }
            public object Value { get { return o.map[o.keys[i]]; } }
            public object Current { get { return Entry; } }
        }
    }
}
