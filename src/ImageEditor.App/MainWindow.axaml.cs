using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
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

    private readonly ObservableCollection<string> _mergeFiles = new();

    public MainWindow()
    {
        InitializeComponent();
        MergeList.ItemsSource = _mergeFiles;
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

    // ---------- PDF 크기 변경 ----------

    private async void OnPickResizePdf(object? sender, RoutedEventArgs e)
    {
        var path = await PickOpenFileAsync("PDF 선택", PdfType);
        if (path is not null) PdfResizeInput.Text = path;
    }

    private async void OnResizePdf(object? sender, RoutedEventArgs e)
    {
        var input = PdfResizeInput.Text;
        if (string.IsNullOrWhiteSpace(input) || !File.Exists(input)) { SetError("PDF 파일을 먼저 선택하세요."); return; }

        var sizeText = (PageSizeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "A4";
        if (!Enum.TryParse<PageSize>(sizeText, out var pageSize)) pageSize = PageSize.A4;

        var suggested = Path.GetFileNameWithoutExtension(input) + $"_{sizeText}.pdf";
        var output = await PickSaveFileAsync("크기 변경 결과 저장", suggested, PdfType);
        if (output is null) return;

        try
        {
            SetStatus("크기 변경 중…");
            var keep = PdfKeepAspect.IsChecked == true;
            await Task.Run(() => PdfService.ResizePages(input, output, pageSize, keep));
            SetStatus($"완료: {output}");
        }
        catch (Exception ex) { SetError(ex.Message); }
    }
}
