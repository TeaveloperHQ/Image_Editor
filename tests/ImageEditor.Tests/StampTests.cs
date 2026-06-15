using ImageEditor.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace ImageEditor.Tests;

public class StampTests : IDisposable
{
    private readonly string _dir;

    public StampTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "StampTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* 무시 */ }
    }

    [Fact]
    public void CreateTextStamp_TransparentBackground_WithGlyphs()
    {
        var family = new FontCatalog().Resolve(null);

        var png = StampMaker.CreateTextStampPng("홍길동", family, "#D00000", StampShape.Circle, 320);

        using var img = Image.Load<Rgba32>(png);
        Assert.Equal(320, img.Width);
        Assert.Equal(320, img.Height);

        // 모서리는 (원형 도장 밖이라) 투명, 어딘가에는 불투명 픽셀이 있어야 함
        Assert.Equal(0, img[2, 2].A);
        var hasOpaque = false;
        for (var y = 0; y < img.Height && !hasOpaque; y += 3)
            for (var x = 0; x < img.Width; x += 3)
                if (img[x, y].A > 200) { hasOpaque = true; break; }
        Assert.True(hasOpaque, "글자/테두리 픽셀이 있어야 합니다.");
    }

    [Fact]
    public void CreateTextStamp_Square_Hanja_Works()
    {
        var family = new FontCatalog().Resolve(null);
        var png = StampMaker.CreateTextStampPng("印", family, "#C00000", StampShape.Square, 200);
        Assert.True(png.Length > 0);
    }

    [Fact]
    public void MakeTransparent_RemovesWhite_KeepsInk()
    {
        // 흰 배경 + 가운데 빨간 사각형
        var scan = Path.Combine(_dir, "scan.png");
        using (var img = new Image<Rgba32>(100, 100, new Rgba32(255, 255, 255, 255)))
        {
            for (var y = 35; y < 65; y++)
                for (var x = 35; x < 65; x++)
                    img[x, y] = new Rgba32(200, 20, 20, 255);
            img.Save(scan);
        }

        var png = StampMaker.MakeTransparentPng(scan, whiteThreshold: 60);

        using var outImg = Image.Load<Rgba32>(png);
        Assert.Equal(0, outImg[5, 5].A);        // 흰 배경 → 투명
        Assert.True(outImg[50, 50].A > 200);    // 빨간 잉크 → 유지
    }

    [Fact]
    public void MakeTransparent_Recolor_ChangesInkColor()
    {
        var scan = Path.Combine(_dir, "scan.png");
        using (var img = new Image<Rgba32>(40, 40, new Rgba32(255, 255, 255, 255)))
        {
            for (var y = 10; y < 30; y++)
                for (var x = 10; x < 30; x++)
                    img[x, y] = new Rgba32(30, 30, 30, 255); // 검은 잉크
            img.Save(scan);
        }

        var png = StampMaker.MakeTransparentPng(scan, whiteThreshold: 60, recolorHex: "#0000FF");

        using var outImg = Image.Load<Rgba32>(png);
        var p = outImg[20, 20];
        Assert.True(p.A > 200);
        Assert.True(p.B > 200 && p.R < 60 && p.G < 60); // 파랑으로 통일
    }
}
