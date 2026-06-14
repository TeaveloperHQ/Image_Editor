using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace ImageEditor.Core;

/// <summary>
/// 이미지 크기 변경 기능을 제공합니다. (PNG/JPEG/BMP/GIF/WebP 등 ImageSharp 지원 포맷)
/// </summary>
public static class ImageService
{
    /// <summary>
    /// 픽셀 단위로 이미지 크기를 변경합니다.
    /// width 또는 height 중 하나를 0으로 두면 종횡비를 유지하며 자동 계산됩니다.
    /// 둘 다 지정하고 keepAspectRatio=true 이면 지정한 박스 안에 맞춰 비율을 유지합니다.
    /// </summary>
    public static async Task ResizeToPixelsAsync(
        string inputPath,
        string outputPath,
        int width,
        int height,
        bool keepAspectRatio = true,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(inputPath)) throw new ArgumentException("입력 경로가 비어 있습니다.", nameof(inputPath));
        if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("출력 경로가 비어 있습니다.", nameof(outputPath));
        if (!File.Exists(inputPath)) throw new FileNotFoundException("이미지 파일을 찾을 수 없습니다.", inputPath);
        if (width < 0 || height < 0) throw new ArgumentOutOfRangeException(nameof(width), "크기는 음수가 될 수 없습니다.");
        if (width == 0 && height == 0) throw new ArgumentException("width 와 height 가 모두 0일 수 없습니다.");

        using var image = await Image.LoadAsync(inputPath, ct).ConfigureAwait(false);

        var options = new ResizeOptions
        {
            Size = new Size(width, height),
            // 한 변이 0이면 ImageSharp 가 종횡비에 맞춰 자동 계산합니다.
            Mode = keepAspectRatio ? ResizeMode.Max : ResizeMode.Stretch,
        };
        image.Mutate(x => x.Resize(options));

        EnsureDirectory(outputPath);
        await image.SaveAsync(outputPath, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 원본 대비 백분율(예: 50 = 50%)로 이미지 크기를 변경합니다.
    /// </summary>
    public static async Task ResizeByPercentAsync(
        string inputPath,
        string outputPath,
        double percent,
        CancellationToken ct = default)
    {
        if (percent <= 0) throw new ArgumentOutOfRangeException(nameof(percent), "백분율은 0보다 커야 합니다.");
        if (!File.Exists(inputPath)) throw new FileNotFoundException("이미지 파일을 찾을 수 없습니다.", inputPath);

        using var image = await Image.LoadAsync(inputPath, ct).ConfigureAwait(false);

        var factor = percent / 100.0;
        var newWidth = Math.Max(1, (int)Math.Round(image.Width * factor));
        var newHeight = Math.Max(1, (int)Math.Round(image.Height * factor));
        image.Mutate(x => x.Resize(newWidth, newHeight));

        EnsureDirectory(outputPath);
        await image.SaveAsync(outputPath, ct).ConfigureAwait(false);
    }

    private static void EnsureDirectory(string filePath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    }
}
