using ImageEditor.Core;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace ImageEditor.Tests;

public class PdfRenderTests : IDisposable
{
    private readonly string _dir;

    public PdfRenderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "PdfRenderTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* 무시 */ }
    }

    private byte[] CreatePdfBytes()
    {
        using var doc = new PdfDocument();
        var page = doc.AddPage(); // 기본 A4
        using (var gfx = XGraphics.FromPdfPage(page))
            gfx.DrawRectangle(XBrushes.SteelBlue, new XRect(50, 50, 200, 150));
        using var ms = new MemoryStream();
        doc.Save(ms);
        return ms.ToArray();
    }

    [Fact]
    public void Render_ProducesNonEmptyImage_WithExpectedDpi()
    {
        var pdf = CreatePdfBytes();

        var page = PdfRenderer.Render(pdf, 0, scaling: 2.0);

        Assert.True(page.Png.Length > 0);
        Assert.True(page.PixelWidth > 0 && page.PixelHeight > 0);
        // A4 = 595 x 842 pt → 2배 렌더 시 ≈ 1190 x 1684 px
        Assert.InRange(page.PixelWidth, 1150, 1230);
        Assert.InRange(page.PageWidthPt, 590, 600);

        // PNG 로 디코딩 가능해야 함
        using var img = Image.Load<Rgba32>(page.Png);
        Assert.Equal(page.PixelWidth, img.Width);
    }
}
