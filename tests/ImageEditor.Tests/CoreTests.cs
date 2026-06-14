using ImageEditor.Core;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace ImageEditor.Tests;

public class CoreTests : IDisposable
{
    private readonly string _dir;

    public CoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ImageEditorTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* 정리 실패 무시 */ }
    }

    private string Path2(string name) => Path.Combine(_dir, name);

    private string CreateImage(string name, int w, int h)
    {
        var path = Path2(name);
        using var img = new Image<Rgba32>(w, h);
        img.Save(path);
        return path;
    }

    private string CreatePdf(string name, int pages)
    {
        var path = Path2(name);
        using var doc = new PdfDocument();
        for (var i = 0; i < pages; i++)
        {
            var page = doc.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            // 폰트 리졸버 없이도 동작하도록 텍스트 대신 사각형을 그립니다.
            gfx.DrawRectangle(XBrushes.LightGray, new XRect(50, 50, 100, 100 + i * 5));
        }
        doc.Save(path);
        return path;
    }

    // ----- 이미지 -----

    [Fact]
    public async Task ResizeToPixels_KeepAspect_FitsInsideBox()
    {
        var input = CreateImage("in.png", 200, 100);
        var output = Path2("out.png");

        await ImageService.ResizeToPixelsAsync(input, output, 100, 100, keepAspectRatio: true);

        using var img = Image.Load(output);
        Assert.Equal(100, img.Width);
        Assert.Equal(50, img.Height); // 2:1 비율 유지
    }

    [Fact]
    public async Task ResizeToPixels_AutoHeight_WhenHeightZero()
    {
        var input = CreateImage("in.png", 400, 200);
        var output = Path2("out.png");

        await ImageService.ResizeToPixelsAsync(input, output, 100, 0);

        using var img = Image.Load(output);
        Assert.Equal(100, img.Width);
        Assert.Equal(50, img.Height);
    }

    [Fact]
    public async Task ResizeByPercent_HalvesDimensions()
    {
        var input = CreateImage("in.png", 300, 200);
        var output = Path2("out.png");

        await ImageService.ResizeByPercentAsync(input, output, 50);

        using var img = Image.Load(output);
        Assert.Equal(150, img.Width);
        Assert.Equal(100, img.Height);
    }

    // ----- PDF -----

    [Fact]
    public void Merge_CombinesPageCounts()
    {
        var a = CreatePdf("a.pdf", 2);
        var b = CreatePdf("b.pdf", 3);
        var output = Path2("merged.pdf");

        PdfService.Merge(new[] { a, b }, output);

        Assert.Equal(5, PdfService.GetPageCount(output));
    }

    [Fact]
    public void SplitToSinglePages_CreatesOneFilePerPage()
    {
        var src = CreatePdf("src.pdf", 4);
        var outDir = Path2("split");

        var files = PdfService.SplitToSinglePages(src, outDir);

        Assert.Equal(4, files.Count);
        Assert.All(files, f => Assert.Equal(1, PdfService.GetPageCount(f)));
    }

    [Fact]
    public void ExtractPages_ByRangeString_KeepsOrderAndCount()
    {
        var src = CreatePdf("src.pdf", 10);
        var output = Path2("extract.pdf");

        PdfService.ExtractPages(src, output, "1-3,5,8");

        Assert.Equal(5, PdfService.GetPageCount(output));
    }

    [Fact]
    public void ResizePages_ChangesPageSizeToA4()
    {
        var src = CreatePdf("src.pdf", 2);
        var output = Path2("resized.pdf");

        PdfService.ResizePages(src, output, PdfSharp.PageSize.A4);

        using var doc = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        Assert.Equal(2, doc.PageCount);
        // A4 = 595 x 842 pt (반올림)
        Assert.Equal(595, Math.Round(doc.Pages[0].Width.Point));
        Assert.Equal(842, Math.Round(doc.Pages[0].Height.Point));
    }

    // ----- PageRange -----

    [Theory]
    [InlineData("1-3", new[] { 1, 2, 3 })]
    [InlineData("1-3,5,8-10", new[] { 1, 2, 3, 5, 8, 9, 10 })]
    [InlineData("5-1", new[] { 5, 4, 3, 2, 1 })]
    [InlineData(" 2 , 4 ", new[] { 2, 4 })]
    public void PageRange_Parse_Works(string input, int[] expected)
    {
        Assert.Equal(expected, PageRange.Parse(input).ToArray());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("abc")]
    [InlineData("")]
    public void PageRange_Parse_Invalid_Throws(string input)
    {
        Assert.ThrowsAny<Exception>(() => PageRange.Parse(input));
    }
}
