using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DbScanner.Core
{
    /// <summary>One gear item from the game's GearTypes, with its stats and runes in the DPS Calculator's names.</summary>
    public sealed class GearDef
    {
        public string Name;
        public string Class;
        public string Slot;
        public int GearId;
        public int Tier;
        public string Rarity;
        public string Focus;
        public string[] Runes;
        public string SkillRune;
        public string Magic;
        public int Level;
        internal string Norm;
    }

    /// <summary>One charm from CharmTypes. Key is the calculator's charm key ("attack", "attack@7", "eyeOfDiscovery").</summary>
    public sealed class CharmDef
    {
        public int Id;
        public string InternalName;
        public string Key;
        public string[] Names;
        public string Icon;
        public string Description;
        internal string[] Norms;
    }

    public sealed class Match<T> where T : class
    {
        public T Item;
        public double Score;
        public T RunnerUp;
        public double RunnerUpScore;
        public string Text;
        /// <summary>Confident when the best match is close to the text and clearly ahead of the next one.</summary>
        public bool Confident { get { return Item != null && Score >= 0.78 && Score - RunnerUpScore >= 0.04; } }
    }

    public sealed class Catalog
    {
        public readonly List<GearDef> Gear = new List<GearDef>();
        public readonly List<CharmDef> Charms = new List<CharmDef>();

        public static Catalog Load(string json)
        {
            var root = (Dictionary<string, object>)Json.Parse(json);
            var cat = new Catalog();
            foreach (Dictionary<string, object> g in Json.List(root, "gear"))
            {
                var def = new GearDef
                {
                    Name = Json.Str(g, "n"),
                    Class = Json.Str(g, "c"),
                    Slot = Json.Str(g, "s"),
                    GearId = Json.Int(g, "id"),
                    Tier = Json.Int(g, "t"),
                    Rarity = Json.Str(g, "r"),
                    Focus = Json.Str(g, "f"),
                    Runes = Json.List(g, "p").Select(x => Convert.ToString(x, CultureInfo.InvariantCulture)).ToArray(),
                    SkillRune = Json.Str(g, "k"),
                    Magic = Json.Str(g, "m"),
                    Level = Json.Int(g, "l")
                };
                def.Norm = Normalize(def.Name);
                cat.Gear.Add(def);
            }
            foreach (Dictionary<string, object> c in Json.List(root, "charms"))
            {
                var def = new CharmDef
                {
                    Id = Json.Int(c, "id"),
                    InternalName = Json.Str(c, "name"),
                    Key = Json.Str(c, "key"),
                    Names = Json.List(c, "names").Select(x => Convert.ToString(x, CultureInfo.InvariantCulture)).ToArray(),
                    Icon = Json.Str(c, "icon"),
                    Description = Json.Str(c, "desc")
                };
                def.Norms = def.Names.Select(Normalize).ToArray();
                cat.Charms.Add(def);
            }
            return cat;
        }

        /// <summary>Lower case, letters and digits only, single spaces; accents dropped (Turkish names).</summary>
        public static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string d = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(d.Length);
            bool space = false;
            foreach (char ch in d)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
                char c = char.ToLowerInvariant(ch);
                if (c == 'ı') c = 'i';
                if (char.IsLetterOrDigit(c))
                {
                    if (space && sb.Length > 0) sb.Append(' ');
                    sb.Append(c);
                    space = false;
                }
                else if (c == '\'' || c == '’' || c == '-')
                {
                    // "Tak-Ogg's" and "Taks Oggs" should read the same.
                }
                else space = true;
            }
            return sb.ToString();
        }

        static bool Confusable(char a, char b)
        {
            const string groups = "o0q|il1|s5|b8|z2|g6|rn|uv|ce|hb";
            foreach (string grp in groups.Split('|'))
                if (grp.IndexOf(a) >= 0 && grp.IndexOf(b) >= 0) return true;
            return false;
        }

        /// <summary>Edit distance where characters OCR often mixes up cost a quarter.</summary>
        public static double Distance(string a, string b)
        {
            int n = a.Length, m = b.Length;
            var prev = new double[m + 1];
            var cur = new double[m + 1];
            for (int j = 0; j <= m; j++) prev[j] = j;
            for (int i = 1; i <= n; i++)
            {
                cur[0] = i;
                char ca = a[i - 1];
                for (int j = 1; j <= m; j++)
                {
                    char cb = b[j - 1];
                    double sub = ca == cb ? 0 : (Confusable(ca, cb) ? 0.25 : 1);
                    double v = prev[j - 1] + sub;
                    if (prev[j] + 1 < v) v = prev[j] + 1;
                    if (cur[j - 1] + 1 < v) v = cur[j - 1] + 1;
                    cur[j] = v;
                }
                var t = prev; prev = cur; cur = t;
            }
            return prev[m];
        }

        /// <summary>1 for identical, 0 for nothing in common. A name the tooltip cut short still scores well on its start.</summary>
        public static double Similarity(string text, string name)
        {
            if (text.Length == 0 || name.Length == 0) return 0;
            double full = 1 - Distance(text, name) / Math.Max(text.Length, name.Length);
            if (name.Length > text.Length + 3 && text.Length >= 8)
            {
                double prefix = 1 - Distance(text, name.Substring(0, text.Length)) / text.Length;
                full = Math.Max(full, prefix * 0.96);
            }
            return full;
        }

        public Match<GearDef> MatchGear(string ocrText, string cls, string slot, string rarity)
        {
            string t = Normalize(ocrText);
            var res = new Match<GearDef> { Text = ocrText };
            foreach (var g in Gear)
            {
                if (cls != null && g.Class != cls) continue;
                if (slot != null && g.Slot != slot) continue;
                if (rarity != null && g.Rarity != rarity) continue;
                double s = Similarity(t, g.Norm);
                Consider(res, g, s, (a, b) => a.Name == b.Name);
            }
            return res;
        }

        public Match<CharmDef> MatchCharm(string ocrText)
        {
            string t = Normalize(ocrText);
            var res = new Match<CharmDef> { Text = ocrText };
            foreach (var c in Charms)
            {
                double best = 0;
                foreach (string n in c.Norms) best = Math.Max(best, Similarity(t, n));
                Consider(res, c, best, (a, b) => a.Key == b.Key);
            }
            return res;
        }

        static void Consider<T>(Match<T> res, T item, double score, Func<T, T, bool> sameName) where T : class
        {
            if (res.Item == null || score > res.Score)
            {
                // The same display name in another class or rarity is not a real rival.
                if (res.Item != null && !sameName(res.Item, item))
                {
                    res.RunnerUp = res.Item;
                    res.RunnerUpScore = res.Score;
                }
                res.Item = item;
                res.Score = score;
            }
            else if (score > res.RunnerUpScore && !sameName(res.Item, item))
            {
                res.RunnerUp = item;
                res.RunnerUpScore = score;
            }
        }

        public CharmDef CharmById(int id)
        {
            foreach (var c in Charms) if (c.Id == id) return c;
            return null;
        }
    }
}
