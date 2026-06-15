using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ImageEditor.Core;

/// <summary>
/// 한 장의 이미지를 메모리에 올려두고 자르기/회전/텍스트·그림 추가를 누적 적용하는 편집 세션입니다.
/// 편집 후 <see cref="ToPngBytes"/> 로 미리보기를 얻거나 <see cref="Save"/> 로 파일로 내보냅니다.
/// </summary>
public sealed class ImageEditSession : IDisposable
{
    private Image<Rgba32> _image;

    private ImageEditSession(Image<Rgba32> image) => _image = image;

    public int Width => _image.Width;
    public int Height => _image.Height;

    public static ImageEditSession Load(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("이미지 파일을 찾을 수 없습니다.", path);
        return new ImageEditSession(Image.Load<Rgba32>(path));
    }

    /// <summary>PNG 등 인코딩된 바이트에서 세션을 복원합니다(실행 취소용).</summary>
    public static ImageEditSession FromBytes(byte[] bytes) => new(Image.Load<Rgba32>(bytes));

    // ----- 회전 -----

    /// <summary>시계 방향 90도 회전.</summary>
    public void RotateRight() => _image.Mutate(x => x.Rotate(RotateMode.Rotate90));

    /// <summary>반시계 방향 90도 회전.</summary>
    public void RotateLeft() => _image.Mutate(x => x.Rotate(RotateMode.Rotate270));

    /// <summary>임의 각도 회전(캔버스는 자동 확장, 빈 영역은 투명).</summary>
    public void Rotate(float degrees) => _image.Mutate(x => x.Rotate(degrees));

    /// <summary>좌우 반전.</summary>
    public void FlipHorizontal() => _image.Mutate(x => x.Flip(FlipMode.Horizontal));

    /// <summary>상하 반전.</summary>
    public void FlipVertical() => _image.Mutate(x => x.Flip(FlipMode.Vertical));

    // ----- 자르기 -----

    /// <summary>지정한 사각형 영역으로 자릅니다. 이미지 범위와 교차하는 부분만 남습니다.</summary>
    public void Crop(Rectangle rect)
    {
        var clamped = Rectangle.Intersect(rect, new Rectangle(0, 0, _image.Width, _image.Height));
        if (clamped.Width <= 0 || clamped.Height <= 0)
            throw new ArgumentException("자를 영역이 이미지 범위를 벗어났습니다.", nameof(rect));
        _image.Mutate(x => x.Crop(clamped));
    }

    // ----- 텍스트 추가 -----

    /// <summary>(x, y) 위치에 텍스트를 그려 넣습니다. color 는 ImageSharp Color(예: Color.Black, Color.ParseHex).</summary>
    public void AddText(string text, FontFamily family, float fontSize, Color color, int x, int y, FontStyle style = FontStyle.Regular)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (fontSize <= 0) throw new ArgumentOutOfRangeException(nameof(fontSize), "글자 크기는 0보다 커야 합니다.");
        var font = family.CreateFont(fontSize, style);
        _image.Mutate(ctx => ctx.DrawText(text, font, color, new PointF(x, y)));
    }

    // ----- 그림(이미지) 추가 -----

    /// <summary>다른 이미지를 (x, y) 위치에 얹습니다. width/height 를 주면 그 크기로 리사이즈 후 합성합니다.</summary>
    public void AddImage(string overlayPath, int x, int y, int? width = null, int? height = null, float opacity = 1f)
    {
        if (!File.Exists(overlayPath)) throw new FileNotFoundException("추가할 이미지를 찾을 수 없습니다.", overlayPath);
        using var overlay = Image.Load<Rgba32>(overlayPath);
        if ((width is > 0) || (height is > 0))
            overlay.Mutate(o => o.Resize(width ?? 0, height ?? 0));
        _image.Mutate(ctx => ctx.DrawImage(overlay, new Point(x, y), Math.Clamp(opacity, 0f, 1f)));
    }

    // ----- 출력 -----

    /// <summary>현재 상태를 PNG 바이트로 인코딩합니다(미리보기용).</summary>
    public byte[] ToPngBytes()
    {
        using var ms = new MemoryStream();
        _image.SaveAsPng(ms);
        return ms.ToArray();
    }

    /// <summary>현재 상태를 파일로 저장합니다(확장자에 따라 포맷 결정).</summary>
    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        _image.Save(path);
    }

    public void Dispose() => _image.Dispose();
}
