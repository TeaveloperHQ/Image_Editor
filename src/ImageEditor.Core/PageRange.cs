namespace ImageEditor.Core;

/// <summary>
/// "1-3,5,8-10" 형식의 페이지 범위 문자열을 1-기반 페이지 번호 목록으로 변환합니다.
/// </summary>
public static class PageRange
{
    public static IReadOnlyList<int> Parse(string range)
    {
        if (string.IsNullOrWhiteSpace(range)) throw new ArgumentException("페이지 범위가 비어 있습니다.", nameof(range));

        var result = new List<int>();
        foreach (var rawPart in range.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var dash = rawPart.IndexOf('-');
            if (dash < 0)
            {
                result.Add(ParseNumber(rawPart));
                continue;
            }

            var startText = rawPart[..dash].Trim();
            var endText = rawPart[(dash + 1)..].Trim();
            var start = ParseNumber(startText);
            var end = ParseNumber(endText);

            if (start <= end)
                for (var i = start; i <= end; i++) result.Add(i);
            else // 역순 범위도 허용 (예: 5-1)
                for (var i = start; i >= end; i--) result.Add(i);
        }

        return result;
    }

    private static int ParseNumber(string text)
    {
        if (!int.TryParse(text, out var n) || n < 1)
            throw new FormatException($"잘못된 페이지 번호입니다: '{text}'. 1 이상의 정수를 입력하세요.");
        return n;
    }
}
