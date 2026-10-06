using System;
using System.Collections.Generic;

namespace DbScanner.Core
{
    /// <summary>A plain 24-bit picture: what the scanner captures and reads.</summary>
    public sealed class RgbImage
    {
        public readonly int Width;
        public readonly int Height;
        /// <summary>Pixels as 0xRRGGBB, row by row.</summary>
        public readonly int[] Pixels;

        public RgbImage(int w, int h)
        {
            Width = w;
            Height = h;
            Pixels = new int[Math.Max(0, w * h)];
        }

        public RgbImage(int w, int h, int[] pixels)
        {
            Width = w;
            Height = h;
            Pixels = pixels;
        }

        public int this[int x, int y]
        {
            get { return Pixels[y * Width + x]; }
            set { Pixels[y * Width + x] = value; }
        }

        public bool Contains(int x, int y) { return x >= 0 && y >= 0 && x < Width && y < Height; }

        public static int R(int c) { return (c >> 16) & 255; }
        public static int G(int c) { return (c >> 8) & 255; }
        public static int B(int c) { return c & 255; }
        public static int Rgb(int r, int g, int b) { return (Clamp(r) << 16) | (Clamp(g) << 8) | Clamp(b); }
        static int Clamp(int v) { return v < 0 ? 0 : v > 255 ? 255 : v; }

        public RgbImage Crop(RectI r)
        {
            int x0 = Math.Max(0, r.X), y0 = Math.Max(0, r.Y);
            int x1 = Math.Min(Width, r.Right), y1 = Math.Min(Height, r.Bottom);
            var o = new RgbImage(Math.Max(0, x1 - x0), Math.Max(0, y1 - y0));
            for (int y = y0; y < y1; y++)
                Array.Copy(Pixels, y * Width + x0, o.Pixels, (y - y0) * o.Width, x1 - x0);
            return o;
        }

        /// <summary>Bilinear resize to an exact size.</summary>
        public RgbImage Resize(int w, int h)
        {
            var o = new RgbImage(w, h);
            if (Width == 0 || Height == 0) return o;
            double sx = (double)Width / w, sy = (double)Height / h;
            for (int y = 0; y < h; y++)
            {
                double fy = (y + 0.5) * sy - 0.5;
                int y0 = (int)Math.Floor(fy);
                double ty = fy - y0;
                int ya = Math.Max(0, Math.Min(Height - 1, y0)), yb = Math.Max(0, Math.Min(Height - 1, y0 + 1));
                for (int x = 0; x < w; x++)
                {
                    double fx = (x + 0.5) * sx - 0.5;
                    int x0 = (int)Math.Floor(fx);
                    double tx = fx - x0;
                    int xa = Math.Max(0, Math.Min(Width - 1, x0)), xb = Math.Max(0, Math.Min(Width - 1, x0 + 1));
                    int c00 = this[xa, ya], c10 = this[xb, ya], c01 = this[xa, yb], c11 = this[xb, yb];
                    o[x, y] = Rgb(
                        (int)Math.Round(Lerp(Lerp(R(c00), R(c10), tx), Lerp(R(c01), R(c11), tx), ty)),
                        (int)Math.Round(Lerp(Lerp(G(c00), G(c10), tx), Lerp(G(c01), G(c11), tx), ty)),
                        (int)Math.Round(Lerp(Lerp(B(c00), B(c10), tx), Lerp(B(c01), B(c11), tx), ty)));
                }
            }
            return o;
        }

        static double Lerp(double a, double b, double t) { return a + (b - a) * t; }

        public static double Distance(int c, int[] rgb)
        {
            int dr = R(c) - rgb[0], dg = G(c) - rgb[1], db = B(c) - rgb[2];
            return Math.Sqrt(dr * dr + dg * dg + db * db);
        }
    }

    public static class Imaging
    {
        /// <summary>Share of the points (game space) that show the tooltip's dark fill.</summary>
        public static double FillShare(RgbImage screen, GameGeometry g, PointD[] points, int[] fill, double tolerance)
        {
            int hit = 0, total = 0;
            foreach (var p in points)
            {
                var s = g.ToScreen(p);
                int x = (int)Math.Round(s.X), y = (int)Math.Round(s.Y);
                if (!screen.Contains(x, y)) continue;
                total++;
                if (RgbImage.Distance(screen[x, y], fill) <= tolerance) hit++;
            }
            return total == 0 ? 0 : (double)hit / total;
        }

