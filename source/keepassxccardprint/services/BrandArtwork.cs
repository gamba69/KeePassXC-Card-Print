using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace KeePassXCCardPrint.Services;

/// <summary>
/// Selects an original, unscaled ICO frame for the client-area brandmark.
/// The image is displayed at one native source pixel per device pixel;
/// its DIP dimensions change slightly if no exact physical size exists.
/// The window/taskbar icon is intentionally handled separately and remains light.
/// </summary>
internal static class BrandArtwork
{
    private const string AssetRoot = "avares://KeePassXCCardPrint/assets/";
    private static readonly int[] NativeSizes = { 24, 32, 40, 48, 64, 96, 128, 256 };
    private static readonly Dictionary<(bool White, int Pixels), Bitmap> CachedFrames = new();

    internal static void Apply(Image image, bool white, double renderScaling, int preferredDip)
    {
        var scale = double.IsFinite(renderScaling) && renderScaling > 0 ? renderScaling : 1.0;
        double target = preferredDip * scale;
        int nativePixels = NativeSizes[0];
        foreach (int candidate in NativeSizes)
        {
            double distance = Math.Abs(candidate - target);
            double bestDistance = Math.Abs(nativePixels - target);
            // At an exact tie, use the smaller frame to avoid unexpectedly enlarged artwork.
            if (distance < bestDistance || (distance == bestDistance && candidate < nativePixels))
                nativePixels = candidate;
        }

        var key = (white, nativePixels);
        if (!CachedFrames.TryGetValue(key, out var frame))
        {
            var shade = white ? "white" : "black";
            using var stream = AssetLoader.Open(new Uri($"{AssetRoot}CardPrint_{shade}_{nativePixels}.png"));
            frame = new Bitmap(stream);
            CachedFrames.Add(key, frame);
        }

        // A native-pixel-sized image never has to be resampled by the UI renderer.
        // Width/Height are DIP; a device pixel equals 1 / RenderScaling DIP.
        var devicePixelMatchedDip = nativePixels / scale;
        if (!ReferenceEquals(image.Source, frame)) image.Source = frame;
        if (Math.Abs(image.Width - devicePixelMatchedDip) > 0.001)
            image.Width = devicePixelMatchedDip;
        if (Math.Abs(image.Height - devicePixelMatchedDip) > 0.001)
            image.Height = devicePixelMatchedDip;
    }
}
