using System.Globalization;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ImageEditor.Core;

public enum StampShape { Circle, Square }

/// <summary>
/// 도장(인감/직인) 이미지를 만듭니다.
/// ① 글자(한글·한자)로 배경 투명 도장 PNG 생성
/// ② 스캔한 직인의 흰 배경을 투명하게 변환
/// </summary>
public static class StampMaker
{
    /// <summary>
    /// 글자로 도장 이미지를 만듭니다(배경 투명). 글자는 전통 도장처럼
    /// 오른쪽 위에서 시작해 위→아래, 오른쪽→왼쪽 순으로 격자 배치합니다.
    /// </summary>
    public static byte[] CreateTextStampPng(string text, FontFamily family, string colorHex, StampShape shape, int sizePx = 320)
    {
        text = (text ?? string.Empty).Trim();
        if (text.Length == 0) throw new ArgumentException("도장에 넣을 글자를 입력하세요.", nameof(text));
        if (sizePx < 64) sizePx = 64;

        var color = ParseColor(colorHex);
        var chars = SplitCharacters(text);
        var n = chars.Count;

        var cols = (int)Math.Ceiling(Math.Sqrt(n));
        var rows = (int)Math.Ceiling((double)n / cols);

        using var image = new Image<Rgba32>(sizePx, sizePx); // 전부 투명
        var thickness = MathF.Max(2.5f, sizePx * 0.05f);
        var pen = Pens.Solid(color, thickness);
        var brush = new SolidBrush(color);

        // 원형은 둥근 테두리 안에 들어가도록 글자 영역을 더 좁게 잡습니다.
        var areaFrac = shape == StampShape.Circle ? 0.52f : 0.72f;
        var area = sizePx * areaFrac;
        var left = (sizePx - area) / 2f;
        var top = (sizePx - area) / 2f;
        var cellW = area / cols;
        var cellH = area / rows;
        var fontSize = MathF.Min(cellW, cellH) * 0.78f;
        var font = family.CreateFont(fontSize, FontStyle.Regular);

        image.Mutate(ctx =>
        {
            // 테두리
            var inset = thickness;
            if (shape == StampShape.Circle)
                ctx.Draw(pen, new EllipsePolygon(sizePx / 2f, sizePx / 2f, sizePx / 2f - inset, sizePx / 2f - inset));
            else
                ctx.Draw(pen, new RectangularPolygon(inset, inset, sizePx - 2 * inset, sizePx - 2 * inset));

            // 글자: 오른쪽 열부터(전통 방식) 위→아래로 채움
            var k = 0;
            for (var colFromRight = 0; colFromRight < cols && k < n; colFromRight++)
            {
                var screenCol = cols - 1 - colFromRight;
                for (var row = 0; row < rows && k < n; row++)
                {
                    var cx = left + (screenCol + 0.5f) * cellW;
                    var cy = top + (row + 0.5f) * cellH;
                    var options = new RichTextOptions(font)
                    {
                        Origin = new PointF(cx, cy),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    ctx.DrawText(options, chars[k], brush);
                    k++;
                }
            }
        });

        return ToPng(image);
    }

    /// <summary>
    /// 스캔한 직인 이미지의 밝은(흰) 배경을 투명하게 만듭니다.
    /// recolorHex 를 주면 남은 잉크를 그 색으로 통일합니다.
    /// </summary>
    /// <param name="whiteThreshold">이 값보다 흰색에 가까우면 투명(0~255, 기본 60). 클수록 더 많이 지움.</param>
    public static byte[] MakeTransparentPng(string scanPath, int whiteThreshold = 60, string? recolorHex = null)
    {
        if (!File.Exists(scanPath)) throw new FileNotFoundException("스캔 이미지를 찾을 수 없습니다.", scanPath);
        whiteThreshold = Math.Clamp(whiteThreshold, 5, 240);

        using var image = Image.Load<Rgba32>(scanPath);

        Rgba32? recolor = null;
        if (!string.IsNullOrWhiteSpace(recolorHex))
            recolor = ParseColor(recolorHex).ToPixel<Rgba32>();

        var lo = whiteThreshold;
        var hi = Math.Min(255, whiteThreshold + 50); // 경계 부드럽게

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    ref var p = ref row[x];
                    // 흰색과의 거리: 흰색=0, 어둡거나 진한 색=큼
                    var d = 255 - Math.Min(p.R, Math.Min(p.G, p.B));
                    byte alpha = d <= lo ? (byte)0
                               : d >= hi ? (byte)255
                               : (byte)((d - lo) * 255 / (hi - lo));

                    if (recolor is { } rc && alpha > 0)
                    {
                        p.R = rc.R; p.G = rc.G; p.B = rc.B;
                    }
                    p.A = alpha;
                }
            }
        });

        return ToPng(image);
    }

    private static IReadOnlyList<string> SplitCharacters(string text)
    {
        var list = new List<string>();
        var e = StringInfo.GetTextElementEnumerator(text);
        while (e.MoveNext())
        {
            var el = (string)e.Current;
            if (!string.IsNullOrWhiteSpace(el)) list.Add(el);
        }
        return list;
    }

    private static Color ParseColor(string? hex)
        => !string.IsNullOrWhiteSpace(hex) && Color.TryParseHex(hex, out var c) ? c : Color.Red;

    private static byte[] ToPng(Image image)
    {
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }
}
