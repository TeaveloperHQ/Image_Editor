using ImageEditor.Core;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace ImageEditor.Tests;

public class PdfCompressTests : IDisposable
{
    private readonly string _dir;

    public PdfCompressTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "PdfCompressTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* 무시 */ }
    }

    private string Path2(string name) => Path.Combine(_dir, name);

    // 압축이 의미 있도록 잡음 패턴의 큰 JPEG 생성
    private string CreateBusyJpeg(string name, int size)
    {
        var path = Path2(name);
        using var img = new Image<Rgb24>(size, size);
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
                img[x, y] = new Rgb24((byte)(x % 256), (byte)(y % 256), (byte)((x * y) % 256));
        img.SaveAsJpeg(path);
        return path;
    }

    private string CreatePdfWithImage(string name, string jpgPath)
    {
        var path = Path2(name);
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        using (var gfx = XGraphics.FromPdfPage(page))
        using (var img = XImage.FromFile(jpgPath))
            gfx.DrawImage(img, 0, 0, page.Width.Point, page.Height.Point);
        doc.Save(path);
        return path;
    }

    [Fact]
    public void Compress_RecompressesImage_AndShrinksFile()
    {
        var jpg = CreateBusyJpeg("big.jpg", 1400);
        var pdf = CreatePdfWithImage("doc.pdf", jpg);
        var output = Path2("doc_small.pdf");

        var result = PdfCompressor.Compress(pdf, output, quality: 30, maxDimension: 600);

        Assert.True(result.ImagesRecompressed >= 1, "이미지가 재압축되어야 합니다.");
        Assert.True(result.NewBytes < result.OriginalBytes,
            $"용량이 줄어야 합니다. before={result.OriginalBytes}, after={result.NewBytes}");
        Assert.True(File.Exists(output));
    }

    [Fact]
    public void Compress_PreservesPageCount()
    {
        var jpg = CreateBusyJpeg("big.jpg", 800);
        var pdf = CreatePdfWithImage("doc.pdf", jpg);
        var output = Path2("out.pdf");

        PdfCompressor.Compress(pdf, output, quality: 50, maxDimension: 0);

        Assert.Equal(PdfService.GetPageCount(pdf), PdfService.GetPageCount(output));
    }

    [Fact]
    public void Compress_QualityIsClamped_NoThrow()
    {
        var jpg = CreateBusyJpeg("big.jpg", 400);
        var pdf = CreatePdfWithImage("doc.pdf", jpg);
        var output = Path2("out.pdf");

        var result = PdfCompressor.Compress(pdf, output, quality: 999, maxDimension: -5);
        Assert.True(result.NewBytes > 0);
    }
}
