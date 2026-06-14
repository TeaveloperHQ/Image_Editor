using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace ImageEditor.Core;

/// <summary>
/// PDF 결합/분해/크기 변경 기능을 제공합니다.
/// </summary>
public static class PdfService
{
    /// <summary>
    /// 여러 PDF 파일을 입력 순서대로 하나로 결합합니다.
    /// </summary>
    public static void Merge(IEnumerable<string> inputPaths, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(inputPaths);
        if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("출력 경로가 비어 있습니다.", nameof(outputPath));

        var paths = inputPaths.ToList();
        if (paths.Count == 0) throw new ArgumentException("결합할 PDF 가 하나도 없습니다.", nameof(inputPaths));

        using var output = new PdfDocument();
        foreach (var path in paths)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("PDF 파일을 찾을 수 없습니다.", path);
            using var input = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            for (var i = 0; i < input.PageCount; i++)
                output.AddPage(input.Pages[i]);
        }

        EnsureDirectory(outputPath);
        output.Save(outputPath);
    }

    /// <summary>
    /// PDF 의 모든 페이지를 한 장씩 개별 PDF 파일로 분해합니다.
    /// 생성된 파일 경로 목록을 반환합니다.
    /// </summary>
    public static IReadOnlyList<string> SplitToSinglePages(string inputPath, string outputDirectory, string? baseName = null)
    {
        if (!File.Exists(inputPath)) throw new FileNotFoundException("PDF 파일을 찾을 수 없습니다.", inputPath);
        if (string.IsNullOrWhiteSpace(outputDirectory)) throw new ArgumentException("출력 폴더가 비어 있습니다.", nameof(outputDirectory));

        Directory.CreateDirectory(outputDirectory);
        baseName ??= Path.GetFileNameWithoutExtension(inputPath);

        using var input = PdfReader.Open(inputPath, PdfDocumentOpenMode.Import);
        var created = new List<string>(input.PageCount);
        var pad = input.PageCount.ToString().Length;

        for (var i = 0; i < input.PageCount; i++)
        {
            using var doc = new PdfDocument();
            doc.AddPage(input.Pages[i]);
            var outPath = Path.Combine(outputDirectory, $"{baseName}_{(i + 1).ToString().PadLeft(pad, '0')}.pdf");
            doc.Save(outPath);
            created.Add(outPath);
        }

        return created;
    }

    /// <summary>
    /// 지정한 1-기반 페이지 번호들만 모아 하나의 PDF 로 추출합니다.
    /// pageNumbers 순서가 곧 결과 페이지 순서입니다.
    /// </summary>
    public static void ExtractPages(string inputPath, string outputPath, IEnumerable<int> pageNumbers)
    {
        if (!File.Exists(inputPath)) throw new FileNotFoundException("PDF 파일을 찾을 수 없습니다.", inputPath);
        ArgumentNullException.ThrowIfNull(pageNumbers);

        var pages = pageNumbers.ToList();
        if (pages.Count == 0) throw new ArgumentException("추출할 페이지가 없습니다.", nameof(pageNumbers));

        using var input = PdfReader.Open(inputPath, PdfDocumentOpenMode.Import);
        using var output = new PdfDocument();
        foreach (var n in pages)
        {
            if (n < 1 || n > input.PageCount)
                throw new ArgumentOutOfRangeException(nameof(pageNumbers), $"페이지 번호 {n} 가 범위(1~{input.PageCount})를 벗어났습니다.");
            output.AddPage(input.Pages[n - 1]);
        }

        EnsureDirectory(outputPath);
        output.Save(outputPath);
    }

    /// <summary>
    /// "1-3,5,8-10" 형식의 문자열로 페이지를 추출합니다.
    /// </summary>
    public static void ExtractPages(string inputPath, string outputPath, string pageRange)
        => ExtractPages(inputPath, outputPath, PageRange.Parse(pageRange));

    /// <summary>
    /// 모든 페이지의 콘텐츠를 지정한 표준 용지 크기에 맞춰 비율대로 채워 넣어
    /// PDF 페이지 크기를 변경합니다. (예: A4 → Letter)
    /// </summary>
    public static void ResizePages(string inputPath, string outputPath, PageSize targetSize, bool keepAspectRatio = true)
    {
        if (!File.Exists(inputPath)) throw new FileNotFoundException("PDF 파일을 찾을 수 없습니다.", inputPath);

        var target = PageSizeConverter.ToSize(targetSize); // XSize (point 단위)

        using var form = XPdfForm.FromFile(inputPath);
        using var output = new PdfDocument();

        for (var i = 0; i < form.PageCount; i++)
        {
            form.PageNumber = i + 1; // XPdfForm 은 1-기반
            var page = output.AddPage();
            page.Width = XUnit.FromPoint(target.Width);
            page.Height = XUnit.FromPoint(target.Height);

            using var gfx = XGraphics.FromPdfPage(page);

            if (keepAspectRatio)
            {
                var scale = Math.Min(target.Width / form.PointWidth, target.Height / form.PointHeight);
                var w = form.PointWidth * scale;
                var h = form.PointHeight * scale;
                var x = (target.Width - w) / 2;
                var y = (target.Height - h) / 2;
                gfx.DrawImage(form, x, y, w, h);
            }
            else
            {
                gfx.DrawImage(form, 0, 0, target.Width, target.Height);
            }
        }

        EnsureDirectory(outputPath);
        output.Save(outputPath);
    }

    /// <summary>입력 PDF 의 페이지 수를 반환합니다.</summary>
    public static int GetPageCount(string inputPath)
    {
        if (!File.Exists(inputPath)) throw new FileNotFoundException("PDF 파일을 찾을 수 없습니다.", inputPath);
        using var input = PdfReader.Open(inputPath, PdfDocumentOpenMode.Import);
        return input.PageCount;
    }

    private static void EnsureDirectory(string filePath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    }
}
