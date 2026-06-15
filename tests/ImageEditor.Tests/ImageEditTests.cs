using ImageEditor.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace ImageEditor.Tests;

public class ImageEditTests : IDisposable
{
    private readonly string _dir;

    public ImageEditTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ImageEditTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* 무시 */ }
    }

    private string Make(string name, int w, int h)
    {
        var path = Path.Combine(_dir, name);
        using var img = new Image<Rgba32>(w, h, new Rgba32(10, 20, 30, 255));
        img.Save(path);
        return path;
    }

    [Fact]
    public void Crop_ReducesToRequestedRegion()
    {
        using var s = ImageEditSession.Load(Make("a.png", 200, 100));
        s.Crop(new Rectangle(10, 10, 50, 40));
        Assert.Equal(50, s.Width);
        Assert.Equal(40, s.Height);
    }

    [Fact]
    public void Crop_ClampsToBounds()
    {
        using var s = ImageEditSession.Load(Make("a.png", 100, 100));
        s.Crop(new Rectangle(80, 80, 50, 50)); // 범위 초과 → 20x20 으로 클램프
        Assert.Equal(20, s.Width);
        Assert.Equal(20, s.Height);
    }

    [Fact]
    public void Crop_OutsideBounds_Throws()
    {
        using var s = ImageEditSession.Load(Make("a.png", 100, 100));
        Assert.Throws<ArgumentException>(() => s.Crop(new Rectangle(200, 200, 10, 10)));
    }

    [Fact]
    public void RotateRight_SwapsWidthHeight()
    {
        using var s = ImageEditSession.Load(Make("a.png", 200, 100));
        s.RotateRight();
        Assert.Equal(100, s.Width);
        Assert.Equal(200, s.Height);
    }

    [Fact]
    public void AddText_DoesNotThrow_AndKeepsSize()
    {
        var catalog = new FontCatalog();
        var family = catalog.Resolve(null);
        using var s = ImageEditSession.Load(Make("a.png", 300, 200));
        s.AddText("안녕 Hello", family, 24, Color.White, 20, 20);
        Assert.Equal(300, s.Width);
        Assert.Equal(200, s.Height);
    }

    [Fact]
    public void AddImage_OverlaysWithoutChangingBaseSize()
    {
        var overlay = Make("overlay.png", 40, 40);
        using var s = ImageEditSession.Load(Make("base.png", 300, 200));
        s.AddImage(overlay, 10, 10, width: 50, height: 50);
        Assert.Equal(300, s.Width);
        Assert.Equal(200, s.Height);
    }

    [Fact]
    public void FromBytes_RestoresSession()
    {
        using var s = ImageEditSession.Load(Make("a.png", 120, 80));
        s.RotateRight(); // 80x120
        var snapshot = s.ToPngBytes();

        using var restored = ImageEditSession.FromBytes(snapshot);
        Assert.Equal(s.Width, restored.Width);
        Assert.Equal(s.Height, restored.Height);
    }

    [Fact]
    public void Save_WritesFile()
    {
        using var s = ImageEditSession.Load(Make("a.png", 100, 100));
        s.RotateLeft();
        var outPath = Path.Combine(_dir, "out.png");
        s.Save(outPath);
        Assert.True(File.Exists(outPath));
    }

    [Fact]
    public void FontCatalog_ListsFamilies_AndResolves()
    {
        var catalog = new FontCatalog();
        Assert.NotEmpty(catalog.FamilyNames());
        Assert.NotNull(catalog.Resolve(null).Name);
    }
}
