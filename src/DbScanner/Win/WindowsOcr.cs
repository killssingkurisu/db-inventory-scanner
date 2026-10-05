using System;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using DbScanner.Core;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace DbScanner.Win
{
    /// <summary>
    /// The text recognition built into Windows 10 and 11 (Windows.Media.Ocr): nothing to
    /// download, and it reads the game's rounded UI font well once the text is enlarged.
    /// </summary>
    public sealed class WindowsOcr : IOcr
    {
        readonly OcrEngine engine;
        public readonly string LanguageName;

        WindowsOcr(OcrEngine e)
        {
            engine = e;
            LanguageName = e.RecognizerLanguage != null ? e.RecognizerLanguage.DisplayName : "?";
        }

        /// <summary>English if installed, otherwise the first OCR language of the user's profile.</summary>
        public static WindowsOcr Create(out string problem)
        {
            problem = null;
            OcrEngine e = null;
            try
            {
                foreach (string tag in new[] { "en-US", "en-GB", "en" })
                {
                    var lang = new Language(tag);
                    if (OcrEngine.IsLanguageSupported(lang)) { e = OcrEngine.TryCreateFromLanguage(lang); if (e != null) break; }
                }
                if (e == null) e = OcrEngine.TryCreateFromUserProfileLanguages();
            }
            catch (Exception ex)
            {
                problem = "Windows text recognition isn't available here (" + ex.Message + "). It needs Windows 10 or 11.";
                return null;
            }
            if (e == null)
            {
                problem = "Windows text recognition has no language installed. In Settings › Time & language › Language, add English (United States) and let it install its optical character recognition part.";
                return null;
            }
            return new WindowsOcr(e);
        }

        public string Read(RgbImage line)
        {
            if (line.Width == 0 || line.Height == 0) return "";
            int w = line.Width, h = line.Height;
            if (w > OcrEngine.MaxImageDimension || h > OcrEngine.MaxImageDimension)
            {
                double f = Math.Min((double)OcrEngine.MaxImageDimension / w, (double)OcrEngine.MaxImageDimension / h);
                line = line.Resize(Math.Max(1, (int)(w * f)), Math.Max(1, (int)(h * f)));
                w = line.Width;
                h = line.Height;
            }
            var bytes = new byte[w * h * 4];
            for (int i = 0; i < line.Pixels.Length; i++)
            {
                int c = line.Pixels[i];
                bytes[i * 4] = (byte)(c & 255);
                bytes[i * 4 + 1] = (byte)((c >> 8) & 255);
                bytes[i * 4 + 2] = (byte)((c >> 16) & 255);
                bytes[i * 4 + 3] = 255;
            }
            using (var bitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, w, h, BitmapAlphaMode.Premultiplied))
            {
                bitmap.CopyFromBuffer(bytes.AsBuffer());
                OcrResult result = engine.RecognizeAsync(bitmap).AsTask().GetAwaiter().GetResult();
                if (result == null || result.Lines == null) return "";
                return string.Join(" ", result.Lines.Select(l => l.Text)).Trim();
            }
        }
    }
}
