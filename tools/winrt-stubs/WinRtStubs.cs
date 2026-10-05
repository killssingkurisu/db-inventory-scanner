// Just enough of the Windows Runtime surface for WindowsOcr.cs to type-check with Mono on
// Linux (tools/check-build.sh). The real build on Windows uses Microsoft.Windows.SDK.Contracts.
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Windows.Foundation { public interface IAsyncOperation<T> { } }
namespace Windows.Storage.Streams { public interface IBuffer { } }
namespace Windows.Globalization
{
    public sealed class Language { public Language(string tag) { } public string DisplayName { get { return ""; } } }
}
namespace Windows.Graphics.Imaging
{
    public enum BitmapPixelFormat { Bgra8 = 87 }
    public enum BitmapAlphaMode { Premultiplied = 0 }
    public sealed class SoftwareBitmap : System.IDisposable
    {
        public SoftwareBitmap(BitmapPixelFormat f, int w, int h, BitmapAlphaMode a) { }
        public void CopyFromBuffer(Windows.Storage.Streams.IBuffer b) { }
        public void Dispose() { }
    }
}
namespace Windows.Media.Ocr
{
    public sealed class OcrEngine
    {
        public static uint MaxImageDimension { get { return 0; } }
        public Windows.Globalization.Language RecognizerLanguage { get { return null; } }
        public static bool IsLanguageSupported(Windows.Globalization.Language l) { return false; }
        public static OcrEngine TryCreateFromLanguage(Windows.Globalization.Language l) { return null; }
        public static OcrEngine TryCreateFromUserProfileLanguages() { return null; }
        public Windows.Foundation.IAsyncOperation<OcrResult> RecognizeAsync(Windows.Graphics.Imaging.SoftwareBitmap b) { return null; }
    }
    public sealed class OcrResult { public IReadOnlyList<OcrLine> Lines { get { return null; } } }
    public sealed class OcrLine { public string Text { get { return ""; } } }
}
namespace System
{
    public static class WindowsRuntimeSystemExtensions
    {
        public static Task<T> AsTask<T>(this global::Windows.Foundation.IAsyncOperation<T> op) { return null; }
    }
}
namespace System.Runtime.InteropServices.WindowsRuntime
{
    public static class WindowsRuntimeBufferExtensions
    {
        public static global::Windows.Storage.Streams.IBuffer AsBuffer(this byte[] a) { return null; }
    }
}
