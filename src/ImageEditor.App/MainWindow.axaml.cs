using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using ImageEditor.Core;
using PdfSharp;

namespace ImageEditor.App;

public partial class MainWindow : Window
{
    private static readonly FilePickerFileType ImageType = new("이미지 파일")
    {
        Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp", "*.tiff" }
    };
    private static readonly FilePickerFileType PdfType = new("PDF 파일")
    {
        Patterns = new[] { "*.pdf" }
    };

    private static readonly FilePickerFileType FontType = new("폰트 파일")
    {
        Patterns = new[] { "*.ttf", "*.otf", "*.ttc" }
    };

    private readonly ObservableCollection<string> _mergeFiles = new();

    // 이미지 편집 상태
    private readonly FontCatalog _fonts = new();
    private ImageEditSession? _session;
    private double _displayScale = 1.0;
    private string? _overlayPath;
    private Avalonia.Point _cropStart;
    private bool _dragging;

    public MainWindow()
    {
        InitializeComponent();
        MergeList.ItemsSource = _mergeFiles;
        ReloadFontCombo(null);
    }

    // ---------- 공통 헬퍼 ----------

    private void SetStatus(string message) => StatusText.Text = message;

    private void SetError(string message) => StatusText.Text = "⚠ " + message;

    private async Task<string?> PickOpenFileAsync(string title, FilePickerFileType type)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { type }
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private async Task<IReadOnlyList<string>> PickOpenFilesAsync(string title, FilePickerFileType type)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = true,
            FileTypeFilter = new[] { type }
        });
        return files.Select(f => f.TryGetLocalPath()).Where(p => p is not null).Cast<string>().ToList();
    }

    private async Task<string?> PickSaveFileAsync(string title, string defaultName, FilePickerFileType type)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = defaultName,
            FileTypeChoices = new[] { type }
        });
        return file?.TryGetLocalPath();
    }

    private async Task<string?> PickFolderAsync(string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    // ---------- 이미지 크기 변경 ----------

    private async void OnPickImage(object? sender, RoutedEventArgs e)
    {
        var path = await PickOpenFileAsync("이미지 선택", ImageType);
        if (path is not null) ImgInput.Text = path;
    }

    private async void OnResizeImage(object? sender, RoutedEventArgs e)
    {
        var input = ImgInput.Text;
        if (string.IsNullOrWhiteSpace(input) || !File.Exists(input)) { SetError("이미지 파일을 먼저 선택하세요."); return; }

        var ext = Path.GetExtension(input);
        var suggested = Path.GetFileNameWithoutExtension(input) + "_resized" + ext;
        var output = await PickSaveFileAsync("저장 위치", suggested, ImageType);
        if (output is null) return;

        try
        {
            SetStatus("크기 변경 중…");
            if (ImgModePercent.IsChecked == true)
            {
                if (!double.TryParse(ImgPercent.Text, out var pct)) { SetError("백분율이 올바르지 않습니다."); return; }
                await ImageService.ResizeByPercentAsync(input, output, pct);
            }
            else
            {
                int.TryParse(ImgWidth.Text, out var w);
                int.TryParse(ImgHeight.Text, out var h);
                await ImageService.ResizeToPixelsAsync(input, output, w, h, ImgKeepAspect.IsChecked == true);
            }
            SetStatus($"완료: {output}");
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
    }

    // ---------- PDF 결합 ----------

    private async void OnAddMergeFiles(object? sender, RoutedEventArgs e)
    {
        foreach (var p in await PickOpenFilesAsync("결합할 PDF 선택", PdfType))
            if (!_mergeFiles.Contains(p)) _mergeFiles.Add(p);
        SetStatus($"{_mergeFiles.Count}개 파일");
    }

    private void OnRemoveMergeFile(object? sender, RoutedEventArgs e)
    {
        if (MergeList.SelectedItem is string s) _mergeFiles.Remove(s);
    }

    private void OnMoveMergeUp(object? sender, RoutedEventArgs e) => MoveMerge(-1);
    private void OnMoveMergeDown(object? sender, RoutedEventArgs e) => MoveMerge(+1);

    private void MoveMerge(int delta)
    {
        var i = MergeList.SelectedIndex;
        var j = i + delta;
        if (i < 0 || j < 0 || j >= _mergeFiles.Count) return;
        _mergeFiles.Move(i, j);
        MergeList.SelectedIndex = j;
    }

    private void OnClearMerge(object? sender, RoutedEventArgs e) => _mergeFiles.Clear();

    private async void OnMergePdf(object? sender, RoutedEventArgs e)
    {
        if (_mergeFiles.Count < 2) { SetError("결합하려면 PDF 가 2개 이상 필요합니다."); return; }

        var output = await PickSaveFileAsync("결합 결과 저장", "merged.pdf", PdfType);
        if (output is null) return;

        try
        {
            SetStatus("결합 중…");
            var inputs = _mergeFiles.ToList();
            await Task.Run(() => PdfService.Merge(inputs, output));
            SetStatus($"완료: {output}");
        }
        catch (Exception ex) { SetError(ex.Message); }
    }

    // ---------- PDF 분해 / 추출 ----------

    private async void OnPickSplitPdf(object? sender, RoutedEventArgs e)
    {
        var path = await PickOpenFileAsync("PDF 선택", PdfType);
        if (path is null) return;
        SplitInput.Text = path;
        try { SplitPageInfo.Text = $"총 {PdfService.GetPageCount(path)} 페이지"; }
        catch (Exception ex) { SetError(ex.Message); }
    }

    private async void OnSplitAll(object? sender, RoutedEventArgs e)
    {
        var input = SplitInput.Text;
        if (string.IsNullOrWhiteSpace(input) || !File.Exists(input)) { SetError("PDF 파일을 먼저 선택하세요."); return; }

        var dir = await PickFolderAsync("분해한 파일을 저장할 폴더");
        if (dir is null) return;

        try
        {
            SetStatus("분해 중…");
            var files = await Task.Run(() => PdfService.SplitToSinglePages(input, dir));
            SetStatus($"완료: {files.Count}개 파일 → {dir}");
        }
        catch (Exception ex) { SetError(ex.Message); }
    }

    private async void OnExtractPages(object? sender, RoutedEventArgs e)
    {
        var input = SplitInput.Text;
        if (string.IsNullOrWhiteSpace(input) || !File.Exists(input)) { SetError("PDF 파일을 먼저 선택하세요."); return; }
        if (string.IsNullOrWhiteSpace(ExtractRange.Text)) { SetError("페이지 범위를 입력하세요. (예: 1-3,5)"); return; }

        var suggested = Path.GetFileNameWithoutExtension(input) + "_extract.pdf";
        var output = await PickSaveFileAsync("추출 결과 저장", suggested, PdfType);
        if (output is null) return;

        try
        {
            SetStatus("추출 중…");
            var range = ExtractRange.Text!;
            await Task.Run(() => PdfService.ExtractPages(input, output, range));
            SetStatus($"완료: {output}");
        }
        catch (Exception ex) { SetError(ex.Message); }
    }

    // ---------- PDF 크기 / 용량 ----------

    private async void OnPickPdfSize(object? sender, RoutedEventArgs e)
    {
        var path = await PickOpenFileAsync("PDF 선택", PdfType);
        if (path is null) return;
        PdfSizeInput.Text = path;
        CompressResult.Text = "";
        try { PdfSizeInfo.Text = $"현재 용량: {FormatBytes(new FileInfo(path).Length)}"; }
        catch { PdfSizeInfo.Text = ""; }
    }

    private async void OnResizePdf(object? sender, RoutedEventArgs e)
    {
        var input = PdfSizeInput.Text;
        if (string.IsNullOrWhiteSpace(input) || !File.Exists(input)) { SetError("PDF 파일을 먼저 선택하세요."); return; }

        var sizeText = (PageSizeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "A4";
        if (!Enum.TryParse<PageSize>(sizeText, out var pageSize)) pageSize = PageSize.A4;

        var suggested = Path.GetFileNameWithoutExtension(input) + $"_{sizeText}.pdf";
        var output = await PickSaveFileAsync("용지 크기 변경 결과 저장", suggested, PdfType);
        if (output is null) return;

        try
        {
            SetStatus("용지 크기 변경 중…");
            var keep = PdfKeepAspect.IsChecked == true;
            await Task.Run(() => PdfService.ResizePages(input, output, pageSize, keep));
            SetStatus($"완료: {output}");
        }
        catch (Exception ex) { SetError(ex.Message); }
    }

    private async void OnCompressPdf(object? sender, RoutedEventArgs e)
    {
        var input = PdfSizeInput.Text;
        if (string.IsNullOrWhiteSpace(input) || !File.Exists(input)) { SetError("PDF 파일을 먼저 선택하세요."); return; }

        // 슬라이더(줄이는 정도 %)를 품질·해상도로 환산 — 사용자는 한 값만 신경 쓰면 됨
        var strength = (int)Math.Round(ReduceSlider.Value);
        var (quality, maxDim) = StrengthToSettings(strength);

        var suggested = Path.GetFileNameWithoutExtension(input) + "_compressed.pdf";
        var output = await PickSaveFileAsync("용량 줄인 결과 저장", suggested, PdfType);
        if (output is null) return;

        try
        {
            SetStatus("용량 줄이는 중…");
            CompressResult.Text = "";
            var result = await Task.Run(() => PdfCompressor.Compress(input, output, quality, maxDim));
            var percent = (1 - result.Ratio) * 100;
            CompressResult.Text = result.ImagesRecompressed == 0
                ? $"{FormatBytes(result.OriginalBytes)} → {FormatBytes(result.NewBytes)} : 줄일 이미지가 없어 거의 그대로입니다(글자 위주 PDF)."
                : $"{FormatBytes(result.OriginalBytes)} → {FormatBytes(result.NewBytes)} " +
                  $"({percent:0.0}% 감소, 이미지 {result.ImagesRecompressed}개 처리)";
            SetStatus($"완료: {output}");
        }
        catch (Exception ex) { SetError(ex.Message); }
    }

    /// <summary>줄이는 정도(10~90%)를 JPEG 품질과 최대 해상도로 환산합니다.</summary>
    private static (int quality, int maxDimension) StrengthToSettings(int strength)
    {
        strength = Math.Clamp(strength, 10, 90);
        // 많이 줄일수록 품질↓(85→25), 해상도↓(2400→800). 약하게면 해상도 유지.
        var quality = (int)Math.Round(85 - (strength - 10) / 80.0 * 60);
        var maxDimension = strength <= 20 ? 0 : (int)Math.Round(2400 - (strength - 20) / 70.0 * 1600);
        return (Math.Clamp(quality, 25, 85), maxDimension);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double size = bytes;
        var u = 0;
        while (size >= 1024 && u < units.Length - 1) { size /= 1024; u++; }
        return $"{size:0.#} {units[u]}";
    }

    // ---------- 이미지 편집 ----------

    private void ReloadFontCombo(string? select)
    {
        var names = _fonts.FamilyNames();
        FontCombo.ItemsSource = names;
        var target = select ?? _fonts.Resolve(null).Name;
        FontCombo.SelectedItem = names.Contains(target) ? target : names.FirstOrDefault();
    }

    private void RefreshPreview()
    {
        if (_session is null) return;

        using var ms = new MemoryStream(_session.ToPngBytes());
        EditImage.Source = new Bitmap(ms);

        const double maxW = 660, maxH = 540;
        _displayScale = Math.Min(1.0, Math.Min(maxW / _session.Width, maxH / _session.Height));
        var dw = _session.Width * _displayScale;
        var dh = _session.Height * _displayScale;
        EditImage.Width = dw;
        EditImage.Height = dh;
        EditCanvas.Width = dw;
        EditCanvas.Height = dh;
        CropRect.IsVisible = false;
        EditInfo.Text = $"{_session.Width} × {_session.Height} px";
    }

    private (int X, int Y) ToImage(Avalonia.Point p) =>
        ((int)(p.X / _displayScale), (int)(p.Y / _displayScale));

    private async void OnEditLoad(object? sender, RoutedEventArgs e)
    {
        var path = await PickOpenFileAsync("편집할 이미지 선택", ImageType);
        if (path is null) return;
        try
        {
            _session?.Dispose();
            _session = ImageEditSession.Load(path);
            RefreshPreview();
            SetStatus($"불러옴: {path}");
        }
        catch (Exception ex) { SetError(ex.Message); }
    }

    private async void OnEditSave(object? sender, RoutedEventArgs e)
    {
        if (_session is null) { SetError("저장할 이미지가 없습니다."); return; }
        var output = await PickSaveFileAsync("편집 결과 저장", "edited.png", ImageType);
        if (output is null) return;
        try { _session.Save(output); SetStatus($"저장: {output}"); }
        catch (Exception ex) { SetError(ex.Message); }
    }

    private void OnEditRotateLeft(object? sender, RoutedEventArgs e) => EditOp(s => s.RotateLeft());
    private void OnEditRotateRight(object? sender, RoutedEventArgs e) => EditOp(s => s.RotateRight());
    private void OnEditFlipH(object? sender, RoutedEventArgs e) => EditOp(s => s.FlipHorizontal());
    private void OnEditFlipV(object? sender, RoutedEventArgs e) => EditOp(s => s.FlipVertical());

    private void EditOp(Action<ImageEditSession> op)
    {
        if (_session is null) { SetError("이미지를 먼저 불러오세요."); return; }
        try { op(_session); RefreshPreview(); }
        catch (Exception ex) { SetError(ex.Message); }
    }

    private void OnEditCrop(object? sender, RoutedEventArgs e)
    {
        if (_session is null) { SetError("이미지를 먼저 불러오세요."); return; }
        if (!CropRect.IsVisible || CropRect.Width < 2 || CropRect.Height < 2)
        { SetError("미리보기에서 영역을 드래그한 뒤 누르세요."); return; }

        var x = (int)(Canvas.GetLeft(CropRect) / _displayScale);
        var y = (int)(Canvas.GetTop(CropRect) / _displayScale);
        var w = (int)(CropRect.Width / _displayScale);
        var h = (int)(CropRect.Height / _displayScale);
        try
        {
            _session.Crop(new SixLabors.ImageSharp.Rectangle(x, y, w, h));
            RefreshPreview();
            SetStatus("자르기 완료");
        }
        catch (Exception ex) { SetError(ex.Message); }
    }

    private async void OnAddFont(object? sender, RoutedEventArgs e)
    {
        var path = await PickOpenFileAsync("폰트 파일 선택", FontType);
        if (path is null) return;
        try
        {
            var name = _fonts.AddFontFile(path);
            ReloadFontCombo(name);
            SetStatus($"폰트 추가: {name}");
        }
        catch (Exception ex) { SetError(ex.Message); }
    }

    private async void OnPickOverlay(object? sender, RoutedEventArgs e)
    {
        var path = await PickOpenFileAsync("얹을 이미지 선택", ImageType);
        if (path is null) return;
        _overlayPath = path;
        OverlayName.Text = Path.GetFileName(path);
    }

    // ----- 미리보기 포인터 -----

    private void OnCanvasPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_session is null) return;
        var p = e.GetPosition(EditCanvas);

        if (ModeCrop.IsChecked == true)
        {
            _dragging = true;
            _cropStart = p;
            Canvas.SetLeft(CropRect, p.X);
            Canvas.SetTop(CropRect, p.Y);
            CropRect.Width = 0;
            CropRect.Height = 0;
            CropRect.IsVisible = true;
        }
        else if (ModeText.IsChecked == true) PlaceText(p);
        else if (ModeImage.IsChecked == true) PlaceOverlay(p);
    }

    private void OnCanvasMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging || _session is null) return;
        var p = e.GetPosition(EditCanvas);
        Canvas.SetLeft(CropRect, Math.Min(p.X, _cropStart.X));
        Canvas.SetTop(CropRect, Math.Min(p.Y, _cropStart.Y));
        CropRect.Width = Math.Abs(p.X - _cropStart.X);
        CropRect.Height = Math.Abs(p.Y - _cropStart.Y);
    }

    private void OnCanvasReleased(object? sender, PointerReleasedEventArgs e) => _dragging = false;

    private void PlaceText(Avalonia.Point p)
    {
        if (_session is null) return;
        var text = TextContent.Text ?? "";
        if (string.IsNullOrEmpty(text)) { SetError("넣을 텍스트를 입력하세요."); return; }
        if (!float.TryParse(TextSize.Text, out var size) || size <= 0) { SetError("글자 크기가 올바르지 않습니다."); return; }
        if (!SixLabors.ImageSharp.Color.TryParseHex(TextColor.Text ?? "#000000", out var color))
            color = SixLabors.ImageSharp.Color.Black;
        try
        {
            var family = _fonts.Resolve(FontCombo.SelectedItem as string);
            var (ix, iy) = ToImage(p);
            _session.AddText(text, family, size, color, ix, iy);
            RefreshPreview();
            SetStatus($"텍스트 추가 @ ({ix},{iy})");
        }
        catch (Exception ex) { SetError(ex.Message); }
    }

    private void PlaceOverlay(Avalonia.Point p)
    {
        if (_session is null) return;
        if (_overlayPath is null) { SetError("먼저 얹을 이미지를 선택하세요."); return; }
        int.TryParse(OverlayW.Text, out var w);
        int.TryParse(OverlayH.Text, out var h);
        try
        {
            var (ix, iy) = ToImage(p);
            _session.AddImage(_overlayPath, ix, iy, w > 0 ? w : null, h > 0 ? h : null);
            RefreshPreview();
            SetStatus($"그림 추가 @ ({ix},{iy})");
        }
        catch (Exception ex) { SetError(ex.Message); }
    }
}
