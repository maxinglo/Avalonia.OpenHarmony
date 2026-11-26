using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.OpenGL.Surfaces;
using Avalonia.Platform;
using Avalonia.Platform.Surfaces;

namespace Avalonia.OpenHarmony;

// All windows share a compositor so an owned window keeps its visual tree when its surface changes.
// Skia's GL backend only considers GL surfaces ready. Forward framebuffer routes to its software backend.
internal sealed class OpenHarmonyWindowedSkia(IPlatformRenderInterface skia) : IPlatformRenderInterface
{
    public IPlatformRenderInterfaceContext CreateBackendContext(IPlatformGraphicsContext? context) =>
        context is OpenHarmonyEgl.WindowedContext windowed ? windowed.CreateBackend(skia) : skia.CreateBackendContext(context);

    internal sealed class Backend(IPlatformRenderInterfaceContext gpu, IPlatformRenderInterfaceContext software)
        : IPlatformRenderInterfaceContext
    {
        public bool IsLost => gpu.IsLost;
        public IReadOnlyDictionary<Type, object> PublicFeatures => gpu.PublicFeatures;
        public PixelSize? MaxOffscreenRenderTargetPixelSize => gpu.MaxOffscreenRenderTargetPixelSize;
        public object? TryGetFeature(Type featureType) => gpu.TryGetFeature(featureType);

        public IRenderTarget CreateRenderTarget(IEnumerable<IPlatformRenderSurface> surfaces) =>
            ChooseBackend(surfaces).CreateRenderTarget(surfaces);

        public bool IsReadyToCreateRenderTarget(IEnumerable<IPlatformRenderSurface> surfaces) =>
            ChooseBackend(surfaces).IsReadyToCreateRenderTarget(surfaces);

        private IPlatformRenderInterfaceContext ChooseBackend(IEnumerable<IPlatformRenderSurface> surfaces) =>
            surfaces.Any(surface => surface is IGlPlatformSurface) ? gpu : software;

        public IDrawingContextLayerImpl CreateOffscreenRenderTarget(PixelSize size, Vector scaling,
            bool antialiasing) => gpu.CreateOffscreenRenderTarget(size, scaling, antialiasing);

        public void Dispose()
        {
            try { gpu.Dispose(); }
            finally { software.Dispose(); }
        }
    }

    public bool SupportsIndividualRoundRects => skia.SupportsIndividualRoundRects;
    public AlphaFormat DefaultAlphaFormat => skia.DefaultAlphaFormat;
    public PixelFormat DefaultPixelFormat => skia.DefaultPixelFormat;
    public bool SupportsRegions => skia.SupportsRegions;
    public bool IsSupportedBitmapPixelFormat(PixelFormat format) => skia.IsSupportedBitmapPixelFormat(format);
    public IPlatformRenderInterfaceRegion CreateRegion() => skia.CreateRegion();
    public IGeometryImpl CreateEllipseGeometry(Rect rect) => skia.CreateEllipseGeometry(rect);
    public IGeometryImpl CreateLineGeometry(Point p1, Point p2) => skia.CreateLineGeometry(p1, p2);
    public IGeometryImpl CreateRectangleGeometry(Rect rect) => skia.CreateRectangleGeometry(rect);
    public IStreamGeometryImpl CreateStreamGeometry() => skia.CreateStreamGeometry();
    public IGeometryImpl CreateGeometryGroup(FillRule rule, IReadOnlyList<IGeometryImpl> children) =>
        skia.CreateGeometryGroup(rule, children);
    public IGeometryImpl CreateCombinedGeometry(GeometryCombineMode mode, IGeometryImpl g1, IGeometryImpl g2) =>
        skia.CreateCombinedGeometry(mode, g1, g2);
    public IGeometryImpl BuildGlyphRunGeometry(GlyphRun glyphRun) => skia.BuildGlyphRunGeometry(glyphRun);
    public IRenderTargetBitmapImpl CreateRenderTargetBitmap(PixelSize size, Vector dpi) =>
        skia.CreateRenderTargetBitmap(size, dpi);
    public IWriteableBitmapImpl CreateWriteableBitmap(PixelSize size, Vector dpi, PixelFormat format, AlphaFormat alpha) =>
        skia.CreateWriteableBitmap(size, dpi, format, alpha);
    public IBitmapImpl LoadBitmap(string fileName) => skia.LoadBitmap(fileName);
    public IBitmapImpl LoadBitmap(Stream stream) => skia.LoadBitmap(stream);
    public IBitmapImpl LoadBitmap(PixelFormat format, AlphaFormat alpha, IntPtr data, PixelSize size, Vector dpi, int stride) =>
        skia.LoadBitmap(format, alpha, data, size, dpi, stride);
    public IWriteableBitmapImpl LoadWriteableBitmap(string fileName) => skia.LoadWriteableBitmap(fileName);
    public IWriteableBitmapImpl LoadWriteableBitmap(Stream stream) => skia.LoadWriteableBitmap(stream);
    public IBitmapImpl LoadBitmapToWidth(Stream stream, int width, BitmapInterpolationMode mode) =>
        skia.LoadBitmapToWidth(stream, width, mode);
    public IBitmapImpl LoadBitmapToHeight(Stream stream, int height, BitmapInterpolationMode mode) =>
        skia.LoadBitmapToHeight(stream, height, mode);
    public IWriteableBitmapImpl LoadWriteableBitmapToWidth(Stream stream, int width, BitmapInterpolationMode mode) =>
        skia.LoadWriteableBitmapToWidth(stream, width, mode);
    public IWriteableBitmapImpl LoadWriteableBitmapToHeight(Stream stream, int height, BitmapInterpolationMode mode) =>
        skia.LoadWriteableBitmapToHeight(stream, height, mode);
    public IBitmapImpl ResizeBitmap(IBitmapImpl bitmap, PixelSize size, BitmapInterpolationMode mode) =>
        skia.ResizeBitmap(bitmap, size, mode);
    public IGlyphRunImpl CreateGlyphRun(GlyphTypeface typeface, double size, IReadOnlyList<GlyphInfo> glyphs, Point origin) =>
        skia.CreateGlyphRun(typeface, size, glyphs, origin);
}
