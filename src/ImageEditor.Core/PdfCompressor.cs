using System.IO.Compression;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ImageEditor.Core;

/// <summary>압축 결과 요약.</summary>
public readonly record struct PdfCompressionResult(long OriginalBytes, long NewBytes, int ImagesRecompressed)
{
    /// <summary>새 용량 / 원본 용량 (0~1, 작을수록 많이 줄어듦).</summary>
    public double Ratio => OriginalBytes > 0 ? (double)NewBytes / OriginalBytes : 1.0;
}

/// <summary>
/// PDF 안에 박힌 이미지를 다운샘플 + JPEG 재압축하여 파일 용량을 줄입니다.
/// (용지 크기 변경이 아니라 "이미지처럼 용량 줄이기")
/// 처리 가능한 이미지: JPEG(DCTDecode), 무압축 예측기 없는 RGB/Gray(FlateDecode 8bpc).
/// CMYK·JPEG2000·CCITT·인덱스·투명(SMask) 등 까다로운 이미지는 원본을 그대로 둡니다.
/// </summary>
public static class PdfCompressor
{
    /// <param name="quality">JPEG 품질 1~100 (낮을수록 용량↓, 화질↓).</param>
    /// <param name="maxDimension">이미지 긴 변 최대 픽셀(이보다 크면 축소). 0 이면 축소 안 함.</param>
    public static PdfCompressionResult Compress(string inputPath, string outputPath, int quality = 60, int maxDimension = 0)
    {
        if (!File.Exists(inputPath)) throw new FileNotFoundException("PDF 파일을 찾을 수 없습니다.", inputPath);
        quality = Math.Clamp(quality, 1, 100);
        if (maxDimension < 0) maxDimension = 0;

        var originalBytes = new FileInfo(inputPath).Length;

        using var doc = PdfReader.Open(inputPath, PdfDocumentOpenMode.Modify);
        doc.Options.CompressContentStreams = true;
        doc.Options.NoCompression = false;

        var recompressed = 0;
        foreach (var obj in doc.Internals.GetAllObjects())
        {
            if (obj is not PdfDictionary dict || dict.Stream is null) continue;
            if (dict.Elements.GetName("/Subtype") != "/Image") continue;
            // 투명도/마스크/CMYK 는 건드리지 않음 (깨질 위험)
            if (dict.Elements.ContainsKey("/SMask") || dict.Elements.ContainsKey("/Mask")) continue;
            if (dict.Elements.ContainsKey("/ImageMask")) continue;
            if (dict.Elements.GetName("/ColorSpace") == "/DeviceCMYK") continue;

            try
            {
                if (TryRecompressImage(dict, quality, maxDimension)) recompressed++;
            }
            catch
            {
                // 한 이미지가 실패해도 나머지는 계속 진행 (원본 유지)
            }
        }

        var dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        doc.Save(outputPath);

        var newBytes = new FileInfo(outputPath).Length;
        return new PdfCompressionResult(originalBytes, newBytes, recompressed);
    }

    private static bool TryRecompressImage(PdfDictionary dict, int quality, int maxDimension)
    {
        var width = dict.Elements.GetInteger("/Width");
        var height = dict.Elements.GetInteger("/Height");
        if (width <= 0 || height <= 0) return false;

        var filter = GetSingleFilter(dict);
        var colorSpace = dict.Elements.GetName("/ColorSpace");
        var raw = dict.Stream.Value;
        var originalLength = raw.Length;

        using Image<Rgb24>? image = LoadAsRgb(filter, colorSpace, raw, width, height, dict);
        if (image is null) return false;

        var newW = image.Width;
        var newH = image.Height;
        if (maxDimension > 0 && Math.Max(newW, newH) > maxDimension)
        {
            var scale = (double)maxDimension / Math.Max(newW, newH);
            newW = Math.Max(1, (int)Math.Round(newW * scale));
            newH = Math.Max(1, (int)Math.Round(newH * scale));
            image.Mutate(x => x.Resize(newW, newH));
        }

        using var ms = new MemoryStream();
        image.Save(ms, new JpegEncoder { Quality = quality });
        var encoded = ms.ToArray();

        var resized = newW != width || newH != height;
        // 축소도 없고 용량 이득도 없으면 원본 유지
        if (!resized && encoded.Length >= originalLength) return false;

        dict.Stream.Value = encoded;
        dict.Elements.SetInteger("/Length", encoded.Length);
        dict.Elements.SetInteger("/Width", newW);
        dict.Elements.SetInteger("/Height", newH);
        dict.Elements.SetInteger("/BitsPerComponent", 8);
        dict.Elements["/Filter"] = new PdfName("/DCTDecode");
        dict.Elements["/ColorSpace"] = new PdfName("/DeviceRGB");
        dict.Elements.Remove("/DecodeParms");
        dict.Elements.Remove("/DP");
        return true;
    }

    /// <summary>지원하는 이미지를 RGB24 로 디코딩합니다. 지원 못 하면 null.</summary>
    private static Image<Rgb24>? LoadAsRgb(string? filter, string colorSpace, byte[] raw, int width, int height, PdfDictionary dict)
    {
        switch (filter)
        {
            case "/DCTDecode":
                // 스트림 자체가 JPEG → ImageSharp 가 바로 디코딩
                return Image.Load<Rgb24>(raw);

            case "/FlateDecode":
            {
                if (HasPredictor(dict)) return null; // PNG 예측기 등은 미지원
                if (dict.Elements.GetInteger("/BitsPerComponent") != 8) return null;

                var comps = colorSpace switch
                {
                    "/DeviceRGB" => 3,
                    "/DeviceGray" => 1,
                    _ => 0
                };
                if (comps == 0) return null;

                var decoded = ZlibDecode(raw);
                var needed = width * height * comps;
                if (decoded.Length < needed) return null;

                if (comps == 3)
                    return Image.LoadPixelData<Rgb24>(decoded.AsSpan(0, needed), width, height);

                using var gray = Image.LoadPixelData<L8>(decoded.AsSpan(0, needed), width, height);
                return gray.CloneAs<Rgb24>();
            }

            default:
                return null; // JPX, CCITT, LZW, 다중필터 등은 건드리지 않음
        }
    }

    private static string? GetSingleFilter(PdfDictionary dict)
    {
        var item = dict.Elements["/Filter"];
        if (item is PdfReference fr) item = fr.Value;
        return item switch
        {
            PdfName name => name.Value,
            PdfArray arr when arr.Elements.Count == 1 && arr.Elements[0] is PdfName n => n.Value,
            _ => null
        };
    }

    private static bool HasPredictor(PdfDictionary dict)
    {
        var dp = dict.Elements["/DecodeParms"] ?? dict.Elements["/DP"];
        if (dp is PdfReference r) dp = r.Value;
        switch (dp)
        {
            case PdfDictionary d:
                return d.Elements.GetInteger("/Predictor") > 1;
            case PdfArray arr:
                foreach (var e in arr.Elements)
                {
                    var ed = e is PdfReference er ? er.Value : e;
                    if (ed is PdfDictionary dd && dd.Elements.GetInteger("/Predictor") > 1) return true;
                }
                return false;
            default:
                return false;
        }
    }

    private static byte[] ZlibDecode(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }
}
