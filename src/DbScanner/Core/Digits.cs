using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DbScanner.Core
{
    /// <summary>
    /// Reads the small stack counts printed on inventory slots ("6", "17"). OCR engines often
    /// miss a lone digit, so this matches each digit's shape against the game's own font
    /// (Helvetica Rounded LT Std Bold, taken from UI_4.swf) instead.
    /// </summary>
    public static class Digits
    {
        const int GW = 10, GH = 14;

        // Coverage of each digit's glyph on a 10×14 grid (hex 0-f), and its width/height.
        static readonly string[] Templates =
        {
            "005bffb50006ffffff602efe66dfe28ff4004ff8cff0000ffcefd0000dfeffc0000cffffb0000bffdfd0000dfdbfe0000efb7ff4004ff71efe66efe105ffffff50005bffb500",
            "0000004efa000004efff0136afffffdfffffffffbddddeffff00000affff00000affff00000affff00000affff00000affff00000affff00000affff00000affff000002cfe7",
            "006bffd8000affffffb06ffc66dff7cfe1002ffcdfa0000ffd4820004ff9000005eff30002bfff50005effc30005ffe600003ffc200000cff9888884ffffffffff8ffffffffa",
            "007cffd8101dffffffd19ffc56eff8bfd1004ffa2620004ff9000058efe20003ffff700001adfff70000006ffd5a10000fffffb0003ffedffa57eff84fffffffb002adffc600",
            "000003ed1000001eff200000afff200006fdff20002fc5ff2000cf35ff2007f805ff202fd005ff20cfb88aff95ffffffffff6bbbbcffb8000005ff20000005ff20000002ee10",
            "09ffffffe30ffffffff73ff98888705ff10000008fd0353000afddfffd50cfffdefff54a81008ffd0000000eff0200000dffcf90004ffeeffa57eff76fffffffb004aefeb500",
            "0029dfea1004ffffffd10dfd528ff56ff4000791afe0000000dfc5dfea20ffefffffe2fffa107ffafff1000dfedff0000cff9ff2000efd2ffc55bff807ffffffb0005bffd810",
            "cffffffffaffffffffff4888889ff8000000bfb0000007fe2000001ff70000009fd0000002ff70000009ff2000001efc0000005ff80000008ff4000000bff00000006f900000",
            "007cffc7000bffffffb06ff933aff6aff1002ffa8ff1003ff82efc66cfe206ffffff604ffe88eff4cff2002ffcffc0000cffefd0000dfe9ffa55aff91dffffffd1018dffd810",
            "018dffb5000bffffff707ffb55cff2dfe0002ff9ffc0000ffdefd0001fffaff7019fff2effffffff03cffe8cfd0001300dfa0950003ff65ff724dfd01dffffff4002aefea300"
        };
        static readonly double[] Aspect = { 0.744, 0.453, 0.753, 0.709, 0.802, 0.718, 0.736, 0.765, 0.736, 0.736 };

        static double[][] grids;

        static double[][] Grids()
        {
            if (grids != null) return grids;
            var g = new double[10][];
            for (int d = 0; d < 10; d++)
            {
                g[d] = new double[GW * GH];
                for (int i = 0; i < GW * GH; i++) g[d][i] = Convert.ToInt32(Templates[d][i].ToString(), 16) / 15.0;
            }
            grids = g;
            return g;
        }

        sealed class Blob
        {
            public int X0 = int.MaxValue, Y0 = int.MaxValue, X1 = -1, Y1 = -1;
            public readonly List<int> Points = new List<int>();
            public int H { get { return Y1 - Y0 + 1; } }
            public int W { get { return X1 - X0 + 1; } }
        }

        /// <summary>
        /// The number on inventory slot i, or -1 when there is none. The count is right-aligned
        /// in the slot's bottom strip, parchment coloured with a dark outline.
        /// </summary>
        public static int ReadSlotCount(RgbImage game, GameGeometry local, int slot)
        {
            RectD s = UiLayout.GridSlot(slot);
            RectI r = local.ToScreen(new RectD(s.X0 + 24, s.Y0 + 33, s.X0 + 51, s.Y0 + 52));
            var img = game.Crop(r);
            double scale = local.Scale;
            double baseline = (s.Y0 + 47.25) * scale + local.OriginY - r.Y;
            double rightEdge = (s.X0 + 47.5) * scale + local.OriginX - r.X;
            string text = Read(img, scale, baseline, rightEdge);
            int n;
            return text.Length > 0 && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : -1;
        }

        /// <summary>Digits right-aligned at rightEdge, sitting on baseline (pixels in img).</summary>
        public static string Read(RgbImage img, double scale, double baseline, double rightEdge)
        {
            int w = img.Width, h = img.Height;
            var mask = new bool[w * h];
            for (int i = 0; i < mask.Length; i++)
            {
                int c = img.Pixels[i];
                int mx = Math.Max(RgbImage.R(c), Math.Max(RgbImage.G(c), RgbImage.B(c)));
                int mn = Math.Min(RgbImage.R(c), Math.Min(RgbImage.G(c), RgbImage.B(c)));
                mask[i] = mx >= 170 && mx - mn <= 75;
            }
            double hExp = 9.4 * scale;
            var blobs = new List<Blob>();
            var seen = new bool[w * h];
            var queue = new Queue<int>();
            for (int start = 0; start < mask.Length; start++)
            {
                if (!mask[start] || seen[start]) continue;
                var b = new Blob();
                seen[start] = true;
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    int p = queue.Dequeue();
                    int px = p % w, py = p / w;
                    b.Points.Add(p);
                    if (px < b.X0) b.X0 = px;
                    if (px > b.X1) b.X1 = px;
                    if (py < b.Y0) b.Y0 = py;
                    if (py > b.Y1) b.Y1 = py;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = px + dx, ny = py + dy;
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                            int q = ny * w + nx;
                            if (mask[q] && !seen[q]) { seen[q] = true; queue.Enqueue(q); }
                        }
                }
                if (b.H >= hExp * 0.6 && b.H <= hExp * 1.35 && Math.Abs(b.Y1 - baseline) <= 2 * scale) blobs.Add(b);
            }
            if (blobs.Count == 0) return "";
            Blob right = blobs[0];
            foreach (var b in blobs) if (b.X1 > right.X1) right = b;
            if (Math.Abs(right.X1 - rightEdge) > 4 * scale) return "";
            var chain = new List<Blob> { right };
            Blob cur = right;
            while (true)
            {
                Blob next = null;
                foreach (var b in blobs)
                {
                    if (b.X1 >= cur.X0 || cur.X0 - b.X1 > 0.6 * hExp || Math.Abs(b.Y1 - cur.Y1) > 1.5 * scale) continue;
                    if (next == null || b.X1 > next.X1) next = b;
                }
                if (next == null) break;
                chain.Insert(0, next);
                cur = next;
            }
            var sb = new StringBuilder();
            foreach (var b in chain) sb.Append(Classify(b, w));
            return sb.ToString();
        }

        static char Classify(Blob b, int stride)
        {
            // Coverage of the blob on the template grid (box filter).
            var cov = new double[GW * GH];
            var pix = new bool[b.W * b.H];
            foreach (int p in b.Points) pix[(p / stride - b.Y0) * b.W + (p % stride - b.X0)] = true;
            for (int gy = 0; gy < GH; gy++)
                for (int gx = 0; gx < GW; gx++)
                {
                    double x0 = (double)gx * b.W / GW, x1 = (double)(gx + 1) * b.W / GW;
                    double y0 = (double)gy * b.H / GH, y1 = (double)(gy + 1) * b.H / GH;
                    double sum = 0, area = 0;
                    for (int y = (int)Math.Floor(y0); y < Math.Ceiling(y1); y++)
                        for (int x = (int)Math.Floor(x0); x < Math.Ceiling(x1); x++)
                        {
                            double ox = Math.Min(x + 1, x1) - Math.Max(x, x0);
                            double oy = Math.Min(y + 1, y1) - Math.Max(y, y0);
                            if (ox <= 0 || oy <= 0) continue;
                            double a = ox * oy;
                            area += a;
                            if (pix[y * b.W + x]) sum += a;
                        }
                    cov[gy * GW + gx] = area > 0 ? sum / area : 0;
                }
            double aspect = (double)b.W / b.H;
            var g = Grids();
            int best = 0;
            double bestScore = double.MaxValue;
            for (int d = 0; d < 10; d++)
            {
                double diff = 0;
                for (int i = 0; i < cov.Length; i++) diff += Math.Abs(cov[i] - g[d][i]);
                double score = diff / cov.Length + 0.5 * Math.Abs(Math.Log(aspect / Aspect[d]));
                if (score < bestScore) { bestScore = score; best = d; }
            }
            return (char)('0' + best);
        }
    }
}
