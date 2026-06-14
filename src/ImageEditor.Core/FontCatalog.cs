using SixLabors.Fonts;

namespace ImageEditor.Core;

/// <summary>
/// 텍스트 추가에 사용할 폰트 모음입니다.
/// 실행 중인 OS 의 시스템 폰트(Windows 라면 맑은 고딕 등)를 모두 포함하며,
/// 사용자가 .ttf/.otf 파일을 직접 추가할 수도 있습니다.
/// </summary>
public sealed class FontCatalog
{
    private readonly FontCollection _collection = new();

    // 시스템에 없을 때 시도할 한글/라틴 우선순위 (앞에서부터 먼저 발견되는 것 사용)
    private static readonly string[] PreferredFallback =
    {
        "Malgun Gothic", "맑은 고딕", "Noto Sans CJK KR", "NanumGothic", "나눔고딕",
        "Segoe UI", "Arial", "Liberation Sans", "DejaVu Sans"
    };

    public FontCatalog()
    {
        _collection.AddSystemFonts();
    }

    /// <summary>설치된 폰트 패밀리 이름 목록(가나다/알파벳 순).</summary>
    public IReadOnlyList<string> FamilyNames() =>
        _collection.Families.Select(f => f.Name).Distinct().OrderBy(n => n, StringComparer.CurrentCulture).ToList();

    /// <summary>폰트 파일(.ttf/.otf)을 추가하고 패밀리 이름을 반환합니다.</summary>
    public string AddFontFile(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("폰트 파일을 찾을 수 없습니다.", path);
        var family = _collection.Add(path);
        return family.Name;
    }

    public bool TryGet(string name, out FontFamily family) => _collection.TryGet(name, out family);

    /// <summary>
    /// 이름으로 폰트를 찾되, 없거나 렌더 불가(CFF2 등 미지원)하면
    /// 한글 지원 폰트 → 렌더 가능한 임의 폰트 순으로 대체합니다.
    /// </summary>
    public FontFamily Resolve(string? preferred)
    {
        if (!string.IsNullOrWhiteSpace(preferred) && _collection.TryGet(preferred, out var f) && CanRender(f))
            return f;

        foreach (var candidate in PreferredFallback)
            if (_collection.TryGet(candidate, out var ff) && CanRender(ff))
                return ff;

        foreach (var family in _collection.Families)
            if (CanRender(family))
                return family;

        throw new InvalidOperationException("렌더링 가능한 폰트가 없습니다. .ttf 폰트 파일을 추가하세요.");
    }

    /// <summary>해당 폰트로 실제 글자를 그릴 수 있는지 검사합니다(미지원 포맷이면 false).</summary>
    public static bool CanRender(FontFamily family)
    {
        try
        {
            var font = family.CreateFont(12f);
            _ = TextMeasurer.MeasureSize("Ag", new TextOptions(font));
            return true;
        }
        catch
        {
            return false;
        }
    }
}
