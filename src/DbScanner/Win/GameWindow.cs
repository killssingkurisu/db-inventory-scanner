using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using DbScanner.Core;

namespace DbScanner.Win
{
    /// <summary>A window that might be the game: the Dungeon Blitz: R launcher, the Flash projector or a browser.</summary>
    public sealed class GameWindow
    {
        public IntPtr Handle;
        public string Title = "";
        public string Process = "";
        public int Score;

        public override string ToString()
        {
            string t = Title.Length > 60 ? Title.Substring(0, 57) + "…" : Title;
            return string.IsNullOrEmpty(Process) ? t : t + "  (" + Process + ")";
        }

        static readonly string[] Ours = { "DbScanner" };

        /// <summary>Visible windows with a title, the likeliest game windows first.</summary>
        public static List<GameWindow> List()
        {
            var list = new List<GameWindow>();
            int self = System.Diagnostics.Process.GetCurrentProcess().Id;
            Native.EnumWindows((h, l) =>
            {
                if (!Native.IsWindowVisible(h)) return true;
                string title = Native.WindowTitle(h);
                if (string.IsNullOrWhiteSpace(title)) return true;
                uint pid;
                Native.GetWindowThreadProcessId(h, out pid);
                if (pid == self) return true;
                string proc = "";
                try { proc = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch (ArgumentException) { } catch (InvalidOperationException) { }
                if (Ours.Contains(proc)) return true;
                var w = new GameWindow { Handle = h, Title = title, Process = proc };
                string t = title.ToLowerInvariant(), p = proc.ToLowerInvariant();
                if (t.Contains("dungeon blitz")) w.Score += 10;
                if (p.Contains("dungeon") || p.Contains("blitz")) w.Score += 8;
                if (t.Contains("flash player") || p.StartsWith("flashplayer")) w.Score += 6;
                if (p == "chrome" || p == "msedge" || p == "firefox" || p == "opera" || p == "brave") w.Score += 1;
                list.Add(w);
                return true;
            }, IntPtr.Zero);
            return list.OrderByDescending(w => w.Score).ThenBy(w => w.Title).ToList();
        }

        public bool Exists { get { return Native.IsWindow(Handle); } }

        /// <summary>The client area in screen pixels.</summary>
        public RectI ClientArea()
        {
            Native.RECT r;
            if (!Native.GetClientRect(Handle, out r)) return new RectI(0, 0, 0, 0);
            var p = new Native.POINT();
            Native.ClientToScreen(Handle, ref p);
            return new RectI(p.X, p.Y, r.Right - r.Left, r.Bottom - r.Top);
        }

        /// <summary>The window's display scaling as a factor (1.5 at 150%); 1 when Windows can't say.</summary>
        public double DpiScale()
        {
            try
            {
                uint dpi = Native.GetDpiForWindow(Handle);
                return dpi >= 48 ? dpi / 96.0 : 1.0;
            }
            catch (EntryPointNotFoundException) { return 1.0; } // before Windows 10 1607
        }

        /// <summary>Restores and brings the window to the front.</summary>
        public void Activate()
        {
            if (Native.IsIconic(Handle)) Native.ShowWindow(Handle, Native.SW_RESTORE);
            // Windows only lets the foreground app hand focus over; a tap of Alt counts as input.
            Native.keybd_event(Native.VK_MENU, 0, 0, UIntPtr.Zero);
            Native.SetForegroundWindow(Handle);
            Native.keybd_event(Native.VK_MENU, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
            Thread.Sleep(250);
        }
    }

    /// <summary>The game on the real screen: screen capture and mouse input with SendInput.</summary>
    public sealed class ScreenSurface : IGameSurface
    {
        readonly GameWindow window;

        public ScreenSurface(GameWindow w) { window = w; }

        public RectI ClientArea() { return window.ClientArea(); }

        public RgbImage Capture(RectI r)
        {
            if (r.W <= 0 || r.H <= 0) return new RgbImage(0, 0);
            using (var bmp = new Bitmap(r.W, r.H, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                    g.CopyFromScreen(r.X, r.Y, 0, 0, new Size(r.W, r.H), CopyPixelOperation.SourceCopy);
                return FromBitmap(bmp);
            }
        }

        public static RgbImage FromBitmap(Bitmap bmp)
        {
            var img = new RgbImage(bmp.Width, bmp.Height);
            var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < bmp.Height; y++)
                    Marshal.Copy(data.Scan0 + y * data.Stride, img.Pixels, y * bmp.Width, bmp.Width);
            }
            finally { bmp.UnlockBits(data); }
            for (int i = 0; i < img.Pixels.Length; i++) img.Pixels[i] &= 0xFFFFFF;
            return img;
        }

        public static Bitmap ToBitmap(RgbImage img)
        {
            var bmp = new Bitmap(Math.Max(1, img.Width), Math.Max(1, img.Height), PixelFormat.Format32bppArgb);
            if (img.Width == 0 || img.Height == 0) return bmp;
            var data = bmp.LockBits(new Rectangle(0, 0, img.Width, img.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                var row = new int[img.Width];
                for (int y = 0; y < img.Height; y++)
                {
                    for (int x = 0; x < img.Width; x++) row[x] = unchecked((int)0xFF000000) | img.Pixels[y * img.Width + x];
                    Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, img.Width);
                }
            }
            finally { bmp.UnlockBits(data); }
            return bmp;
        }

        static Native.INPUT Mouse(uint flags, int x, int y)
        {
            int vx = Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN), vy = Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN);
            int vw = Math.Max(1, Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN)), vh = Math.Max(1, Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN));
            var input = new Native.INPUT { type = Native.INPUT_MOUSE };
            input.u.mi.dx = (int)Math.Round((x - vx) * 65535.0 / (vw - 1));
            input.u.mi.dy = (int)Math.Round((y - vy) * 65535.0 / (vh - 1));
            input.u.mi.dwFlags = flags | Native.MOUSEEVENTF_ABSOLUTE | Native.MOUSEEVENTF_VIRTUALDESK;
            return input;
        }

        public void MoveTo(int x, int y)
        {
            var inputs = new[] { Mouse(Native.MOUSEEVENTF_MOVE, x, y) };
            Native.SendInput(1, inputs, Marshal.SizeOf(typeof(Native.INPUT)));
        }

        public void Click(int x, int y)
        {
            MoveTo(x, y);
            Thread.Sleep(40);
            var inputs = new[] { Mouse(Native.MOUSEEVENTF_LEFTDOWN, x, y), Mouse(Native.MOUSEEVENTF_LEFTUP, x, y) };
            Native.SendInput(2, inputs, Marshal.SizeOf(typeof(Native.INPUT)));
        }

        public void Sleep(int ms) { if (ms > 0) Thread.Sleep(ms); }

        public double DpiScale() { return window.DpiScale(); }

        public bool UserMovedMouse(int expectedX, int expectedY)
        {
            Native.POINT p;
            if (!Native.GetCursorPos(out p)) return false;
            int dx = p.X - expectedX, dy = p.Y - expectedY;
            return dx * dx + dy * dy > 30 * 30;
        }
    }
}
