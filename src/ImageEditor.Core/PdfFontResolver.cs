using System.Diagnostics;
using System.Runtime.InteropServices;
using PdfSharp.Fonts;
using SixLabors.Fonts;

namespace ImageEditor.Core;

/// <summary>
/// PdfSharp 가 PDF 에 텍스트를 그릴 때 사용할 폰트를 공급합니다.
/// 실행 OS 의 시스템 폰트(Windows=맑은 고딕, Linux=나눔고딕 등)를 기본값으로 자동 탐색하고,
/// 사용자가 .ttf 파일을 추가로 등록할 수 있습니다.
/// </summary>
public sealed class PdfFontResolver : IFontResolver
{
    private readonly Dictionary<string, byte[]> _faceToBytes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _familyToFace = new(StringComparer.OrdinalIgnoreCase);

    public string DefaultFamily { get; private set; } = "";

    public PdfFontResolver()
    {
        var def = TryLoadDefault();
        if (def is not null)
        {
            Register(def.Value.family, def.Value.bytes);
            DefaultFamily = def.Value.family;
        }
    }

    /// <summary>등록된 폰트 패밀리 목록.</summary>
    public IReadOnlyList<string> Families => _familyToFace.Keys.OrderBy(k => k, StringComparer.CurrentCulture).ToList();

    public bool HasAnyFont => _faceToBytes.Count > 0;

    /// <summary>폰트 파일을 등록하고 패밀리 이름을 반환합니다.</summary>
    public string RegisterFontFile(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("폰트 파일을 찾을 수 없습니다.", path);
        var bytes = File.ReadAllBytes(path);
        var family = ReadFamilyName(bytes) ?? Path.GetFileNameWithoutExtension(path);
        Register(family, bytes);
        if (string.IsNullOrEmpty(DefaultFamily)) DefaultFamily = family;
        return family;
    }

    private void Register(string family, byte[] bytes)
    {
        _faceToBytes[family] = bytes;       // face 이름 = family 로 단순화
        _familyToFace[family] = family;
    }

    // ----- IFontResolver -----

    public byte[]? GetFont(string faceName)
    {
        if (_faceToBytes.TryGetValue(faceName, out var b)) return b;
        if (!string.IsNullOrEmpty(DefaultFamily) && _faceToBytes.TryGetValue(DefaultFamily, out var d)) return d;
        return null;
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        if (_familyToFace.TryGetValue(familyName, out var face)) return new FontResolverInfo(face);
        if (!string.IsNullOrEmpty(DefaultFamily)) return new FontResolverInfo(DefaultFamily);
        return null;
    }

    // ----- 폰트 탐색 -----

    private static string? ReadFamilyName(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            return FontDescription.LoadDescription(ms).FontFamilyInvariantCulture;
        }
        catch
        {
            return null; // ttc(컬렉션)·미지원 포맷 등
        }
    }

    private static (string family, byte[] bytes)? TryLoadDefault()
    {
        foreach (var path in CandidatePaths())
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                var bytes = File.ReadAllBytes(path);
                var family = ReadFamilyName(bytes);
                if (family is not null) return (family, bytes);
            }
            catch { /* 다음 후보 */ }
        }
        return null;
    }

    private static IEnumerable<string> CandidatePaths()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            yield return Path.Combine(fonts, "malgun.ttf");   // 맑은 고딕
            yield return Path.Combine(fonts, "arial.ttf");
            yield return Path.Combine(fonts, "gulim.ttc");
        }
        else
        {
            foreach (var p in FcMatch("NanumGothic")) yield return p;
            foreach (var p in FcMatch("Noto Sans CJK KR")) yield return p;
            foreach (var p in FcMatch(":lang=ko")) yield return p;
            foreach (var p in FcMatch("DejaVu Sans")) yield return p;
            yield return "/usr/share/fonts/truetype/nanum/NanumGothic.ttf";
            yield return "/usr/share/fonts/liberation-sans-fonts/LiberationSans-Regular.ttf";
            yield return "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf";
        }
    }

    /// <summary>리눅스/맥: fc-match 로 폰트 파일 경로를 찾습니다.</summary>
    private static IEnumerable<string> FcMatch(string pattern)
    {
        string? path = null;
        try
        {
            var psi = new ProcessStartInfo("fc-match", $"-f \"%{{file}}\" \"{pattern}\"")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p is not null)
            {
                path = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(2000);
            }
        }
        catch { /* fc-match 없음 */ }

        if (!string.IsNullOrEmpty(path) && File.Exists(path)) yield return path!;
    }
}