        /// <summary>
        /// Turns a line of light game text on a dark panel into dark text on white, enlarged,
        /// which is what OCR reads best. Brightness is taken from the strongest channel so blue,
        /// gold and parchment text all come out solid.
        /// </summary>
        public static RgbImage TextForOcr(RgbImage line, double targetHeight)
        {
            if (line.Width == 0 || line.Height == 0) return line;
            double f = Math.Max(1.0, targetHeight / Math.Max(1, line.Height));
            int w = (int)Math.Round(line.Width * f), h = (int)Math.Round(line.Height * f);
            var big = line.Resize(w, h);
            // Find the background level (most pixels) and the text level (brightest pixels).
            var hist = new int[256];
            foreach (int c in big.Pixels) hist[MaxChannel(c)]++;
            int bg = Percentile(hist, big.Pixels.Length, 0.5);
            int fg = Percentile(hist, big.Pixels.Length, 0.985);
            if (fg - bg < 40) fg = Math.Min(255, bg + 40);
            int pad = (int)Math.Round(h * 0.35);
            var o = new RgbImage(w + pad * 2, h + pad * 2);
            for (int i = 0; i < o.Pixels.Length; i++) o.Pixels[i] = 0xFFFFFF;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int v = MaxChannel(big[x, y]);
                    double t = (double)(v - bg) / (fg - bg);
                    t = t < 0 ? 0 : t > 1 ? 1 : t;
                    t = t < 0.35 ? 0 : t > 0.75 ? 1 : (t - 0.35) / 0.4;
                    int gray = (int)Math.Round(255 * (1 - t));
                    o[x + pad, y + pad] = RgbImage.Rgb(gray, gray, gray);
                }
            return o;
        }

        static int MaxChannel(int c) { return Math.Max(RgbImage.R(c), Math.Max(RgbImage.G(c), RgbImage.B(c))); }

        static int Percentile(int[] hist, int total, double p)
        {
            int target = (int)(total * p), acc = 0;
            for (int v = 0; v < 256; v++)
            {
                acc += hist[v];
                if (acc > target) return v;
            }
            return 255;
        }

        /// <summary>The average colour of the text in a line: its brightest, most coloured pixels.</summary>
        public static int[] TextColor(RgbImage line)
        {
            var px = new List<int>();
            foreach (int c in line.Pixels) if (MaxChannel(c) >= 150) px.Add(c);
            if (px.Count == 0) return null;
            // Keep the brightest half: anti-aliased edges blend into the dark panel.
            px.Sort((p, q) => MaxChannel(q).CompareTo(MaxChannel(p)));
            int n = Math.Max(1, px.Count / 2);
            long sr = 0, sg = 0, sb = 0;
            for (int i = 0; i < n; i++) { sr += RgbImage.R(px[i]); sg += RgbImage.G(px[i]); sb += RgbImage.B(px[i]); }
            return new[] { (int)(sr / n), (int)(sg / n), (int)(sb / n) };
        }

        /// <summary>Legendary names are gold, rare names blue, magic names parchment.</summary>
        public static string RarityFromColor(int[] rgb)
        {
            if (rgb == null) return null;
            double dl = Dist(rgb, UiLayout.Legendary), dr = Dist(rgb, UiLayout.Rare), dm = Dist(rgb, UiLayout.Parchment);
            // Blue text is the only kind whose blue channel beats red by far.
            if (rgb[2] - rgb[0] > 80) return "R";
            if (dl < dm && rgb[1] - rgb[2] > 70) return "L";
            if (dm <= dl && dm <= dr) return "M";
            return dl < dr ? "L" : "R";
        }

        static double Dist(int[] a, int[] b)
        {
            double dr = a[0] - b[0], dg = a[1] - b[1], db = a[2] - b[2];
            return Math.Sqrt(dr * dr + dg * dg + db * db);
        }

        /// <summary>The blue "E" badge on equipped gear: bright blue disc with a white letter.</summary>
        public static bool EquippedBadge(RgbImage badge)
        {
            if (badge.Pixels.Length == 0) return false;
            int blue = 0, white = 0;
            foreach (int c in badge.Pixels)
            {
                int r = RgbImage.R(c), g = RgbImage.G(c), b = RgbImage.B(c);
                if (b > 200 && r < 90 && g > 120) blue++;
                if (r > 225 && g > 225 && b > 225) white++;
            }
            double n = badge.Pixels.Length;
            return blue / n >= 0.12 && white / n >= 0.03;
        }

