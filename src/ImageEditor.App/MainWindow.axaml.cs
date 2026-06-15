using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
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

    // 도장 만들기 상태
    private string? _stampPath;      // 만들어진 도장 PNG(임시 파일)
    private string? _stampScanPath;  // 투명화할 스캔 원본

    // 오버레이 객체(확정 전) 선택 상태
    private OverlayItem? _selectedImageOverlay;
    private OverlayItem? _selectedPdfOverlay;

    // 미리보기 줌(휠 확대/축소)
    private double _editZoom = 1.0;
    private double _pdfZoom = 1.0;

    // 우클릭 드래그 화면 이동(팬)
    private bool _panning;
    private Avalonia.Point _panLast;

    // PDF 편집 상태
    private const double PdfRenderScaling = 2.0;
    private PdfEditSession? _pdfSession;
    private int _pdfPageIndex;
    private double _pdfRenderScale = 1.0; // 렌더 픽셀 / 포인트
    private double _pdfFit = 1.0;         // 표시 픽셀 / 렌더 픽셀
    private string? _pdfOverlayPath;

    public MainWindow()
    {
        InitializeComponent();
        MergeList.ItemsSource = _mergeFiles;
        ReloadFontCombo(null);
        LoadStampWatermark();
        KeyDown += OnGlobalKeyDown;
    }

    // 도장 미리보기 배경에 teaveloper 엠블럼을 깔아 투명 영역을 확인할 수 있게 함
    private void LoadStampWatermark()
    {
        try
        {
            var asm = typeof(MainWindow).Assembly.GetName().Name;
            using var s = AssetLoader.Open(new Uri($"avares://{asm}/Assets/teaveloper-emblem.png"));
            StampWatermark.Source = new Bitmap(s);
        }
        catch { /* 리소스 없으면 무시 */ }
    }

    private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || (e.Key != Key.Delete && e.Key != Key.Back)) return;
        // 텍스트 입력 중에는 글자 삭제로 동작하도록 가로채지 않음
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox) return;

        var removed = MainTabs.SelectedItem == PdfEditTab ? DeleteSelectedPdfOverlay()
                    : MainTabs.SelectedItem == ImageEditTab ? DeleteSelectedImageOverlay()
                    : false;
        if (removed) e.Handled = true;
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
        StampFontCombo.ItemsSource = names;
        var target = select ?? _fonts.Resolve(null).Name;
        var chosen = names.Contains(target) ? target : names.FirstOrDefault();
        FontCombo.SelectedItem = chosen;
        StampFontCombo.SelectedItem = chosen;
    }

    // ---------- 도장 만들기 ----------

    private static string StampColorHex(int index) => index == 1 ? "#14328C" : "#C8102E"; // 0:붉은색 1:푸른색

    private StampShape ResolveStampShape(string text)
    {
        if (StampShapeCircle.IsChecked == true) return StampShape.Circle;
        if (StampShapeSquare.IsChecked == true) return StampShape.Square;
        // 자동: 끝의 '인'(印)을 빼고 4자 초과(5자 이상)면 사각(단체), 그 이하는 원형
        var core = StampMaker.CharacterCount(text);
        if (text.TrimEnd().EndsWith("인")) core -= 1;
        return core > 4 ? StampShape.Square : StampShape.Circle;
    }

    private void OnMakeStamp(object? sender, RoutedEventArgs e)
    {
        var text = StampText.Text;
        if (string.IsNullOrWhiteSpace(text)) { SetError("도장에 넣을 글자를 입력하세요."); return; }
        try
        {
            var family = _fonts.Resolve(StampFontCombo.SelectedItem as string);
            var shape = ResolveStampShape(text!);
            var color = StampColorHex(StampColorCombo.SelectedIndex);
            var png = StampMaker.CreateTextStampPng(text!, family, color, shape, 320);
            StoreStamp(png);
        }
        catch (Exception ex) { SetError(ex.Message); }
    }

    private async void OnPickStampScan(object? sender, RoutedEventArgs e)
    {
        var path = await PickOpenFileAsync("스캔한 직인 이미지 선택", ImageType);
        if (path is null) return;
        _stampScanPath = path;
        StampScanName.Text = Path.GetFileName(path);
        try { StampPreview.Source = new Bitmap(path); } catch (Exception ex) { SetError(ex.Message); }
        StampReady.IsChecked = false; // 아직 투명화 전
    }

    private void OnMakeStampFromScan(object? sender, RoutedEventArgs e)
    {
        if (_stampScanPath is null || !File.Exists(_stampScanPath)) { SetError("먼저 스캔 이미지를 선택하세요."); return; }
        try
        {
            var threshold = (int)Math.Round(StampThreshold.Value);
            string? recolor = StampRecolorOn.IsChecked == true ? StampColorHex(StampRecolorCombo.SelectedIndex) : null;
            var png = StampMaker.MakeTransparentPng(_stampScanPath, threshold, recolor);
            StoreStamp(png);
        }
        catch (Exception ex) { SetError(ex.Message); }
    }

    private void StoreStamp(byte[] png)
    {
        _stampPath = Path.Combine(Path.GetTempPath(), $"stamp_{Guid.NewGuid():N}.png");
        File.WriteAllBytes(_stampPath, png);
        using (var ms = new MemoryStream(png))
            StampPreview.Source = new Bitmap(ms);
        StampReady.IsChecked = true;
        StampInfo.Text = "도장이 만들어졌습니다. 'PNG로 저장'으로 내보낸 뒤 편집 탭에서 쓰세요.";
        SetStatus("도장 생성 완료");
    }

    private async void OnSaveStamp(object? sender, RoutedEventArgs e)
    {
        if (_stampPath is null || !File.Exists(_stampPath)) { SetError("먼저 도장을 만드세요."); return; }
        var output = await PickSaveFileAsync("도장 PNG 저장", "stamp.png", ImageType);
        if (output is null) return;
        try { File.Copy(_stampPath, output, overwrite: true); SetStatus($"저장: {output}"); }
        catch (Exception ex) { SetError(ex.Message); }
    }

    private void RefreshPreview()
    {
        if (_session is null) return;

        using var ms = new MemoryStream(_session.ToPngBytes());
        EditImage.Source = new Bitmap(ms);

        // 폭에 맞춰 표시(세로는 넘치면 스크롤). 확대/축소는 휠 줌으로.
        _displayScale = ViewportWidth(EditScroll) / _session.Width;
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

    // ----- 미리보기 폭 맞춤 / 휠 줌 -----

    private static double ViewportWidth(ScrollViewer sv)
    {
        var w = sv.Viewport.Width;
        if (w < 10) w = sv.Bounds.Width;
        // 세로 스크롤바가 생겨도 가로 스크롤바가 불필요하게 뜨지 않도록 여유분 확보
        return w > 30 ? w - 18 : 560;
    }

    private void SetEditZoom(double z)
    {
        _editZoom = Math.Clamp(z, 0.2, 8.0);
        EditZoom.LayoutTransform = new ScaleTransform(_editZoom, _editZoom);
    }

    private void OnEditWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_session is null) return;
        SetEditZoom(_editZoom * (e.Delta.Y > 0 ? 1.1 : 1.0 / 1.1));
        e.Handled = true;
    }

    private void SetPdfZoom(double z)
    {
        _pdfZoom = Math.Clamp(z, 0.2, 8.0);
        PdfZoom.LayoutTransform = new ScaleTransform(_pdfZoom, _pdfZoom);
    }

    private void OnPdfWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_pdfSession is null) return;
        SetPdfZoom(_pdfZoom * (e.Delta.Y > 0 ? 1.1 : 1.0 / 1.1));
        e.Handled = true;
    }

    private async void OnEditLoad(object? sender, RoutedEventArgs e)
    {
        var path = await PickOpenFileAsync("편집할 이미지 선택", ImageType);
        if (path is null) return;
        try
        {
            _session?.Dispose();
            _session = ImageEditSession.Load(path);
            ClearOverlays(EditCanvas);
            _selectedImageOverlay = null;
            SetEditZoom(1.0);
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
        try { BakeImageOverlays(); RefreshPreview(); _session.Save(output); SetStatus($"저장: {output}"); }
        catch (Exception ex) { SetError(ex.Message); }
    }

    private void OnEditRotateLeft(object? sender, RoutedEventArgs e) => EditOp(s => s.RotateLeft());
    private void OnEditRotateRight(object? sender, RoutedEventArgs e) => EditOp(s => s.RotateRight());
    private void OnEditFlipH(object? sender, RoutedEventArgs e) => EditOp(s => s.FlipHorizontal());
    private void OnEditFlipV(object? sender, RoutedEventArgs e) => EditOp(s => s.FlipVertical());

    private void EditOp(Action<ImageEditSession> op)
    {
        if (_session is null) { SetError("이미지를 먼저 불러오세요."); return; }
        try { BakeImageOverlays(); op(_session); RefreshPreview(); }
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
            BakeImageOverlays();
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
        var props = e.GetCurrentPoint(EditCanvas).Properties;

        if (props.IsRightButtonPressed) { StartPan(EditScroll, EditCanvas, e); return; }
        if (!props.IsLeftButtonPressed) return;

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
        else if (ModeText.IsChecked == true) AddImageTextObject(p);
        else if (ModeImage.IsChecked == true) AddImageImageObject(p);
    }

    private void OnCanvasMoved(object? sender, PointerEventArgs e)
    {
        if (DoPan(EditScroll, e)) return;
        if (!_dragging || _session is null) return;
        var p = e.GetPosition(EditCanvas);
        Canvas.SetLeft(CropRect, Math.Min(p.X, _cropStart.X));
        Canvas.SetTop(CropRect, Math.Min(p.Y, _cropStart.Y));
        CropRect.Width = Math.Abs(p.X - _cropStart.X);
        CropRect.Height = Math.Abs(p.Y - _cropStart.Y);
    }

    private void OnCanvasReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (EndPan(e)) return;
        if (!_dragging) return;
        _dragging = false;
        // 드래그가 거의 없으면(단순 클릭) 선택 영역을 초기화 — 일반 편집기처럼
        if (CropRect.Width < 3 || CropRect.Height < 3)
            CropRect.IsVisible = false;
    }

    // ----- 우클릭 드래그 화면 이동(팬), 이미지·PDF 공용 -----

    private void StartPan(ScrollViewer sv, Control captureTarget, PointerPressedEventArgs e)
    {
        _panning = true;
        _panLast = e.GetPosition(sv);
        e.Pointer.Capture(captureTarget);
        e.Handled = true;
    }

    private bool DoPan(ScrollViewer sv, PointerEventArgs e)
    {
        if (!_panning) return false;
        var pos = e.GetPosition(sv);
        sv.Offset -= pos - _panLast; // Point - Point = Vector
        _panLast = pos;
        e.Handled = true;
        return true;
    }

    private bool EndPan(PointerReleasedEventArgs e)
    {
        if (!_panning) return false;
        _panning = false;
        e.Pointer.Capture(null);
        e.Handled = true;
        return true;
    }

    // ----- 오버레이 객체(이미지 편집) -----

    private void AddImageTextObject(Avalonia.Point p)
    {
        var text = TextContent.Text ?? "";
        if (string.IsNullOrEmpty(text)) { SetError("넣을 텍스트를 입력하세요."); return; }
        if (!double.TryParse(TextSize.Text, out var sizeImg) || sizeImg <= 0) { SetError("글자 크기가 올바르지 않습니다."); return; }
        var item = OverlayItem.CreateText(text, (FontCombo.SelectedItem as string) ?? "", sizeImg * _displayScale, TextColor.Text ?? "#000000");
        AddOverlay(EditCanvas, p, item, isPdf: false);
        SetStatus("객체 추가됨 — 드래그로 이동, 모서리로 크기조절 후 '적용'");
    }

    private void AddImageImageObject(Avalonia.Point p)
    {
        if (_overlayPath is null) { SetError("먼저 얹을 이미지를 선택하세요."); return; }
        Bitmap bmp;
        try { bmp = new Bitmap(_overlayPath); }
        catch (Exception ex) { SetError(ex.Message); return; }
        int.TryParse(OverlayW.Text, out var wImg);
        var displayW = wImg > 0 ? wImg * _displayScale : Math.Min(bmp.PixelSize.Width * _displayScale, 220);
        var item = OverlayItem.CreateImage(_overlayPath, bmp, displayW);
        AddOverlay(EditCanvas, p, item, isPdf: false);
        SetStatus("객체 추가됨 — 드래그로 이동, 모서리로 크기조절 후 '적용'");
    }

    private void AddOverlay(Canvas canvas, Avalonia.Point p, OverlayItem item, bool isPdf)
    {
        Canvas.SetLeft(item, p.X);
        Canvas.SetTop(item, p.Y);
        item.Selected += (s, _) => SelectOverlay(canvas, (OverlayItem)s!, isPdf);
        canvas.Children.Add(item);
        SelectOverlay(canvas, item, isPdf);
    }

    private void SelectOverlay(Canvas canvas, OverlayItem item, bool isPdf)
    {
        foreach (var c in canvas.Children)
            if (c is OverlayItem oi) oi.SetSelected(oi == item);
        if (isPdf) _selectedPdfOverlay = item; else _selectedImageOverlay = item;
    }

    private static void ClearOverlays(Canvas canvas)
    {
        foreach (var oi in canvas.Children.OfType<OverlayItem>().ToList())
            canvas.Children.Remove(oi);
    }

    private static SixLabors.ImageSharp.Color ParseImgColor(string? hex)
        => SixLabors.ImageSharp.Color.TryParseHex(hex ?? "#000000", out var c) ? c : SixLabors.ImageSharp.Color.Black;

    private void BakeImageOverlays()
    {
        if (_session is null) return;
        foreach (var oi in EditCanvas.Children.OfType<OverlayItem>().ToList())
        {
            var (ix, iy) = ToImage(new Avalonia.Point(Canvas.GetLeft(oi), Canvas.GetTop(oi)));
            if (oi.Kind == OverlayKind.Text)
            {
                var family = _fonts.Resolve(oi.FontFamilyName);
                var sizeImg = (float)(oi.DisplayFontSize / _displayScale);
                _session.AddText(oi.Text ?? "", family, sizeImg, ParseImgColor(oi.ColorHex), ix, iy);
            }
            else if (oi.ImagePath is not null)
            {
                var wImg = (int)Math.Round(oi.DisplayWidth / _displayScale);
                _session.AddImage(oi.ImagePath, ix, iy, wImg > 0 ? wImg : null, null);
            }
            EditCanvas.Children.Remove(oi);
        }
        _selectedImageOverlay = null;
    }

    private void OnApplyImageOverlays(object? sender, RoutedEventArgs e)
    {
        if (_session is null) { SetError("이미지를 먼저 불러오세요."); return; }
        if (!EditCanvas.Children.OfType<OverlayItem>().Any()) { SetError("적용할 객체가 없습니다."); return; }
        try { BakeImageOverlays(); RefreshPreview(); SetStatus("적용 완료"); }
        catch (Exception ex) { SetError(ex.Message); }
    }

    private void OnDeleteImageOverlay(object? sender, RoutedEventArgs e)
    {
        if (!DeleteSelectedImageOverlay()) SetError("삭제할 객체를 선택하세요.");
    }

    private bool DeleteSelectedImageOverlay()
    {
        if (_selectedImageOverlay is null) return false;
        EditCanvas.Children.Remove(_selectedImageOverlay);
        _selectedImageOverlay = null;
        return true;
    }

    // ---------- PDF 편집 ----------

    private void ReloadPdfFontCombo(string? select)
    {
        if (_pdfSession is null) return;
        var names = _pdfSession.Fonts.Families;
        PdfFontCombo.ItemsSource = names;
        var target = select ?? _pdfSession.Fonts.DefaultFamily;
        PdfFontCombo.SelectedItem = names.Contains(target) ? target : names.FirstOrDefault();
    }

    private void RenderCurrentPdfPage()
    {
        if (_pdfSession is null) return;

        var rp = _pdfSession.RenderPage(_pdfPageIndex, PdfRenderScaling);
        using (var ms = new MemoryStream(rp.Png))
            PdfPageImage.Source = new Bitmap(ms);

        // 폭에 맞춰 표시. 확대/축소는 휠 줌으로.
        _pdfFit = ViewportWidth(PdfScroll) / rp.PixelWidth;
        _pdfRenderScale = rp.Scale;

        var dw = rp.PixelWidth * _pdfFit;
        var dh = rp.PixelHeight * _pdfFit;
        PdfPageImage.Width = dw;
        PdfPageImage.Height = dh;
        PdfCanvas.Width = dw;
        PdfCanvas.Height = dh;

        PdfPageLabel.Text = $"{_pdfPageIndex + 1} / {_pdfSession.PageCount}";
        var (wpt, hpt) = _pdfSession.PageSize(_pdfPageIndex);
        PdfEditInfo.Text = $"페이지 크기 {wpt:0}×{hpt:0} pt";
    }

    private (double X, double Y) ToPagePoint(Avalonia.Point p) =>
        (p.X / (_pdfFit * _pdfRenderScale), p.Y / (_pdfFit * _pdfRenderScale));

    private async void OnPdfEditLoad(object? sender, RoutedEventArgs e)
    {
        var path = await PickOpenFileAsync("편집할 PDF 선택", PdfType);
        if (path is null) return;
        try
        {
            _pdfSession?.Dispose();
            _pdfSession = PdfEditSession.Load(path);
            _pdfPageIndex = 0;
            ClearOverlays(PdfCanvas);
            _selectedPdfOverlay = null;
            SetPdfZoom(1.0);
            ReloadPdfFontCombo(null);
            RenderCurrentPdfPage();
            SetStatus($"불러옴: {path}");
        }
        catch (Exception ex) { SetError(ex.Message); }
    }

    private async void OnPdfEditSave(object? sender, RoutedEventArgs e)
    {
        if (_pdfSession is null) { SetError("저장할 PDF가 없습니다."); return; }
        var output = await PickSaveFileAsync("편집한 PDF 저장", "edited.pdf", PdfType);
        if (output is null) return;
        try { BakePdfOverlays(); _pdfSession.Save(output); SetStatus($"저장: {output}"); }
        catch (Exception ex) { SetError(ex.Message); }
    }

    private void OnPdfPrevPage(object? sender, RoutedEventArgs e)
    {
        if (_pdfSession is null || _pdfPageIndex <= 0) return;
        BakePdfOverlays(); // 현재 페이지에 먼저 확정
        _pdfPageIndex--;
        RenderCurrentPdfPage();
    }

    private void OnPdfNextPage(object? sender, RoutedEventArgs e)
    {
        if (_pdfSession is null || _pdfPageIndex >= _pdfSession.PageCount - 1) return;
        BakePdfOverlays();
        _pdfPageIndex++;
        RenderCurrentPdfPage();
    }

    private async void OnPdfAddFont(object? sender, RoutedEventArgs e)
    {
        if (_pdfSession is null) { SetError("PDF를 먼저 불러오세요."); return; }
        var path = await PickOpenFileAsync("폰트 파일 선택", FontType);
        if (path is null) return;
        try
        {
            var name = _pdfSession.Fonts.RegisterFontFile(path);
            ReloadPdfFontCombo(name);
            SetStatus($"폰트 추가: {name}");
        }
        catch (Exception ex) { SetError(ex.Message); }
    }

    private async void OnPickPdfOverlay(object? sender, RoutedEventArgs e)
    {
        var path = await PickOpenFileAsync("얹을 이미지/서명 선택", ImageType);
        if (path is null) return;
        _pdfOverlayPath = path;
        PdfOverlayName.Text = Path.GetFileName(path);
    }

    private double PdfFactor() => _pdfFit * _pdfRenderScale; // 표시 픽셀 / 포인트

    private void OnPdfCanvasPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_pdfSession is null) { SetError("PDF를 먼저 불러오세요."); return; }
        var props = e.GetCurrentPoint(PdfCanvas).Properties;

        if (props.IsRightButtonPressed) { StartPan(PdfScroll, PdfCanvas, e); return; }
        if (!props.IsLeftButtonPressed) return;

        var p = e.GetPosition(PdfCanvas);
        if (ModePdfText.IsChecked == true) AddPdfTextObject(p);
        else if (ModePdfImage.IsChecked == true) AddPdfImageObject(p);
    }

    private void OnPdfCanvasMoved(object? sender, PointerEventArgs e) => DoPan(PdfScroll, e);

    private void OnPdfCanvasReleased(object? sender, PointerReleasedEventArgs e) => EndPan(e);

    private void AddPdfTextObject(Avalonia.Point p)
    {
        var text = PdfTextContent.Text ?? "";
        if (string.IsNullOrEmpty(text)) { SetError("넣을 텍스트를 입력하세요."); return; }
        if (!double.TryParse(PdfTextSize.Text, out var sizePt) || sizePt <= 0) { SetError("글자 크기가 올바르지 않습니다."); return; }
        var item = OverlayItem.CreateText(text, (PdfFontCombo.SelectedItem as string) ?? "", sizePt * PdfFactor(), PdfTextColor.Text ?? "#000000");
        AddOverlay(PdfCanvas, p, item, isPdf: true);
        SetStatus("객체 추가됨 — 드래그로 이동, 모서리로 크기조절 후 '적용'");
    }

    private void AddPdfImageObject(Avalonia.Point p)
    {
        if (_pdfOverlayPath is null) { SetError("먼저 얹을 이미지를 선택하세요."); return; }
        Bitmap bmp;
        try { bmp = new Bitmap(_pdfOverlayPath); }
        catch (Exception ex) { SetError(ex.Message); return; }
        double.TryParse(PdfOverlayW.Text, out var wpt);
        var displayW = (wpt > 0 ? wpt : 120) * PdfFactor();
        var item = OverlayItem.CreateImage(_pdfOverlayPath, bmp, displayW);
        AddOverlay(PdfCanvas, p, item, isPdf: true);
        SetStatus("객체 추가됨 — 드래그로 이동, 모서리로 크기조절 후 '적용'");
    }

    private void BakePdfOverlays()
    {
        if (_pdfSession is null) return;
        var factor = PdfFactor();
        foreach (var oi in PdfCanvas.Children.OfType<OverlayItem>().ToList())
        {
            var xPt = Canvas.GetLeft(oi) / factor;
            var yPt = Canvas.GetTop(oi) / factor;
            if (oi.Kind == OverlayKind.Text)
                _pdfSession.AddText(_pdfPageIndex, oi.Text ?? "", xPt, yPt, oi.FontFamilyName, oi.DisplayFontSize / factor, oi.ColorHex);
            else if (oi.ImagePath is not null)
                _pdfSession.AddImage(_pdfPageIndex, oi.ImagePath, xPt, yPt, oi.DisplayWidth / factor);
            PdfCanvas.Children.Remove(oi);
        }
        _selectedPdfOverlay = null;
    }

    private void OnApplyPdfOverlays(object? sender, RoutedEventArgs e)
    {
        if (_pdfSession is null) { SetError("PDF를 먼저 불러오세요."); return; }
        if (!PdfCanvas.Children.OfType<OverlayItem>().Any()) { SetError("적용할 객체가 없습니다."); return; }
        try { BakePdfOverlays(); RenderCurrentPdfPage(); SetStatus("적용 완료"); }
        catch (Exception ex) { SetError(ex.Message); }
    }

    private void OnDeletePdfOverlay(object? sender, RoutedEventArgs e)
    {
        if (!DeleteSelectedPdfOverlay()) SetError("삭제할 객체를 선택하세요.");
    }

    private bool DeleteSelectedPdfOverlay()
    {
        if (_selectedPdfOverlay is null) return false;
        PdfCanvas.Children.Remove(_selectedPdfOverlay);
        _selectedPdfOverlay = null;
        return true;
    }
}
