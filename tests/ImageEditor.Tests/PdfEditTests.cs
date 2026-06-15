using ImageEditor.Core;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace ImageEditor.Tests;

public class PdfEditTests : IDisposable
{
    private readonly string _dir;

    public PdfEditTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "PdfEditTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* 무시 */ }
    }

    private string Path2(string n) => Path.Combine(_dir, n);

    private string CreateBasePdf(string name, int pages = 1)
    {
        var path = Path2(name);
        using var doc = new PdfDocument();
        for (var i = 0; i < pages; i++)
        {
            var page = doc.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawRectangle(XBrushes.WhiteSmoke, new XRect(0, 0, page.Width.Point, page.Height.Point));
        }
        doc.Save(path);
        return path;
    }

    private string CreatePng(string name, int w, int h)
    {
        var path = Path2(name);
        using var img = new Image<Rgba32>(w, h, new Rgba32(0, 120, 220, 255));
        img.Save(path);
        return path;
    }

    [Fact]
    public void AddTextAndImage_ThenSave_ProducesValidPdf()
    {
        var pdf = CreateBasePdf("base.pdf");
        var sig = CreatePng("sig.png", 120, 60);
        var output = Path2("edited.pdf");

        using (var session = PdfEditSession.Load(pdf))
        {
            Assert.True(session.Fonts.HasAnyFont, "기본 폰트를 찾지 못했습니다(시스템에 폰트 필요).");
            session.AddText(0, "서명 확인 OK", 60, 60, null, 24, "#FF0000");
            session.AddImage(0, sig, 60, 120, 100);
            session.Save(output);
        }

        Assert.Equal(1, PdfService.GetPageCount(output));

        // 결과가 렌더링 가능한 정상 PDF 인지 확인
        var page = PdfRenderer.Render(PdfRenderer.LoadBytes(output), 0);
        Assert.True(page.Png.Length > 0);
    }

    [Fact]
    public void FromBytes_RestoresSession()
    {
        var pdf = CreateBasePdf("base.pdf", pages: 3);
        using var session = PdfEditSession.Load(pdf);
        var snapshot = session.ToBytes();

        using var restored = PdfEditSession.FromBytes(snapshot);
        Assert.Equal(3, restored.PageCount);
    }

    [Fact]
    public void RenderPage_ReflectsPageSize()
    {
        var pdf = CreateBasePdf("base.pdf");
        using var session = PdfEditSession.Load(pdf);

        var (wPt, hPt) = session.PageSize(0);
        var page = session.RenderPage(0, scaling: 1.5);

        Assert.Equal(1, session.PageCount);
        Assert.InRange(page.PageWidthPt, wPt - 1, wPt + 1);
        Assert.True(page.PixelWidth > wPt); // 1.5배 렌더라 픽셀이 더 큼
    }
}
