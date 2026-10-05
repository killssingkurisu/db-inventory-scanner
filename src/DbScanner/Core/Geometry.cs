using System;

namespace DbScanner.Core
{
    public struct PointD
    {
        public double X, Y;
        public PointD(double x, double y) { X = x; Y = y; }
        public override string ToString() { return string.Format("({0:0.#}, {1:0.#})", X, Y); }
    }

    public struct RectD
    {
        public double X0, Y0, X1, Y1;
        public RectD(double x0, double y0, double x1, double y1) { X0 = x0; Y0 = y0; X1 = x1; Y1 = y1; }
        public double Width { get { return X1 - X0; } }
        public double Height { get { return Y1 - Y0; } }
        public PointD Center { get { return new PointD((X0 + X1) / 2, (Y0 + Y1) / 2); } }
        public RectD Inflate(double d) { return new RectD(X0 - d, Y0 - d, X1 + d, Y1 + d); }
        public RectD Offset(double dx, double dy) { return new RectD(X0 + dx, Y0 + dy, X1 + dx, Y1 + dy); }
        public override string ToString() { return string.Format("[{0:0.#}, {1:0.#}, {2:0.#}, {3:0.#}]", X0, Y0, X1, Y1); }
    }

    public struct RectI
    {
        public int X, Y, W, H;
        public RectI(int x, int y, int w, int h) { X = x; Y = y; W = w; H = h; }
        public int Right { get { return X + W; } }
        public int Bottom { get { return Y + H; } }
        public override string ToString() { return string.Format("{0},{1} {2}x{3}", X, Y, W, H); }
    }

    /// <summary>
    /// Where the game is drawn inside a window, and how big.
    ///
    /// Dungeon Blitz lays itself out in a 1152×768 space and scales that to fit the Flash area
    /// (Main.method_561 in the client): the scale leaves a 31 px border on the tighter side, is
    /// rounded so the drawn width is a multiple of 6 px, stays between 0.125 and 1.25, and the
    /// game is centred. Game coordinates in UiLayout map to screen pixels with Origin + Scale × point.
    /// </summary>
    public sealed class GameGeometry
    {
        public const double Width = 1152;
        public const double Height = 768;
        const double Pad = 31;

        public double Scale;
        public double OriginX;
        public double OriginY;
        public string Source = "";

        /// <summary>The game's own layout for a Flash area of w×h pixels whose top-left is at (left, top).</summary>
        public static GameGeometry ForFlashArea(int left, int top, int w, int h)
        {
            double s;
            if (w < h * (Width / Height)) s = w / (Width + Pad * 2);
            else s = h / (Height + Pad * 2);
            double drawn = Math.Floor(Width * s / 6) * 6;
            s = drawn / Width;
            if (s > 1.25) s = 1.25;
            if (s < 0.125) s = 0.125;
            return new GameGeometry
            {
                Scale = s,
                OriginX = left + Math.Floor((w - Width * s) * 0.5),
                OriginY = top + Math.Floor((h - Height * s) * 0.5),
                Source = "window size"
            };
        }

        /// <summary>Geometry from the game's drawn rectangle, found on screen.</summary>
        public static GameGeometry ForDrawnRect(RectI r)
        {
            // The drawn width is a multiple of 6 px, so snap to that.
            double drawn = Math.Round(r.W / 6.0) * 6;
            double s = drawn / Width;
            return new GameGeometry { Scale = s, OriginX = r.X, OriginY = r.Y, Source = "found on screen" };
        }

        public PointD ToScreen(PointD p) { return new PointD(OriginX + p.X * Scale, OriginY + p.Y * Scale); }

        public PointD ToScreen(double x, double y) { return ToScreen(new PointD(x, y)); }

        public RectI ToScreen(RectD r)
        {
            int x0 = (int)Math.Floor(OriginX + r.X0 * Scale);
            int y0 = (int)Math.Floor(OriginY + r.Y0 * Scale);
            int x1 = (int)Math.Ceiling(OriginX + r.X1 * Scale);
            int y1 = (int)Math.Ceiling(OriginY + r.Y1 * Scale);
            return new RectI(x0, y0, x1 - x0, y1 - y0);
        }

        public RectI GameRect { get { return ToScreen(new RectD(0, 0, Width, Height)); } }

        public override string ToString()
        {
            return string.Format("scale {0:0.###}, origin ({1:0}, {2:0}) [{3}]", Scale, OriginX, OriginY, Source);
        }
    }
}
