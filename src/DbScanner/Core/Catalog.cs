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

    /// <summary>
    /// One charm. Key is the calculator's charm key: "attack" (top rank), "attack@7" (rank 7),
    /// "eyeOfDiscovery" (a special charm), or with a Magic Forge bonus "expertise+defense:R".
    /// </summary>
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

    /// <summary>One rank of an ability: the player power the game uses for it (PoisonStrike10).</summary>
    public sealed class AbilityRank
    {
        public int Rank;
        public int PowerId;
        public string PowerName;
        public string ManaCost;
        public int CooldownMs;
        public string DamageType;
        /// <summary>The tooltip text, "[Stats: 1.49x attack, 2x Expertise/s (5s), ...]" included.</summary>
        public string Description;
    }

    /// <summary>
    /// A class ability from the game's AbilityTypes, as the Tome of Power and the hotbar show it.
    /// Key is the game's AbilityName (PoisonStrike); Name the display name (Poison Strike).
    /// </summary>
    public sealed class AbilityDef
    {
        public string Key;
        public string Name;
        /// <summary>Rogue, Executioner, ShadowWalker, ...: the class or discipline it belongs to.</summary>
        public string Class;
        /// <summary>Rogue, Mage or Paladin.</summary>
        public string BaseClass;
        public string Category;
        /// <summary>The hotbar slot it goes in: 1-3 the tier abilities, 4-6 the discipline's master abilities, 0 a weapon passive.</summary>
        public int Hotbar;
        public int MaxRank;
        public readonly List<AbilityRank> Ranks = new List<AbilityRank>();
        internal string Norm;

        public AbilityRank RankInfo(int rank)
        {
            foreach (var r in Ranks) if (r.Rank == rank) return r;
            return null;
        }
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
        /// <summary>The 94 charms of CharmTypes.</summary>
        public readonly List<CharmDef> Charms = new List<CharmDef>();
        /// <summary>Gem charms with a Magic Forge bonus ("Infinite Sapphire of Deflecting").</summary>
        public readonly List<CharmDef> ForgedCharms = new List<CharmDef>();
        /// <summary>Class abilities with every rank (catalog "abilities"; empty in catalogs built before 1.1).</summary>
        public readonly List<AbilityDef> Abilities = new List<AbilityDef>();

        // The game's charm types in item-id order, and the suffixes it names a forged charm with
        // (class_64 in the client): tier R adds half of the same-rank gem of the second type,
        // tier L all of it.
        static readonly string[] CharmTypes = { "", "Trog", "Infernal", "Undead", "Mythic", "Draconic", "Sylvan", "Melee", "Magic", "Armor" };
        static readonly string[] SuffixR = { "", "of Luck", "of Skill", "of Greed", "of Foraging", "of Carnage", "of Health", "of Strength", "of the Mind", "of Deflecting" };
        static readonly string[] SuffixL = { "", "of Fortune", "of Precision", "of Wealth", "of Scouring", "of Ruin", "of Fortitude", "of Might", "of Brilliance", "of Protection" };

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
            cat.AddForgedCharms();
            foreach (Dictionary<string, object> a in Json.List(root, "abilities"))
            {
                var def = new AbilityDef
                {
                    Key = Json.Str(a, "k"),
                    Name = Json.Str(a, "n"),
                    Class = Json.Str(a, "c"),
                    BaseClass = Json.Str(a, "b"),
                    Category = Json.Str(a, "cat"),
                    Hotbar = Json.Int(a, "h"),
                    MaxRank = Json.Int(a, "max")
                };
                foreach (List<object> r in Json.List(a, "r"))
                {
                    def.Ranks.Add(new AbilityRank
                    {
                        Rank = Convert.ToInt32(r[0], CultureInfo.InvariantCulture),
                        PowerId = Convert.ToInt32(r[1], CultureInfo.InvariantCulture),
                        PowerName = Convert.ToString(r[2], CultureInfo.InvariantCulture),
                        ManaCost = Convert.ToString(r[3], CultureInfo.InvariantCulture),
                        CooldownMs = Convert.ToInt32(r[4], CultureInfo.InvariantCulture),
                        DamageType = Convert.ToString(r[5], CultureInfo.InvariantCulture),
                        Description = Convert.ToString(r[6], CultureInfo.InvariantCulture)
                    });
                }
                def.Norm = Normalize(def.Name);
                cat.Abilities.Add(def);
            }
            return cat;
        }

        /// <summary>Every gem charm with every Magic Forge bonus, named the way the game names them.</summary>
        void AddForgedCharms()
        {
            // The stat key of each type, from its charms ("Armor10" is "defense", "Melee7" is "attack@7").
            var statOf = new Dictionary<string, string>();
            foreach (var c in Charms)
            {
                var m = System.Text.RegularExpressions.Regex.Match(c.InternalName ?? "", @"^([A-Za-z]+?)(\d+)$");
                if (m.Success && Array.IndexOf(CharmTypes, m.Groups[1].Value) > 0) statOf[m.Groups[1].Value] = c.Key.Split('@')[0];
            }
            foreach (var c in Charms)
            {
                var m = System.Text.RegularExpressions.Regex.Match(c.InternalName ?? "", @"^([A-Za-z]+?)(\d+)$");
                if (!m.Success || Array.IndexOf(CharmTypes, m.Groups[1].Value) <= 0) continue;
                for (int s = 1; s < CharmTypes.Length; s++)
                {
                    string stat;
                    if (!statOf.TryGetValue(CharmTypes[s], out stat)) continue;
                    for (int tier = 1; tier <= 2; tier++)
                    {
                        string suffix = (tier == 1 ? SuffixR : SuffixL)[s];
                        var def = new CharmDef
                        {
                            Id = c.Id | (s << 9) | (tier << 14),
                            InternalName = c.InternalName + (tier == 1 ? "R" : "L") + s.ToString(CultureInfo.InvariantCulture),
                            Key = c.Key + "+" + stat + ":" + (tier == 1 ? "R" : "L"),
                            Names = c.Names.Select(n => n + " " + suffix).ToArray(),
                            Icon = c.Icon,
                            Description = c.Description
                        };
                        def.Norms = def.Names.Select(Normalize).ToArray();
                        ForgedCharms.Add(def);
                    }
                }
            }
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
            foreach (var c in Charms.Concat(ForgedCharms))
            {
                double best = 0;
                foreach (string n in c.Norms) best = Math.Max(best, Similarity(t, n));
                Consider(res, c, best, (a, b) => a.Key == b.Key);
            }
            return res;
        }

        /// <summary>
        /// The ability a tooltip name belongs to. With a class ("Rogue"), abilities of the other
        /// two classes are left out, since the Tome and the hotbar only show your own.
        /// </summary>
        public Match<AbilityDef> MatchAbility(string ocrText, string baseClass)
        {
            string t = Normalize(ocrText);
            var res = new Match<AbilityDef> { Text = ocrText };
            foreach (var a in Abilities)
            {
                if (!string.IsNullOrEmpty(baseClass) && a.BaseClass != baseClass) continue;
                Consider(res, a, Similarity(t, a.Norm), (x, y) => x.Key == y.Key);
            }
            return res;
        }

        public AbilityDef AbilityByKey(string key)
        {
            foreach (var a in Abilities) if (a.Key == key) return a;
            return null;
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