        /// <summary>An empty inventory slot is a flat dark square, about (62, 63, 58).</summary>
        public static bool SlotEmpty(RgbImage icon)
        {
            if (icon.Pixels.Length == 0) return true;
            double[] mean = new double[3], sq = new double[3];
            foreach (int c in icon.Pixels)
            {
                int[] v = { RgbImage.R(c), RgbImage.G(c), RgbImage.B(c) };
                for (int k = 0; k < 3; k++) { mean[k] += v[k]; sq[k] += v[k] * v[k]; }
            }
            double n = icon.Pixels.Length, std = 0;
            for (int k = 0; k < 3; k++)
            {
                mean[k] /= n;
                std += Math.Sqrt(Math.Max(0, sq[k] / n - mean[k] * mean[k]));
            }
            std /= 3;
            double d = Math.Sqrt(Math.Pow(mean[0] - 62, 2) + Math.Pow(mean[1] - 63, 2) + Math.Pow(mean[2] - 58, 2));
            return std < 11 && d < 12;
        }

        /// <summary>An empty Tome of Power slot is plain parchment, about (215, 195, 155), with no icon on it.</summary>
        public static bool TomeSlotEmpty(RgbImage icon)
        {
            if (icon.Pixels.Length == 0) return true;
            double[] mean = new double[3], sq = new double[3];
            foreach (int c in icon.Pixels)
            {
                int[] v = { RgbImage.R(c), RgbImage.G(c), RgbImage.B(c) };
                for (int k = 0; k < 3; k++) { mean[k] += v[k]; sq[k] += v[k] * v[k]; }
            }
            double n = icon.Pixels.Length, std = 0;
            for (int k = 0; k < 3; k++)
            {
                mean[k] /= n;
                std += Math.Sqrt(Math.Max(0, sq[k] / n - mean[k] * mean[k]));
            }
            std /= 3;
            double d = Math.Sqrt(Math.Pow(mean[0] - 215, 2) + Math.Pow(mean[1] - 195, 2) + Math.Pow(mean[2] - 155, 2));
            return std < 14 && d < 40;
        }

        /// <summary>Mean absolute difference per channel; 0 means identical.</summary>
        public static double Difference(RgbImage a, RgbImage b)
        {
            if (a.Width != b.Width || a.Height != b.Height || a.Pixels.Length == 0) return 255;
            long sum = 0;
            for (int i = 0; i < a.Pixels.Length; i++)
            {
                int x = a.Pixels[i], y = b.Pixels[i];
                sum += Math.Abs(RgbImage.R(x) - RgbImage.R(y)) + Math.Abs(RgbImage.G(x) - RgbImage.G(y)) + Math.Abs(RgbImage.B(x) - RgbImage.B(y));
            }
            return sum / (3.0 * a.Pixels.Length);
        }

        /// <summary>
        /// Finds the drawn game inside a window capture: the game fills its area with 0x484955
        /// around the 1152×768 picture, so the picture is the box of everything else.
        /// </summary>
        public static RectI? FindGameRect(RgbImage shot)
        {
            int[] bg = { 0x48, 0x49, 0x55 };
            Func<int, bool> isBg = c => RgbImage.Distance(c, bg) < 10;
            int w = shot.Width, h = shot.Height;
            Func<int, bool> rowHasGame = y =>
            {
                int n = 0;
                for (int x = 0; x < w; x += 2) if (!isBg(shot[x, y])) n++;
                return n > w / 2 * 0.25;
            };
            Func<int, int, int, bool> colHasGame = (x, y0, y1) =>
            {
                int n = 0;
                for (int y = y0; y < y1; y += 2) if (!isBg(shot[x, y])) n++;
                return n > (y1 - y0) / 2 * 0.25;
            };
            int top = 0, bottom = h - 1;
            while (top < h && !rowHasGame(top)) top++;
            while (bottom > top && !rowHasGame(bottom)) bottom--;
            if (bottom - top < 100) return null;
            int left = 0, right = w - 1;
            while (left < w && !colHasGame(left, top, bottom)) left++;
            while (right > left && !colHasGame(right, top, bottom)) right--;
            if (right - left < 150) return null;
            return new RectI(left, top, right - left + 1, bottom - top + 1);
        }
    }
}
