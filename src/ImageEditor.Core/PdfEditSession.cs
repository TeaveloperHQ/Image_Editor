using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace ImageEditor.Core;

/// <summary>
/// PDF 위에 텍스트·이미지·서명을 얹는 편집 세션입니다(기존 내용은 그대로 두고 위에 그림).
/// 좌표는 모두 PDF 포인트(1/72인치, 왼쪽 위 원점) 기준입니다.
/// 편집 상태는 바이트로 보관하며, 편집할 때마다 새로 열어 적용합니다
/// (PdfSharp 문서 객체는 한 번 저장하면 재사용 불가하기 때문).
/// </summary>
public sealed class PdfEditSession : IDisposable
{
    private static PdfFontResolver? _sharedResolver;

    private byte[] _bytes;
    private readonly (double WidthPt, double HeightPt)[] _pageSizes;

    public PdfFontResolver Fonts { get; }

    private PdfEditSession(byte[] bytes, (double, double)[] pageSizes, PdfFontResolver fonts)
    {
        _bytes = bytes;
        _pageSizes = pageSizes;
        Fonts = fonts;
    }

    public int PageCount => _pageSizes.Length;

    /// <summary>지정한 페이지의 크기(포인트).</summary>
    public (double WidthPt, double HeightPt) PageSize(int index) => _pageSizes[index];

    public static PdfEditSession Load(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("PDF 파일을 찾을 수 없습니다.", path);
        EnsureResolver();
        var bytes = File.ReadAllBytes(path);
        return new PdfEditSession(bytes, ReadPageSizes(bytes), _sharedResolver!);
    }

    /// <summary>PDF 바이트에서 세션을 복원합니다(실행 취소용).</summary>
    public static PdfEditSession FromBytes(byte[] bytes)
    {
        EnsureResolver();
        return new PdfEditSession((byte[])bytes.Clone(), ReadPageSizes(bytes), _sharedResolver!);
    }

    private static void EnsureResolver()
    {
        if (_sharedResolver is null)
        {
            _sharedResolver = new PdfFontResolver();
            GlobalFontSettings.FontResolver = _sharedResolver;
        }
    }

    private static (double, double)[] ReadPageSizes(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        using var doc = PdfReader.Open(input, PdfDocumentOpenMode.Import);
        var sizes = new (double, double)[doc.PageCount];
        for (var i = 0; i < doc.PageCount; i++)
            sizes[i] = (doc.Pages[i].Width.Point, doc.Pages[i].Height.Point);
        return sizes;
    }

    /// <summary>현재 상태의 PDF 바이트 사본.</summary>
    public byte[] ToBytes() => (byte[])_bytes.Clone();

    /// <summary>지정 페이지를 이미지로 렌더링합니다(화면 표시용).</summary>
    public RenderedPage RenderPage(int index, double scaling = 2.0) => PdfRenderer.Render(_bytes, index, scaling);

    /// <summary>(xPt, yPt) 위치(텍스트 왼쪽 위)에 글자를 얹습니다.</summary>
    public void AddText(int pageIndex, string text, double xPt, double yPt, string? family, double sizePt, string colorHex)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (sizePt <= 0) throw new ArgumentOutOfRangeException(nameof(sizePt));
        if (!Fonts.HasAnyFont) throw new InvalidOperationException("사용할 폰트가 없습니다. 폰트를 추가하세요.");

        Mutate(pageIndex, (gfx, page) =>
        {
            var fam = string.IsNullOrWhiteSpace(family) ? Fonts.DefaultFamily : family!;
            var font = new XFont(fam, sizePt);
            var brush = new XSolidBrush(ParseColor(colorHex));
            gfx.DrawString(text, font, brush,
                new XRect(xPt, yPt, page.Width.Point - xPt, page.Height.Point - yPt), XStringFormats.TopLeft);
        });
    }

    /// <summary>(xPt, yPt) 위치에 이미지/서명을 얹습니다. widthPt 0 이면 원본 크기.</summary>
    public void AddImage(int pageIndex, string imagePath, double xPt, double yPt, double widthPt = 0)
    {
        if (!File.Exists(imagePath)) throw new FileNotFoundException("얹을 이미지를 찾을 수 없습니다.", imagePath);

        Mutate(pageIndex, (gfx, _) =>
        {
            using var img = XImage.FromFile(imagePath);
            var w = widthPt > 0 ? widthPt : img.PointWidth;
            var h = w * img.PixelHeight / img.PixelWidth;
            gfx.DrawImage(img, xPt, yPt, w, h);
        });
    }

    private void Mutate(int pageIndex, Action<XGraphics, PdfPage> draw)
    {
        if (pageIndex < 0 || pageIndex >= _pageSizes.Length)
            throw new ArgumentOutOfRangeException(nameof(pageIndex));

        using var input = new MemoryStream(_bytes);
        using var doc = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
        var page = doc.Pages[pageIndex];
        using (var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append))
            draw(gfx, page);

        using var output = new MemoryStream();
        doc.Save(output);
        _bytes = output.ToArray();
    }

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllBytes(path, _bytes);
    }

    public void Dispose() { /* 바이트 기반이라 별도 해제 불필요 */ }

    private static XColor ParseColor(string? hex)
    {
        if (!string.IsNullOrWhiteSpace(hex))
        {
            var s = hex.TrimStart('#');
            if (s.Length == 6 &&
                int.TryParse(s.AsSpan(0, 2), System.Globalization.NumberStyles.HexNumber, null, out var r) &&
                int.TryParse(s.AsSpan(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var g) &&
                int.TryParse(s.AsSpan(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
                return XColor.FromArgb(r, g, b);
        }
        return XColors.Black;
    }
}
