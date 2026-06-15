using Docnet.Core;
using Docnet.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ImageEditor.Core;

/// <summary>렌더링된 PDF 한 페이지.</summary>
public readonly record struct RenderedPage(byte[] Png, int PixelWidth, int PixelHeight, double PageWidthPt, double PageHeightPt)
{
    /// <summary>픽셀 → PDF 포인트 변환 배율 (포인트 = 픽셀 / Scale).</summary>
    public double Scale => PageWidthPt > 0 ? PixelWidth / PageWidthPt : 1.0;
}

/// <summary>PDFium(Docnet) 으로 PDF 페이지를 이미지로 렌더링합니다(화면 표시용).</summary>
public static class PdfRenderer
{
    /// <param name="scaling">렌더 배율. 1.0=72DPI, 2.0=144DPI(선명).</param>
    public static RenderedPage Render(byte[] pdfBytes, int pageIndex, double scaling = 2.0)
    {
        if (pdfBytes is null || pdfBytes.Length == 0) throw new ArgumentException("PDF 데이터가 비어 있습니다.", nameof(pdfBytes));
        if (scaling <= 0) scaling = 1.0;

        using var docReader = DocLib.Instance.GetDocReader(pdfBytes, new PageDimensions(scaling));
        if (pageIndex < 0 || pageIndex >= docReader.GetPageCount())
            throw new ArgumentOutOfRangeException(nameof(pageIndex));

        using var pageReader = docReader.GetPageReader(pageIndex);
        var w = pageReader.GetPageWidth();
        var h = pageReader.GetPageHeight();
        var bgra = pageReader.GetImage(); // BGRA, w*h*4

        using var rendered = Image.LoadPixelData<Bgra32>(bgra, w, h);

        // PDFium 은 배경을 투명하게 렌더하므로, 흰 종이 위에 합성해 실제 문서처럼 보이게 합니다.
        using var canvas = new Image<Rgba32>(w, h, new Rgba32(255, 255, 255, 255));
        canvas.Mutate(ctx => ctx.DrawImage(rendered, 1f));

        using var ms = new MemoryStream();
        canvas.SaveAsPng(ms);

        return new RenderedPage(ms.ToArray(), w, h, w / scaling, h / scaling);
    }

    public static byte[] LoadBytes(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("PDF 파일을 찾을 수 없습니다.", path);
        return File.ReadAllBytes(path);
    }
}
