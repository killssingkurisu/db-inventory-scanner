using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace DbScanner.Core
{
    /// <summary>One ability read from the Tome of Power or the hotbar.</summary>
    public sealed class ScannedSpell
    {
        public AbilityDef Def;
        public double Score;
        public string OcrName = "";
        public string OcrType = "";
        public string OcrDescription = "";
        /// <summary>The rank the tooltip showed; 0 when it showed none (not trained yet).</summary>
        public int Rank;
        public bool RankFromText;
        public string ManaCost = "";
        /// <summary>Where it is in the Tome (page 0-4, slot 0-11); -1 when only seen on the hotbar.</summary>
        public int Page = -1;
        public int Slot = -1;
        /// <summary>Whose page of the Tome it is: the class (Rogue), a discipline (Executioner) or "Master".</summary>
        public string PageName = "";
        /// <summary>The hotbar key it's on ("1", "E", ...), or null when it isn't equipped.</summary>
        public string HotbarKey;
        public int HotbarSlot;
        public int Tier { get { return Slot < 0 ? 0 : Slot / UiLayout.TomeColumns + 1; } }
        public bool Bought { get { return Rank > 0; } }

        public AbilityRank RankInfo { get { return Def == null ? null : Def.RankInfo(Rank > 0 ? Rank : 1); } }
    }

    /// <summary>"1.49x attack", "2x Expertise/s (5s)": the terms of a description's Stats line.</summary>
    public sealed class ScalingTerm
    {
        public double Multiplier;
        public string Stat;
        public bool PerSecond;
        public double Seconds;
    }

    public static class SpellText
    {
        static readonly Regex StatsBlock = new Regex(@"\[Stats:([^\]]*)\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex Term = new Regex(@"(\d+(?:\.\d+)?)x\s*(attack|expertise|heal)(/s)?(?:\s*\((\d+(?:\.\d+)?)s\))?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex RankRe = new Regex(@"R\s*[a@o]\s*n\s*[kK]\s*:?\s*([0-9OoIl|]{1,2})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex ManaRe = new Regex(@"M\s*[a@]\s*n\s*[a@]\s*C\s*[o0]\s*s\s*t\s*[:;.]?\s*([0-9OoIl|,]{1,6})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>The current rank's Stats text, before any "| Next rank: ...".</summary>
        public static string StatsText(string description)
        {
            var m = StatsBlock.Match(description ?? "");
            return m.Success ? m.Groups[1].Value.Split('|')[0].Trim() : "";
        }

        public static List<ScalingTerm> Scaling(string description)
        {
            var list = new List<ScalingTerm>();
            foreach (Match t in Term.Matches(StatsText(description)))
            {
                list.Add(new ScalingTerm
                {
                    Multiplier = double.Parse(t.Groups[1].Value, CultureInfo.InvariantCulture),
                    Stat = t.Groups[2].Value.ToLowerInvariant(),
                    PerSecond = t.Groups[3].Success,
                    Seconds = t.Groups[4].Success ? double.Parse(t.Groups[4].Value, CultureInfo.InvariantCulture) : 0
                });
            }
            return list;
        }

        static int Digits(string s)
        {
            s = s.Replace('O', '0').Replace('o', '0').Replace('I', '1').Replace('l', '1').Replace('|', '1').Replace(",", "");
            int v;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : -1;
        }

        /// <summary>"Rank 10" -> 10; -1 when the line has no rank.</summary>
        public static int ParseRank(string line)
        {
            var m = RankRe.Match(line ?? "");
            return m.Success ? Digits(m.Groups[1].Value) : -1;
        }

        /// <summary>"Mana Cost: 20" -> "20"; "" when the line has none.</summary>
        public static string ParseMana(string line)
        {
            var m = ManaRe.Match(line ?? "");
            if (!m.Success) return "";
            int v = Digits(m.Groups[1].Value);
            return v >= 0 ? v.ToString(CultureInfo.InvariantCulture) : "";
        }
    }

    public sealed partial class Scanner
    {
        string spellClass = "";

        bool AbilityTipShown(RgbImage game)
        {
            return Imaging.FillShare(game, local, UiLayout.AbilityTipBackground(), UiLayout.TipFill, 34) >= 0.7;
        }

        /// <summary>
        /// Reads the ability tooltip: name, rank, mana cost and the visible part of the description.
        /// The name is matched against the game's abilities; when the rank line didn't read, the
        /// rank whose description fits the visible text best is taken.
        /// </summary>
        ScannedSpell ReadAbility(RgbImage game, string where)
        {
            var sp = new ScannedSpell();
            sp.OcrName = ReadText(game, UiLayout.AbilityTipName, "spell-name");
            sp.OcrType = ReadText(game, UiLayout.AbilityTipType, "spell-rank");
            var lines = new List<string>();
            for (int i = 0; i < UiLayout.AbilityTipDesc.Length; i++)
            {
                string t = ReadText(game, UiLayout.AbilityTipDesc[i], "spell-desc" + (i + 1));
                if (t.Length > 0) lines.Add(t);
            }
            sp.OcrDescription = string.Join(" ", lines);

            string cls = string.IsNullOrEmpty(spellClass) ? null : spellClass;
            var m = catalog.MatchAbility(sp.OcrName, cls);
            if (!m.Confident && cls != null) m = BetterOf(m, catalog.MatchAbility(sp.OcrName, null));
            sp.Score = m.Score;
            if (m.Item == null || m.Score < 0.6)
            {
                problems.Add(where + ": couldn't read the spell name (\"" + sp.OcrName + "\").");
                return sp;
            }
            sp.Def = m.Item;
            if (!m.Confident)
                problems.Add(string.Format(CultureInfo.InvariantCulture, "{0}: read \"{1}\", saved as \"{2}\" ({3:0}% sure)", where, sp.OcrName, m.Item.Name, m.Score * 100));

            int rank = SpellText.ParseRank(sp.OcrType);
            sp.ManaCost = SpellText.ParseMana(sp.OcrType);
            if (rank >= 1 && rank <= sp.Def.MaxRank)
            {
                sp.Rank = rank;
                sp.RankFromText = true;
            }
            else if (rank == 0)
            {
                sp.Rank = 0;
                sp.RankFromText = true;
            }
            else
            {
                int hi;
                sp.Rank = RankFromDescription(sp.Def, sp.OcrDescription, out hi);
                if (sp.Rank > 0)
                    problems.Add(string.Format(CultureInfo.InvariantCulture, "{0}: couldn't read the rank of {1}; {2} fits its description, saved as rank {3}.",
                        where, sp.Def.Name, hi > sp.Rank ? "ranks " + sp.Rank + " to " + hi : "rank " + sp.Rank, sp.Rank));
            }
            return sp;
        }

        static Match<AbilityDef> BetterOf(Match<AbilityDef> a, Match<AbilityDef> b)
        {
            return b.Item != null && (a.Item == null || b.Score > a.Score + 0.02) ? b : a;
        }

        /// <summary>
        /// The rank whose tooltip text is closest to what was read (ranks differ in their Stats
        /// numbers). Top ranks often share one text (Poison Strike 8 to 10), so this gives the lowest
        /// rank that fits and, in `highest`, the highest; 0 when nothing fits.
        /// </summary>
        public static int RankFromDescription(AbilityDef def, string ocrDescription, out int highest)
        {
            highest = 0;
            string t = Catalog.Normalize(ocrDescription);
            if (t.Length < 12 || def == null) return 0;
            int best = 0;
            double bestScore = 0;
            foreach (var r in def.Ranks)
            {
                string d = Catalog.Normalize(r.Description);
                if (d.Length > t.Length) d = d.Substring(0, t.Length);
                double s = 1 - Catalog.Distance(t, d) / Math.Max(t.Length, d.Length);
                if (s > bestScore + 1e-9) { bestScore = s; best = r.Rank; highest = r.Rank; }
                else if (Math.Abs(s - bestScore) <= 1e-9 && best > 0) highest = Math.Max(highest, r.Rank);
            }
            if (bestScore < 0.7) { highest = 0; return 0; }
            return best;
        }

        public static int RankFromDescription(AbilityDef def, string ocrDescription)
        {
            int hi;
            return RankFromDescription(def, ocrDescription, out hi);
        }

        void ParkAt(PointD p)
        {
            var s = geo.ToScreen(p);
            surface.MoveTo((int)Math.Round(s.X), (int)Math.Round(s.Y));
        }

        void NoteClass(ScannedSpell sp)
        {
            if (sp.Def == null || sp.Score < 0.85) return;
            if (string.IsNullOrEmpty(spellClass)) spellClass = sp.Def.BaseClass;
        }

        /// <summary>Hovers a point and says whether the ability tooltip came up.</summary>
        RgbImage HoverForAbility(PointD p, out bool shown)
        {
            Hover(p);
            CheckStop(p);
            var game = CaptureGame();
            shown = AbilityTipShown(game);
            return game;
        }

        /// <summary>
        /// Finds the game and works out what is on screen: the Tome of Power (every ability and
        /// rank) or just the hotbar (the equipped ones). Nothing is clicked until one of them is found.
        /// </summary>
        bool LocateSpells(ScanResult result)
        {
            RectI client = surface.ClientArea();
            if (client.W < 200 || client.H < 150) throw new ScanAbortedException("The game window is too small or minimized.");
            var shot = surface.Capture(client);
            Debug("window", shot);
            foreach (var g in Candidates(client, shot, surface.DpiScale()))
            {
                UseGeometry(g);
                foreach (int slot in new[] { 0, 1, 4 })
                {
                    bool shown;
                    var game = HoverForAbility(UiLayout.TomeSlot(slot).Center, out shown);
                    if (shown)
                    {
                        Debug("tome-found", game);
                        Say("Game found: " + g + ". The Tome of Power is open.");
                        result.Geometry = g;
                        return true;
                    }
                }
                for (int k = 0; k < 3; k++)
                {
                    bool shown;
                    var game = HoverForAbility(UiLayout.HotbarKey(k).Center, out shown);
                    if (shown)
                    {
                        Debug("hotbar-found", game);
                        Say("Game found: " + g + ". The Tome of Power isn't open, so only the hotbar is read.");
                        result.Geometry = g;
                        return false;
                    }
                }
            }
            throw new ScanAbortedException("Couldn't find your hotbar or the Tome of Power. Open the Tome of Power in the game (or close every window so the hotbar shows), keep the game window in front, and try again.");
        }

        /// <summary>Spell scan: the Tome of Power's pages when it's open, then the hotbar.</summary>
        public ScanResult RunSpells()
        {
            var result = new ScanResult { SpellsScanned = true };
            problems = result.Problems;
            spellClass = "";
            try
            {
                Report("Finding the game", null);
                bool tome = LocateSpells(result);
                if (tome)
                {
                    ScanTome(result);
                    ClickAt(UiLayout.TomeExit.Center, opt.PageDelayMs);
                }
                else
                {
                    problems.Add("Only the hotbar was read. Open the Tome of Power before scanning to get every spell you own and its rank.");
                }
                result.TomeRead = tome;
                ScanHotbar(result);
                ParkMouse();
            }
            catch (OperationCanceledException)
            {
                result.Cancelled = true;
                problems.Add("The scan was stopped before the end; what was read so far is kept.");
            }
            catch (ScanAbortedException e)
            {
                result.Cancelled = true;
                problems.Add(e.Message);
            }
            var classes = result.Spells.Where(s => s.Def != null).GroupBy(s => s.Def.BaseClass).OrderByDescending(x => x.Count()).ToList();
            if (classes.Count > 0) result.Class = classes[0].Key;
            result.ScannedAt = DateTime.UtcNow;
            Report("Done", null);
            return result;
        }

        void ScanTome(ScanResult result)
        {
            var seen = new HashSet<string>();
            for (int page = 0; page < UiLayout.TomeTabs; page++)
            {
                ClickAt(UiLayout.TomeTab(page).Center, opt.PageDelayMs);
                ParkAt(UiLayout.TomePark);
                surface.Sleep(Math.Max(120, opt.HoverDelayMs / 2));
                var pageShot = CaptureGame();
                Debug(string.Format(CultureInfo.InvariantCulture, "tome-page{0}", page + 1), pageShot);
                var onPage = new List<ScannedSpell>();
                int found = 0;
                bool lastShown = false;
                for (int slot = 0; slot < UiLayout.TomeSlots; slot++)
                {
                    if (Imaging.TomeSlotEmpty(Region(pageShot, UiLayout.TomeSlotIcon(slot)))) continue;
                    if (lastShown)
                    {
                        // Let the last tooltip go, so an empty-looking slot can't show the previous spell.
                        ParkAt(UiLayout.TomePark);
                        surface.Sleep(60);
                    }
                    bool shown;
                    var game = HoverForAbility(UiLayout.TomeSlot(slot).Center, out shown);
                    lastShown = shown;
                    string where = string.Format(CultureInfo.InvariantCulture, "Tome page {0} slot {1}", page + 1, slot + 1);
                    if (!shown)
                    {
                        Debug(string.Format(CultureInfo.InvariantCulture, "tome-p{0}-s{1}-none", page + 1, slot + 1), game);
                        continue;
                    }
                    Debug(string.Format(CultureInfo.InvariantCulture, "tome-p{0}-s{1}", page + 1, slot + 1), game);
                    var sp = ReadAbility(game, where);
                    sp.Page = page;
                    sp.Slot = slot;
                    found++;
                    if (sp.Def == null) continue;
                    onPage.Add(sp);
                    NoteClass(sp);
                    if (!seen.Add(sp.Def.Key)) continue; // the master page can repeat a discipline's abilities
                    result.Spells.Add(sp);
                    Report("Reading the Tome of Power, page " + (page + 1), sp.Def.Name + (sp.Rank > 0 ? " rank " + sp.Rank : ""));
                }
                if (found == 0) Debug(string.Format(CultureInfo.InvariantCulture, "tome-page{0}-empty", page + 1), pageShot);
                // Which class or discipline the page is for: the one most of its abilities belong to.
                // The bottom tab is the master page, with the disciplines' hotbar 4/E/Q abilities.
                string pageClass = page == UiLayout.TomeTabs - 1 ? "Master" :
                    onPage.GroupBy(x => x.Def.Class).OrderByDescending(x => x.Count()).Select(x => x.Key).FirstOrDefault() ?? "";
                foreach (var x in onPage) x.PageName = pageClass;
            }
            Say(result.Spells.Count + " spells read from the Tome of Power (" + result.Spells.Count(s => s.Bought) + " trained).");
        }

        void ScanHotbar(ScanResult result)
        {
            for (int k = 0; k < UiLayout.HotbarLabels.Length; k++)
            {
                ParkMouse();
                surface.Sleep(60);
                bool shown;
                var game = HoverForAbility(UiLayout.HotbarKey(k).Center, out shown);
                string label = UiLayout.HotbarLabels[k];
                if (!shown)
                {
                    Debug("hotbar-" + label + "-empty", game);
                    continue;
                }
                Debug("hotbar-" + label, game);
                var sp = ReadAbility(game, "Hotbar key " + label);
                if (sp.Def == null) continue;
                NoteClass(sp);
                var known = result.Spells.FirstOrDefault(s => s.Def == sp.Def);
                if (known != null)
                {
                    known.HotbarKey = label;
                    known.HotbarSlot = k + 1;
                    if (known.Rank == 0 && sp.Rank > 0) known.Rank = sp.Rank;
                    sp = known;
                }
                else
                {
                    sp.HotbarKey = label;
                    sp.HotbarSlot = k + 1;
                    result.Spells.Add(sp);
                }
                result.Hotbar.Add(sp);
                Report("Reading the hotbar", label + ": " + sp.Def.Name);
            }
            Say("Hotbar: " + (result.Hotbar.Count == 0 ? "no spells" : string.Join(", ", result.Hotbar.Select(s => s.HotbarKey + " " + s.Def.Name))) + ".");
        }

        /* ---------- for tests ---------- */

        public ScannedSpell ReadAbilityFrom(RgbImage game, GameGeometry localGeometry, string cls)
        {
            local = localGeometry;
            spellClass = cls ?? "";
            if (problems == null) problems = new List<string>();
            return AbilityTipShown(game) ? ReadAbility(game, "test") : null;
        }

        public bool TomeSlotEmptyFrom(RgbImage game, GameGeometry localGeometry, int slot)
        {
            local = localGeometry;
            return Imaging.TomeSlotEmpty(Region(game, UiLayout.TomeSlotIcon(slot)));
        }
    }

    public sealed partial class ScanResult
    {
        public readonly List<ScannedSpell> Spells = new List<ScannedSpell>();
        public readonly List<ScannedSpell> Hotbar = new List<ScannedSpell>();
        public bool SpellsScanned;
        public bool TomeRead;

        public const string SpellsFormat = "dbb-spells";

        /// <summary>The "spells" object (FORMAT.md): every ability read and the hotbar.</summary>
        public JObject SpellsJson()
        {
            var abilities = new List<object>();
            foreach (var s in Spells.Where(x => x.Def != null).OrderBy(x => x.Page < 0 ? 99 : x.Page).ThenBy(x => x.Slot))
            {
                var d = s.Def;
                var r = s.RankInfo;
                string description = r != null ? r.Description : "";
                var o = new JObject()
                    .Add("key", d.Key).Add("name", d.Name).Add("class", d.Class).Add("baseClass", d.BaseClass)
                    .Add("category", d.Category).Add("hotbarSlot", d.Hotbar)
                    .Add("rank", s.Rank).Add("maxRank", d.MaxRank).Add("bought", s.Bought)
                    .Add("equipped", s.HotbarKey);
                if (s.Page >= 0) o.Add("tome", new JObject().Add("page", s.Page + 1).Add("pageClass", s.PageName).Add("tier", s.Tier).Add("slot", s.Slot + 1));
                if (r != null)
                {
                    o.Add("powerId", r.PowerId).Add("powerName", r.PowerName).Add("manaCost", s.ManaCost.Length > 0 ? s.ManaCost : r.ManaCost)
                     .Add("cooldownMs", r.CooldownMs).Add("damageType", r.DamageType);
                }
                o.Add("description", description)
                 .Add("scalingText", SpellText.StatsText(description))
                 .Add("scaling", SpellText.Scaling(description).Select(t => (object)new JObject()
                     .Add("multiplier", t.Multiplier).Add("stat", t.Stat).Add("perSecond", t.PerSecond).Add("seconds", t.Seconds)).ToList())
                 .Add("tooltip", new JObject().Add("name", s.OcrName).Add("rankLine", s.OcrType).Add("description", s.OcrDescription))
                 .Add("confidence", Math.Round(s.Score, 3));
                abilities.Add(o);
            }
            var hotbar = new List<object>();
            foreach (var s in Hotbar.Where(x => x.Def != null))
                hotbar.Add(new JObject().Add("slot", s.HotbarSlot).Add("slotKey", s.HotbarKey).Add("key", s.Def.Key).Add("name", s.Def.Name).Add("rank", s.Rank));
            return new JObject()
                .Add("scannedAt", ScannedAt.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))
                .Add("readFrom", TomeRead ? "tome+hotbar" : "hotbar")
                .Add("abilities", abilities)
                .Add("hotbar", hotbar);
        }

        /// <summary>A spells-only scan file: the DPS overlay and the calculator read "spells".</summary>
        public JObject ToSpellsJson(string source)
        {
            var root = new JObject()
                .Add("format", SpellsFormat)
                .Add("version", 1)
                .Add("source", source)
                .Add("scannedAt", ScannedAt.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))
                .Add("character", new JObject().Add("name", CharacterName).Add("class", Class))
                .Add("spells", SpellsJson());
            if (Problems.Count > 0) root.Add("notes", Problems.ToList<object>());
            return root;
        }
    }
}
