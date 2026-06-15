using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace ImageEditor.App;

public enum OverlayKind { Text, Image }

/// <summary>
/// 미리보기 위에 떠 있는 편집 객체(텍스트/이미지/도장).
/// 확정 전까지 드래그로 이동, 우하단 모서리 핸들로 크기를 조절할 수 있습니다.
/// 좌표/크기는 화면(표시) 픽셀 기준이며, 확정 시 Core 가 이미지/PDF 좌표로 환산합니다.
/// </summary>
public class OverlayItem : Border
{
    public OverlayKind Kind { get; }
    public string? Text { get; }
    public string FontFamilyName { get; }
    public string ColorHex { get; }
    public string? ImagePath { get; }
    public double DisplayFontSize { get; private set; }
    public double DisplayWidth { get; private set; }
    public double AspectRatio { get; }

    public event EventHandler? Selected;

    private readonly TextBlock? _textBlock;
    private readonly Image? _image;
    private readonly Rectangle _handle;

    private enum Drag { None, Move, Resize }
    private Drag _drag = Drag.None;
    private Point _last;

    private OverlayItem(OverlayKind kind, Control content,
        string? text, string fontFamily, string colorHex, double displayFontSize,
        string? imagePath, double displayWidth, double aspectRatio)
    {
        Kind = kind;
        Text = text;
        FontFamilyName = fontFamily;
        ColorHex = colorHex;
        DisplayFontSize = displayFontSize;
        ImagePath = imagePath;
        DisplayWidth = displayWidth;
        AspectRatio = aspectRatio;

        _textBlock = content as TextBlock;
        _image = content as Image;

        BorderThickness = new Thickness(1);
        BorderBrush = Brushes.Transparent;
        Background = Brushes.Transparent;
        Padding = new Thickness(0);
        Cursor = new Cursor(StandardCursorType.SizeAll);

        _handle = new Rectangle
        {
            Width = 14,
            Height = 14,
            Fill = new SolidColorBrush(Color.Parse("#00B4FF")),
            Stroke = Brushes.White,
            StrokeThickness = 1,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            IsVisible = false,
            Cursor = new Cursor(StandardCursorType.BottomRightCorner)
        };

        content.IsHitTestVisible = false;
        var grid = new Grid();
        grid.Children.Add(content);
        grid.Children.Add(_handle);
        Child = grid;

        PointerPressed += OnBodyPressed;
        PointerMoved += OnMoved;
        PointerReleased += OnReleased;
        _handle.PointerPressed += OnHandlePressed;
    }

    public static OverlayItem CreateText(string text, string fontFamily, double displayFontSize, string colorHex)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = displayFontSize,
            FontFamily = new FontFamily(fontFamily),
            Foreground = new SolidColorBrush(ParseColor(colorHex))
        };
        return new OverlayItem(OverlayKind.Text, tb, text, fontFamily, colorHex, displayFontSize, null, 0, 0);
    }

    public static OverlayItem CreateImage(string imagePath, Bitmap bitmap, double displayWidth)
    {
        var aspect = bitmap.PixelSize.Width > 0 ? bitmap.PixelSize.Height / (double)bitmap.PixelSize.Width : 1.0;
        var img = new Image
        {
            Source = bitmap,
            Width = displayWidth,
            Height = displayWidth * aspect,
            Stretch = Stretch.Fill
        };
        return new OverlayItem(OverlayKind.Image, img, null, "", "", 0, imagePath, displayWidth, aspect);
    }

    public void SetSelected(bool selected)
    {
        BorderBrush = selected ? new SolidColorBrush(Color.Parse("#00B4FF")) : Brushes.Transparent;
        _handle.IsVisible = selected;
    }

    private void OnBodyPressed(object? sender, PointerPressedEventArgs e)
    {
        Selected?.Invoke(this, EventArgs.Empty);
        _drag = Drag.Move;
        _last = e.GetPosition(Parent as Visual);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void OnHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        Selected?.Invoke(this, EventArgs.Empty);
        _drag = Drag.Resize;
        _last = e.GetPosition(Parent as Visual);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_drag == Drag.None) return;
        var pos = e.GetPosition(Parent as Visual);
        var dx = pos.X - _last.X;
        var dy = pos.Y - _last.Y;
        _last = pos;

        if (_drag == Drag.Move)
        {
            Canvas.SetLeft(this, Canvas.GetLeft(this) + dx);
            Canvas.SetTop(this, Canvas.GetTop(this) + dy);
        }
        else if (_drag == Drag.Resize)
        {
            if (Kind == OverlayKind.Text && _textBlock is not null)
            {
                DisplayFontSize = Math.Clamp(DisplayFontSize + dy, 6, 4000);
                _textBlock.FontSize = DisplayFontSize;
            }
            else if (_image is not null)
            {
                DisplayWidth = Math.Clamp(DisplayWidth + dx, 12, 8000);
                _image.Width = DisplayWidth;
                _image.Height = DisplayWidth * AspectRatio;
            }
        }
        e.Handled = true;
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_drag == Drag.None) return;
        _drag = Drag.None;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private static Color ParseColor(string? hex)
    {
        try { return string.IsNullOrWhiteSpace(hex) ? Colors.Black : Color.Parse(hex); }
        catch { return Colors.Black; }
    }
}
