// Checks for the scanner's core. tests/run.sh runs them with Mono and Tesseract on Linux;
// tests/WinOcrCheck builds them with WINDOWS_OCR defined to use the text recognition built into Windows.
// Screenshots in tests/screens are cut from real captures of the game (1920×1080, launcher window).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using DbScanner.Core;

static class TestRunner
{
    static int pass, fail;

    static void Check(string name, object got, object want)
    {
        bool ok = Equals(got, want) || (got != null && want != null && got.ToString() == want.ToString());
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name + ": got " + (got ?? "null") + ", want " + (want ?? "null"));
        if (ok) pass++; else fail++;
    }

    static RgbImage Load(string path)
    {
        using (var bmp = new Bitmap(path))
        {
            var img = new RgbImage(bmp.Width, bmp.Height);
            var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var buf = new int[bmp.Width * bmp.Height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buf, 0, buf.Length);
            bmp.UnlockBits(data);
            for (int i = 0; i < buf.Length; i++) img.Pixels[i] = buf[i] & 0xFFFFFF;
            return img;
        }
    }

    /// <summary>Stand-in for Windows OCR: Tesseract on the command line.</summary>
    sealed class TesseractOcr : IOcr
    {
        public string Read(RgbImage line)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "dbscan-ocr-" + Guid.NewGuid().ToString("N") + ".png");
            using (var bmp = new Bitmap(line.Width, line.Height, PixelFormat.Format32bppArgb))
            {
                var data = bmp.LockBits(new Rectangle(0, 0, line.Width, line.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                var buf = line.Pixels.Select(p => unchecked((int)0xFF000000) | p).ToArray();
                System.Runtime.InteropServices.Marshal.Copy(buf, 0, data.Scan0, buf.Length);
                bmp.UnlockBits(data);
                bmp.Save(tmp, ImageFormat.Png);
            }
            var psi = new ProcessStartInfo("tesseract", "\"" + tmp + "\" - --psm 7 -l eng")
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
            };
            using (var p = Process.Start(psi))
            {
                string o = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                File.Delete(tmp);
                return o.Trim();
            }
        }
    }

    static int Main(string[] args)
    {
        string root = args.Length > 0 ? args[0] : ".";
        var catalog = Catalog.Load(File.ReadAllText(Path.Combine(root, "data", "catalog.json")));
        Check("catalog gear count", catalog.Gear.Count > 3000, true);
        Check("catalog charm count", catalog.Charms.Count, 94);

        // Geometry: the launcher's client area on a 1920×1080 screen.
        var cands = Scanner.Candidates(new RectI(0, 29, 1920, 991), null);
        Check("scale for 1920×991", Math.Round(cands[0].Scale, 4), 1.1927);
        Check("origin x", cands[0].OriginX, 273.0);
        Check("origin y", cands[0].OriginY, 66.0);
        Check("projector at 1152×768 window", Math.Round(GameGeometry.ForFlashArea(0, 0, 1152, 768).Scale, 4), 0.9219);
        Check("scale capped at 1.25 on a 4K window", GameGeometry.ForFlashArea(0, 0, 3840, 2100).Scale, 1.25);

        // Name matching.
        Check("exact name", catalog.MatchGear("Key to the City", "Rogue", null, "R").Item.Name, "Key to the City");
        Check("OCR noise", catalog.MatchGear("Key to the Clty ‘", null, null, null).Item.Name, "Key to the City");
        Check("cut-off long name", catalog.MatchGear("Sythokhan's Ball of Wic", "Paladin", null, null).Item.Name, "Sythokhan's Ball of Wicked Beads");
        Check("apostrophes and dashes", catalog.MatchGear("Tak Oggs Heavy Leveler", null, null, null).Item.Name, "Tak-Ogg's Heavy Leveler");
        Check("charm name", catalog.MatchCharm("Infinite Sapphire .").Item.Key, "expertise");
        Check("charm rank", catalog.MatchCharm("Radiant Citrine").Item.Key, "attack@7");
        Check("special charm", catalog.MatchCharm("Twilight Sliver").Item.Key, "twilightSliver");
        Check("Turkish charm name", catalog.MatchCharm("Sonsuz Safir").Item.Key, "expertise");

        // Stat lines.
        int v; string stat;
        Check("stat +472 Attack", Scanner.ParseStat("+472 Attack", out v, out stat) && v == 472 && stat == "attack", true);
        Check("stat with comma", Scanner.ParseStat("+1,234 Expertise", out v, out stat) && v == 1234 && stat == "expertise", true);
        Check("stat with O for 0", Scanner.ParseStat("+4O0 Expertise", out v, out stat) && v == 400, true);
        Check("stat Defense", Scanner.ParseStat("+75 Defense", out v, out stat) && stat == "defense", true);
        Check("empty line is no stat", Scanner.ParseStat("BS", out v, out stat), false);
        Check("magic line is no stat", Scanner.ParseStat("+2.3% Movement Speed", out v, out stat), false);

        // JSON.
        var parsed = (Dictionary<string, object>)Json.Parse("{\"a\":[1,2,{\"b\":\"x\\u00e7\"}],\"c\":true,\"d\":null}");
        Check("json array", ((List<object>)parsed["a"]).Count, 3);
        Check("json unicode", ((Dictionary<string, object>)((List<object>)parsed["a"])[2])["b"], "xç");
        Check("json write", Json.Write(new JObject().Add("n", 1).Add("s", "a\"b").Add("l", new List<object> { true, null }), false), "{\"n\":1,\"s\":\"a\\\"b\",\"l\":[true,null]}");

        // Real screenshots.
        string screens = Path.Combine(root, "tests", "screens");
#if WINDOWS_OCR
        string problem;
        var winOcr = DbScanner.Win.WindowsOcr.Create(out problem);
        if (winOcr == null)
        {
            Console.WriteLine("SKIP screenshot checks: " + problem);
            Console.WriteLine(pass + " passed, " + fail + " failed");
            return 2;
        }
        Console.WriteLine("Text recognition: Windows (" + winOcr.LanguageName + ")");
        IOcr ocr = winOcr;
#else
        IOcr ocr = new TesseractOcr();
#endif
        // Debug on, so every piece of recognised text is printed next to the checks.
        var scanner = new Scanner(null, ocr, catalog, new ScanOptions { Debug = true });
        scanner.DebugSink = (name, img, text) => { if (text != null) Console.WriteLine("     ocr " + name + " = " + text); };
        // The fixtures are cut from 1920×1080 captures at (690, 60).
        var g = new GameGeometry { Scale = 1.1927, OriginX = 273 - 690, OriginY = 66 - 60 };

        var gearShot = Load(Path.Combine(screens, "gear-tooltip.png"));
        var item = scanner.ReadGearFrom(gearShot, g);
        Check("gear tooltip found", item != null, true);
        if (item != null)
        {
            Check("gear name", item.Def != null ? item.Def.Name : item.OcrName, "Key to the City");
            Check("gear rarity from colour", item.Rarity, "R");
            Check("gear stats", item.Stats.ToString(), "472/400/0");
            Check("gear focus from catalog", item.Def != null ? item.Def.Focus : "", "Expertise");
        }
        for (int i = 0; i < 8; i++) Check("equipped badge slot " + (i + 1), scanner.EquippedBadgeFrom(gearShot, g, i), i < 6);
        Check("character name", scanner.ReadTextFrom(gearShot, g, UiLayout.CharacterName), "ksq");

        var charmShot = Load(Path.Combine(screens, "charm-tooltip.png"));
        var charm = scanner.ReadCharmFrom(charmShot, g);
        Check("charm tooltip", charm != null && charm.Def != null ? charm.Def.Key : "", "twilightSliver");
        var charmShot2 = Load(Path.Combine(screens, "socket-tooltip.png"));
        var charm2 = scanner.ReadCharmFrom(charmShot2, g);
        Check("socketed charm tooltip", charm2 != null && charm2.Def != null ? charm2.Def.Key : "", "expertise");
        Check("gear tooltip is not taken for a charm tooltip", scanner.ReadGearFrom(charmShot2, g) == null, true);

        int[] counts = { 6, 1, 5, 1, 6, 1, 2, 3, 1 };
        for (int i = 0; i < counts.Length; i++) Check("charm count slot " + (i + 1), Digits.ReadSlotCount(charmShot, g, i), counts[i]);
        Check("empty slot has no count", Digits.ReadSlotCount(charmShot, g, 9), -1);
        Check("chest count 17", Digits.ReadSlotCount(gearShot, g, 26), 17);
        Check("chest count 40", Digits.ReadSlotCount(gearShot, g, 27), 40);
        for (int i = 7; i <= 10; i++)
            Check("slot " + (i + 1) + (i < 9 ? " filled" : " empty"), Imaging.SlotEmpty(charmShot.Crop(g.ToScreen(UiLayout.GridIcon(i)))), i >= 9);
        Check("dark gear item is not empty", Imaging.SlotEmpty(gearShot.Crop(g.ToScreen(UiLayout.GridIcon(16)))), false);

        // The export.
        var res = new ScanResult { CharacterName = "ksq", Class = "Rogue" };
        if (item != null && item.Def != null) res.Gear.Add(item);
        if (charm != null && charm.Def != null) { charm.Count = 6; res.Charms.Add(charm); }
        string json = Json.Write(res.ToJson("test"), true);
        var back = (Dictionary<string, object>)Json.Parse(json);
        Check("export format", back["format"], "dbb-inventory");
        Check("export gear stats", Json.Write(((Dictionary<string, object>)((List<object>)back["gear"])[0])["stats"], false), "{\"attack\":472,\"expertise\":400,\"defense\":0}");

        Console.WriteLine(pass + " passed, " + fail + " failed");
        return fail == 0 ? 0 : 1;
    }
}
