using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace DbScanner.Core
{
    /// <summary>Reads one line of text. Gets dark text on white, already enlarged.</summary>
    public interface IOcr
    {
        string Read(RgbImage line);
    }

    /// <summary>The game on screen: capture pixels, move and click the mouse.</summary>
    public interface IGameSurface
    {
        /// <summary>The window's client area in screen pixels.</summary>
        RectI ClientArea();
        RgbImage Capture(RectI screenRect);
        void MoveTo(int x, int y);
        void Click(int x, int y);
        void Sleep(int ms);
        /// <summary>True when the user has taken the mouse somewhere else; the scan stops.</summary>
        bool UserMovedMouse(int expectedX, int expectedY);
        /// <summary>The display scaling of the game's window as a factor: 1 at 100%, 1.5 at 150%.</summary>
        double DpiScale();
    }

    public sealed class ScanOptions
    {
        public bool Bag = true;
        public bool Charms = true;
        /// <summary>How long a tooltip takes to appear after the mouse moves onto an item.</summary>
        public int HoverDelayMs = 220;
        /// <summary>How long a tab or page change takes to draw.</summary>
        public int PageDelayMs = 450;
        public int MaxPages = 40;
        /// <summary>Save every capture and OCR result when set.</summary>
        public bool Debug;
    }

    public sealed class GearStats
    {
        public int Attack, Expertise, Defense;
        public bool Read;
        public override string ToString() { return Attack + "/" + Expertise + "/" + Defense; }
        public override bool Equals(object o)
        {
            var s = o as GearStats;
            return s != null && s.Attack == Attack && s.Expertise == Expertise && s.Defense == Defense;
        }
        public override int GetHashCode() { return Attack * 1000003 ^ Expertise * 1009 ^ Defense; }
    }

    public sealed class ScannedGear
    {
        public GearDef Def;
        public string OcrName = "";
        public string Rarity;
        public double Score;
        public bool Equipped;
        public GearStats Stats = new GearStats();
        public string[] Charms = new string[3];
        public string Where = "";
    }

    public sealed class ScannedCharm
    {
        public CharmDef Def;
        public int Count;
        public bool CountUncertain;
        public string OcrName = "";
        public double Score;
    }

    public sealed partial class ScanResult
    {
        public string CharacterName = "";
        public string Class = "";
        public readonly List<ScannedGear> Gear = new List<ScannedGear>();
        public readonly List<ScannedCharm> Charms = new List<ScannedCharm>();
        public readonly List<string> Problems = new List<string>();
        public int SkippedItems;
        public GameGeometry Geometry;
        public bool Cancelled;
        public DateTime ScannedAt = DateTime.UtcNow;

        public const string Format = "dbb-inventory";

        /// <summary>The file the DPS Calculator imports (see FORMAT.md).</summary>
        public JObject ToJson(string source)
        {
            var gear = new List<object>();
            foreach (var g in Gear)
            {
                if (g.Def == null) continue;
                var d = g.Def;
                var o = new JObject()
                    .Add("name", d.Name).Add("gearId", d.GearId).Add("tier", d.Tier).Add("slot", d.Slot)
                    .Add("rarity", d.Rarity).Add("focus", d.Focus).Add("runes", d.Runes.ToList<object>())
                    .Add("skillRune", d.SkillRune).Add("magic", d.Magic).Add("level", d.Level)
                    .Add("equipped", g.Equipped);
                if (g.Stats.Read)
                    o.Add("stats", new JObject().Add("attack", g.Stats.Attack).Add("expertise", g.Stats.Expertise).Add("defense", g.Stats.Defense));
                o.Add("charms", g.Charms.Select(c => (object)c).ToList()).Add("confidence", Math.Round(g.Score, 3));
                gear.Add(o);
            }
            var charms = new List<object>();
            foreach (var c in Charms)
            {
                if (c.Def == null) continue;
                charms.Add(new JObject().Add("key", c.Def.Key).Add("name", c.Def.Names[0]).Add("count", c.Count));
            }
            var root = new JObject()
                .Add("format", Format)
                .Add("version", 1)
                .Add("source", source)
                .Add("scannedAt", ScannedAt.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))
                .Add("character", new JObject().Add("name", CharacterName).Add("class", Class))
                .Add("gear", gear)
                .Add("charms", charms);
            if (Problems.Count > 0) root.Add("notes", Problems.ToList<object>());
            return root;
        }
    }

    public sealed class ScanProgress
    {
        public string Stage = "";
        public int Items;
        public int Charms;
        public string Last = "";
    }

    public sealed class ScanAbortedException : Exception
    {
        public ScanAbortedException(string message) : base(message) { }
    }

    /// <summary>
    /// Walks the inventory window the way a player would: hovers every equipped item, every
    /// item in the bag pages and every charm, reads each tooltip, and looks the name up in the
    /// game's own item list. Positions come from UiLayout and GameGeometry.
    /// </summary>
    public sealed partial class Scanner
    {
        readonly IGameSurface surface;
        readonly IOcr ocr;
        readonly Catalog catalog;
        readonly ScanOptions opt;
        GameGeometry geo;
        GameGeometry local;
        RectI gameRect;
        int shots;
        string cls = "";
        List<string> problems = new List<string>();
        readonly ScanProgress progress = new ScanProgress();

        public event Action<string> Log;
        public event Action<ScanProgress> Progress;
        public CancellationToken Cancel = CancellationToken.None;
        /// <summary>Set by the app to save debug pictures and text: (name, image, text).</summary>
        public Action<string, RgbImage, string> DebugSink;

        public Scanner(IGameSurface surface, IOcr ocr, Catalog catalog, ScanOptions options)
        {
            this.surface = surface;
            this.ocr = ocr;
            this.catalog = catalog;
            opt = options ?? new ScanOptions();
        }

        void Say(string s) { var h = Log; if (h != null) h(s); }

        void Report(string stage, string last)
        {
            progress.Stage = stage;
            if (last != null) progress.Last = last;
            var h = Progress;
            if (h != null) h(progress);
        }

        void Debug(string what, RgbImage img, string text = null)
        {
            if (!opt.Debug || DebugSink == null) return;
            shots++;
            DebugSink(string.Format(CultureInfo.InvariantCulture, "{0:0000}-{1}", shots, what), img, text);
        }

        /* ---------- geometry ---------- */

        void UseGeometry(GameGeometry g)
        {
            geo = g;
            gameRect = g.GameRect;
            local = new GameGeometry { Scale = g.Scale, OriginX = g.OriginX - gameRect.X, OriginY = g.OriginY - gameRect.Y, Source = g.Source };
        }

        /// <summary>
        /// Where the game might be drawn: the whole client area (the launcher and the Flash
        /// projector), the 3:2 box the game's web page uses, or the picture found on screen.
        /// </summary>
        public static List<GameGeometry> Candidates(RectI client, RgbImage clientShot, double dpi = 1)
        {
            var list = new List<GameGeometry>();
            // On a scaled display (150% and the like) Flash lays the game out in device-independent
            // pixels, so that layout is the likeliest; the unscaled one stays as a fallback.
            if (dpi >= 1.01) list.Add(GameGeometry.ForFlashArea(client.X, client.Y, client.W, client.H, dpi));
            var plain = GameGeometry.ForFlashArea(client.X, client.Y, client.W, client.H);
            if (list.All(x => Math.Abs(x.Scale - plain.Scale) > 0.003 || Math.Abs(x.OriginX - plain.OriginX) > 2)) list.Add(plain);
            int bw = (int)Math.Min(client.W, client.H * 1.5), bh = (int)Math.Min(client.H, client.W / 1.5);
            var page = GameGeometry.ForFlashArea(client.X + (client.W - bw) / 2, client.Y + (client.H - bh) / 2, bw, bh);
            page.Source = "web page";
            if (list.All(x => Math.Abs(page.Scale - x.Scale) > 0.003 || Math.Abs(page.OriginX - x.OriginX) > 2)) list.Add(page);
            if (clientShot != null)
            {
                RectI? found = Imaging.FindGameRect(clientShot);
                if (found.HasValue)
                {
                    var f = found.Value;
                    var g = GameGeometry.ForDrawnRect(new RectI(f.X + client.X, f.Y + client.Y, f.W, f.H));
                    if (Math.Abs((double)f.W / Math.Max(1, f.H) - 1.5) < 0.05 && list.All(x => Math.Abs(x.Scale - g.Scale) > 0.003)) list.Add(g);
                }
            }
            return list;
        }

        /// <summary>Finds the game and checks that the inventory window is open, by hovering the equipped gear.</summary>
        public GameGeometry Locate(ScanResult result)
        {
            RectI client = surface.ClientArea();
            if (client.W < 200 || client.H < 150) throw new ScanAbortedException("The game window is too small or minimized.");
            var shot = surface.Capture(client);
            Debug("window", shot);
            foreach (var g in Candidates(client, shot, surface.DpiScale()))
            {
                UseGeometry(g);
                if (EquippedTooltipAppears())
                {
                    Say("Game found: " + g);
                    result.Geometry = g;
                    return g;
                }
            }
            throw new ScanAbortedException("Couldn't find the open inventory. Open your inventory (the Gear tab) in the game, keep the game window in front, and try again.");
        }

        bool EquippedTooltipAppears()
        {
            // Click the Gear tab first (harmless when already there), then hover the paper doll.
            ClickAt(UiLayout.TabGear.Center, opt.PageDelayMs);
            for (int k = 0; k < 6; k++)
            {
                Hover(UiLayout.EquipmentSlot(k).Center);
                var game = CaptureGame();
                if (GearTipShown(game)) return true;
            }
            return false;
        }

        /* ---------- low-level steps ---------- */

        RgbImage CaptureGame() { return surface.Capture(gameRect); }

        void Hover(PointD p)
        {
            var s = geo.ToScreen(p);
            int x = (int)Math.Round(s.X), y = (int)Math.Round(s.Y);
            // A small jiggle so Flash sees a fresh mouse-over even when the cursor is already there.
            surface.MoveTo(x - 3, y - 3);
            surface.MoveTo(x, y);
            surface.Sleep(opt.HoverDelayMs);
        }

        void ClickAt(PointD p, int waitMs)
        {
            var s = geo.ToScreen(p);
            surface.Click((int)Math.Round(s.X), (int)Math.Round(s.Y));
            surface.Sleep(waitMs);
        }

        void CheckStop(PointD expected)
        {
            if (Cancel.IsCancellationRequested) throw new OperationCanceledException();
            var s = geo.ToScreen(expected);
            if (surface.UserMovedMouse((int)Math.Round(s.X), (int)Math.Round(s.Y)))
                throw new ScanAbortedException("Stopped because the mouse was moved.");
        }

        void ParkMouse()
        {
            var s = geo.ToScreen(new PointD(940, 652));
            surface.MoveTo((int)s.X, (int)s.Y);
        }

        RgbImage Region(RgbImage game, RectD r) { return game.Crop(local.ToScreen(r)); }

        string ReadText(RgbImage game, RectD r, string tag)
        {
            var prepared = Imaging.TextForOcr(Region(game, r), 64);
            string text = (ocr.Read(prepared) ?? "").Trim();
            Debug("ocr-" + tag, prepared, text);
            return text;
        }

        bool GearTipShown(RgbImage game)
        {
            return Imaging.FillShare(game, local, UiLayout.GearTipBackground(), UiLayout.TipFill, 34) >= 0.7;
        }

        bool CharmTipShown(RgbImage game)
        {
            return Imaging.FillShare(game, local, UiLayout.CharmTipBackground(), UiLayout.TipFill, 34) >= 0.7;
        }

        bool EquippedBadge(RgbImage game, int slot)
        {
            return Imaging.EquippedBadge(Region(game, UiLayout.GridEquippedBadge(slot)));
        }

        /* ---------- reading tooltips ---------- */

        static readonly Regex StatLine = new Regex(@"([0-9OoIl|SB][0-9OoIl|SB,\.]*)\s*([A-Za-z]{4,})", RegexOptions.Compiled);

        /// <summary>"+472 Attack" → (472, attack). OCR's usual digit mix-ups are undone.</summary>
        public static bool ParseStat(string text, out int value, out string stat)
        {
            value = 0;
            stat = null;
            if (string.IsNullOrEmpty(text)) return false;
            foreach (Match m in StatLine.Matches(text))
            {
                string word = m.Groups[2].Value.ToLowerInvariant();
                string key = Closest(word, new[] { "attack", "expertise", "defense" });
                if (key == null) continue;
                string digits = m.Groups[1].Value.Replace(",", "").Replace(".", "")
                    .Replace('O', '0').Replace('o', '0').Replace('I', '1').Replace('l', '1').Replace('|', '1').Replace('S', '5').Replace('B', '8');
                int v;
                if (!int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) || v <= 0 || v > 20000) continue;
                value = v;
                stat = key;
                return true;
            }
            return false;
        }

        static string Closest(string word, string[] options)
        {
            string best = null;
            double bestScore = 0;
            foreach (var o in options)
            {
                double s = Catalog.Similarity(word, o);
                if (s > bestScore) { bestScore = s; best = o; }
            }
            return bestScore >= 0.6 ? best : null;
        }

        ScannedGear ReadGear(RgbImage game, string where, string slotHint)
        {
            var nameImg = Region(game, UiLayout.GearTipName);
            string rarity = Imaging.RarityFromColor(Imaging.TextColor(nameImg));
            string name = ReadText(game, UiLayout.GearTipName, "name");
            var g = new ScannedGear { OcrName = name, Rarity = rarity, Where = where };
            string c = string.IsNullOrEmpty(cls) ? null : cls;
            var m = catalog.MatchGear(name, c, slotHint, rarity);
            if (!m.Confident && rarity != null) m = BetterOf(m, catalog.MatchGear(name, c, slotHint, null));
            if (!m.Confident && c != null) m = BetterOf(m, catalog.MatchGear(name, null, slotHint, null));
            g.Score = m.Score;
            if (m.Item == null || m.Score < 0.6) return g;   // not gear (a lockbox or a chest) or unreadable
            g.Def = m.Item;
            if (!m.Confident)
                problems.Add(string.Format(CultureInfo.InvariantCulture, "{0}: read \"{1}\", saved as \"{2}\" ({3:0}% sure)", where, name, m.Item.Name, m.Score * 100));
            for (int i = 0; i < 3; i++)
            {
                string t = ReadText(game, UiLayout.GearTipStats[i], "stat" + (i + 1));
                int v;
                string stat;
                if (!ParseStat(t, out v, out stat)) continue;
                g.Stats.Read = true;
                if (stat == "attack") g.Stats.Attack = v;
                else if (stat == "expertise") g.Stats.Expertise = v;
                else g.Stats.Defense = v;
            }
            return g;
        }

        static Match<GearDef> BetterOf(Match<GearDef> a, Match<GearDef> b)
        {
            return b.Item != null && (a.Item == null || b.Score > a.Score + 0.02) ? b : a;
        }

        ScannedCharm ReadCharm(RgbImage game, string where)
        {
            string name = ReadText(game, UiLayout.CharmTipName, "charm");
            var m = catalog.MatchCharm(name);
            var c = new ScannedCharm { OcrName = name, Score = m.Score };
            if (m.Item != null && m.Score >= 0.7) c.Def = m.Item;
            else problems.Add(where + ": couldn't read the charm name (\"" + name + "\").");
            return c;
        }

        int ReadQuantity(RgbImage game, int slot, out bool uncertain)
        {
            int n = Digits.ReadSlotCount(game, local, slot);
            if (n > 0)
            {
                uncertain = false;
                return n;
            }
            // Fall back to OCR on the number strip.
            string t = ReadText(game, UiLayout.GridQuantity(slot), "count");
            var m = Regex.Match(t.Replace('O', '0').Replace('o', '0').Replace('l', '1').Replace('I', '1').Replace('|', '1'), @"\d+");
            if (m.Success && int.TryParse(m.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n > 0 && n < 100000)
            {
                uncertain = false;
                return n;
            }
            uncertain = true;
            return 1;
        }

        /* ---------- the scan ---------- */

        public ScanResult Run()
        {
            var result = new ScanResult();
            problems = result.Problems;
            try
            {
                Report("Finding the game", null);
                Locate(result);
                var game = CaptureGame();
                Debug("gear-tab", game);
                result.CharacterName = CleanName(ReadText(game, UiLayout.CharacterName, "character"));
                Say("Character: " + result.CharacterName);

                var equipped = ScanEquipped();
                if (opt.Bag) ScanBag(result, equipped);
                else result.Gear.AddRange(equipped);
                FixStats(result);
                if (opt.Charms) ScanCharms(result);
                ClickAt(UiLayout.TabGear.Center, opt.PageDelayMs);
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
            result.Class = cls;
            result.ScannedAt = DateTime.UtcNow;
            Report("Done", null);
            return result;
        }

        static string CleanName(string s)
        {
            s = Regex.Replace(s ?? "", @"[^\p{L}\p{N} _\-\.]", "").Trim();
            return s.Length > 40 ? s.Substring(0, 40) : s;
        }

        List<ScannedGear> ScanEquipped()
        {
            var equipped = new List<ScannedGear>();
            string[] slots = UiLayout.EquipmentSlots;
            for (int k = 0; k < 6; k++)
            {
                var p = UiLayout.EquipmentSlot(k).Center;
                Hover(p);
                CheckStop(p);
                var game = CaptureGame();
                if (!GearTipShown(game)) { Debug("equipped-empty-" + slots[k], game); continue; }
                Debug("equipped-" + slots[k], game);
                var g = ReadGear(game, "Equipped " + slots[k], slots[k]);
                if (g.Def == null) { problems.Add("Equipped " + slots[k] + ": couldn't read the item name (\"" + g.OcrName + "\")."); continue; }
                g.Equipped = true;
                equipped.Add(g);
                if (string.IsNullOrEmpty(cls) && g.Score >= 0.85) cls = g.Def.Class;
                progress.Items = equipped.Count;
                Report("Reading equipped gear", g.Def.Name);
            }
            var classes = equipped.GroupBy(g => g.Def.Class).OrderByDescending(x => x.Count()).ToList();
            if (classes.Count > 0) cls = classes[0].Key;
            return equipped;
        }

        /// <summary>
        /// A capture of the grid with the mouse out of the way, and how many slots are filled.
        /// Items are packed from the first slot, so the first empty slot ends the list.
        /// </summary>
        RgbImage PageShot(out int filled)
        {
            ParkMouse();
            surface.Sleep(Math.Max(120, opt.HoverDelayMs / 2));
            var game = CaptureGame();
            filled = UiLayout.SlotsPerPage;
            for (int i = 0; i < UiLayout.SlotsPerPage; i++)
            {
                if (Imaging.SlotEmpty(Region(game, UiLayout.GridIcon(i)))) { filled = i; break; }
            }
            return game;
        }

        /// <summary>Turns the page; false when the page didn't change (it was the last one).</summary>
        bool NextPage(RgbImage before)
        {
            ClickAt(UiLayout.PageRight.Center, opt.PageDelayMs);
            ParkMouse();
            surface.Sleep(Math.Max(120, opt.HoverDelayMs / 2));
            var after = CaptureGame();
            return Imaging.Difference(Region(before, UiLayout.Grid), Region(after, UiLayout.Grid)) > 2.0;
        }

        void ScanBag(ScanResult result, List<ScannedGear> equipped)
        {
            // All owned gear is in the bag pages, the equipped pieces too (with an "E" badge).
            var pendingEquipped = new List<ScannedGear>(equipped);
            int pagesMoved = 0, seen = 0;
            for (int page = 0; page < opt.MaxPages; page++)
            {
                int filled;
                var pageShot = PageShot(out filled);
                Debug(string.Format(CultureInfo.InvariantCulture, "bag-page{0}", page + 1), pageShot);
                for (int i = 0; i < filled; i++)
                {
                    var p = UiLayout.GridSlot(i).Center;
                    Hover(p);
                    CheckStop(p);
                    var game = CaptureGame();
                    if (!GearTipShown(game))
                    {
                        // A lockbox, chest or other item that isn't gear.
                        result.SkippedItems++;
                        Debug(string.Format(CultureInfo.InvariantCulture, "bag-p{0}-s{1}-other", page + 1, i + 1), game);
                        continue;
                    }
                    Debug(string.Format(CultureInfo.InvariantCulture, "bag-p{0}-s{1}", page + 1, i + 1), game);
                    var g = ReadGear(game, string.Format(CultureInfo.InvariantCulture, "Page {0} slot {1}", page + 1, i + 1), null);
                    if (g.Def == null)
                    {
                        result.SkippedItems++;
                        if (g.Score >= 0.45) problems.Add(string.Format(CultureInfo.InvariantCulture, "Page {0} slot {1}: couldn't read the item name (\"{2}\").", page + 1, i + 1, g.OcrName));
                        continue;
                    }
                    seen++;
                    if (Imaging.EquippedBadge(Region(pageShot, UiLayout.GridEquippedBadge(i))))
                    {
                        g.Equipped = true;
                        var eq = pendingEquipped.FirstOrDefault(e => e.Def == g.Def);
                        if (eq != null)
                        {
                            pendingEquipped.Remove(eq);
                            if (!g.Stats.Read) g.Stats = eq.Stats;
                        }
                    }
                    result.Gear.Add(g);
                    progress.Items = seen;
                    Report("Reading gear, page " + (page + 1), g.Def.Name);
                }
                if (filled < UiLayout.SlotsPerPage) break;
                if (!NextPage(pageShot)) break;
                pagesMoved++;
            }
            // Equipped pieces the bag didn't show are still kept.
            foreach (var e in pendingEquipped) result.Gear.Add(e);
            for (int i = 0; i < pagesMoved; i++) ClickAt(UiLayout.PageLeft.Center, 150);
            if (result.SkippedItems > 0) Say(result.SkippedItems + " items that aren't gear were skipped.");
        }

        /// <summary>
        /// Copies of the same item show the same stats, so the value most of them show is used
        /// for all, which irons out a misread digit. (Only exact copies: how the game works the
        /// numbers out for different items isn't known well enough to compare them.)
        /// </summary>
        void FixStats(ScanResult result)
        {
            foreach (var grp in result.Gear.Where(g => g.Def != null && g.Stats.Read).GroupBy(g => g.Def))
            {
                var votes = grp.GroupBy(g => g.Stats).OrderByDescending(x => x.Count()).ToList();
                if (votes.Count < 2 || votes[0].Count() < 2) continue;
                var winner = votes[0].Key;
                foreach (var g in grp)
                {
                    if (g.Stats.Equals(winner)) continue;
                    g.Stats = new GearStats { Attack = winner.Attack, Expertise = winner.Expertise, Defense = winner.Defense, Read = true };
                }
            }
        }

        void ScanCharms(ScanResult result)
        {
            ClickAt(UiLayout.TabCharms.Center, opt.PageDelayMs);
            // Charms socketed in the equipped gear, shown beside the paper doll on this tab.
            string[] slots = UiLayout.EquipmentSlots;
            for (int k = 0; k < 6; k++)
            {
                var owner = result.Gear.FirstOrDefault(g => g.Equipped && g.Def != null && g.Def.Slot == slots[k]);
                for (int j = 0; j < 3; j++)
                {
                    var p = UiLayout.CharmSocket(k, j).Center;
                    Hover(p);
                    CheckStop(p);
                    var game = CaptureGame();
                    if (!CharmTipShown(game)) { Debug(string.Format(CultureInfo.InvariantCulture, "socket-{0}-{1}-empty", slots[k], j + 1), game); continue; }
                    Debug(string.Format(CultureInfo.InvariantCulture, "socket-{0}-{1}", slots[k], j + 1), game);
                    var c = ReadCharm(game, string.Format(CultureInfo.InvariantCulture, "Charm {0} on the {1}", j + 1, slots[k]));
                    if (c.Def != null && owner != null) owner.Charms[j] = c.Def.Key;
                    Report("Reading socketed charms", c.Def != null ? c.Def.Names[0] : c.OcrName);
                }
            }
            // Charms in the bag: one slot per kind, with the stack size printed on it.
            int pagesMoved = 0;
            for (int page = 0; page < opt.MaxPages; page++)
            {
                int filled;
                var pageShot = PageShot(out filled);
                Debug(string.Format(CultureInfo.InvariantCulture, "charms-page{0}", page + 1), pageShot);
                for (int i = 0; i < filled; i++)
                {
                    var p = UiLayout.GridSlot(i).Center;
                    Hover(p);
                    CheckStop(p);
                    var game = CaptureGame();
                    if (!CharmTipShown(game)) { Debug(string.Format(CultureInfo.InvariantCulture, "charms-p{0}-s{1}-none", page + 1, i + 1), game); continue; }
                    Debug(string.Format(CultureInfo.InvariantCulture, "charms-p{0}-s{1}", page + 1, i + 1), game);
                    var c = ReadCharm(game, string.Format(CultureInfo.InvariantCulture, "Charm page {0} slot {1}", page + 1, i + 1));
                    bool uncertain;
                    // The count is read from the page capture: the hover highlight can cover it.
                    c.Count = ReadQuantity(pageShot, i, out uncertain);
                    c.CountUncertain = uncertain;
                    if (uncertain && c.Def != null) problems.Add(string.Format(CultureInfo.InvariantCulture, "Couldn't read how many {0} you have; counted 1.", c.Def.Names[0]));
                    if (c.Def != null)
                    {
                        var same = result.Charms.FirstOrDefault(x => x.Def == c.Def);
                        if (same != null) same.Count += c.Count;
                        else result.Charms.Add(c);
                    }
                    progress.Charms = result.Charms.Count;
                    Report("Reading charms, page " + (page + 1), c.Def != null ? c.Def.Names[0] + " ×" + c.Count : c.OcrName);
                }
                if (filled < UiLayout.SlotsPerPage) break;
                if (!NextPage(pageShot)) break;
                pagesMoved++;
            }
            for (int i = 0; i < pagesMoved; i++) ClickAt(UiLayout.PageLeft.Center, 150);
        }

        /* ---------- for tests: read what one screenshot shows ---------- */

        /// <summary>Reads the gear tooltip in a capture of the whole game area (used by the tests).</summary>
        public ScannedGear ReadGearFrom(RgbImage game, GameGeometry localGeometry)
        {
            local = localGeometry;
            return GearTipShown(game) ? ReadGear(game, "test", null) : null;
        }

        public ScannedCharm ReadCharmFrom(RgbImage game, GameGeometry localGeometry)
        {
            local = localGeometry;
            return CharmTipShown(game) ? ReadCharm(game, "test") : null;
        }

        public int ReadQuantityFrom(RgbImage game, GameGeometry localGeometry, int slot)
        {
            local = localGeometry;
            bool u;
            return ReadQuantity(game, slot, out u);
        }

        public bool EquippedBadgeFrom(RgbImage game, GameGeometry localGeometry, int slot)
        {
            local = localGeometry;
            return EquippedBadge(game, slot);
        }

        public string ReadTextFrom(RgbImage game, GameGeometry localGeometry, RectD r)
        {
            local = localGeometry;
            return ReadText(game, r, "test");
        }
    }
}
