using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.UI;
using Windows.UI.ViewManagement;
using WinRT.Interop;

namespace SimpleStickyNotes;


public sealed partial class NoteWindow : Window
{
    private const int WmNclButtonDown = 0x00A1;
    private const int HtCaption = 0x0002;

    private readonly NoteItem _note;
    private readonly Func<IReadOnlyList<string>> _getFontFamilies;
    private readonly Action _saveNotes;
    private readonly Action _showListWindow;
    private bool _isLoading;
    private bool _isTextLocked;
    private bool _isAlwaysOnTop;

    private SolidColorBrush backgroundBrush;
    private SolidColorBrush textBrush;

    public NoteWindow(
        NoteItem note,
        Func<IReadOnlyList<string>> getFontFamilies,
        Action saveNotes,
        Action showListWindow)
    {
        _note = note;
        _getFontFamilies = getFontFamilies;
        _saveNotes = saveNotes;
        _showListWindow = showListWindow;

        backgroundBrush = new();
        textBrush = new();

        InitializeComponent();
        SetSliderValue();
        ExtendsContentIntoTitleBar = true;
        BuildContextMenu();
        HideTitleBar();
        ResizeWindow(400, 400);
        LoadNote();
        ApplyLockState();
        SetTitleBar(HandleButton);
        DisableTitleBarDoubleClick();
        SetBackgroundColor();
        SetTextColor(IsLight(_note.BackgroundColor));
    }

    private void SetSliderValue()
    {
        FontSizeSlider.ValueChanged -= FontSizeSlider_ValueChanged;
        FontSizeSlider.Value = _note.FontSize;
        FontSizeSlider.ValueChanged += FontSizeSlider_ValueChanged;
    }

    public static byte[] HexToBytes(string hex)
    {
        hex = hex.TrimStart('#');

        byte r, g, b, a;

        if (hex.Length == 8) {
            a = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
            r = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
            g = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
            b = byte.Parse(hex.Substring(6, 2), System.Globalization.NumberStyles.HexNumber);
        } else {
            a = 255; // Default alpha value
            r = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
            g = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
            b = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
        }
        return [r, g, b, a];
    }

    public void SetBackgroundColor()
    {
        byte[] bytes = HexToBytes(_note.BackgroundColor);
        backgroundBrush.Color = Color.FromArgb(bytes[3], bytes[0], bytes[1], bytes[2]);
        RootGrid.Background = backgroundBrush;
    }

    public void RefreshFonts()
    {
        FontMenu.Items.Clear();
        LockedFontMenu.Items.Clear();
        BuildFontMenu(FontMenu);
        BuildFontMenu(LockedFontMenu);
    }

    private void BuildContextMenu()
    {
        BuildFontMenu(FontMenu);
        BuildFontMenu(LockedFontMenu);
    }

    private void BuildFontMenu(MenuFlyoutSubItem fontMenu)
    {
        foreach (var fontFamily in _getFontFamilies()) {
            var item = new MenuFlyoutItem { Text = GetFontDisplayName(fontFamily), Tag = fontFamily };
            item.Click += FontMenuItem_Click;
            fontMenu.Items.Add(item);
        }
    }

    private void LoadNote()
    {
        _isLoading = true;
        NoteBox.Text = _note.Text;
        FontSizeSlider.Value = Math.Clamp(_note.FontSize, 24, 144);
        ApplyFont(_note.FontFamily, _note.FontSize);
        _isLoading = false;
    }

    private void ApplyFont(string fontFamily, int fontSize)
    {
        var families = _getFontFamilies();
        if (families.Count > 0 && !families.Contains(fontFamily)) {
            fontFamily = families[0];
            _note.FontFamily = fontFamily;
        }

        if (string.IsNullOrWhiteSpace(fontFamily)) {
            fontFamily = "Segoe UI";
        }

        NoteBox.FontFamily = new FontFamily(fontFamily);
        NoteBox.FontSize = Math.Clamp(fontSize, 24, 144);
    }

    private static string GetFontDisplayName(string fontFamily)
    {
        var markerIndex = fontFamily.LastIndexOf('#');
        return markerIndex >= 0 && markerIndex < fontFamily.Length - 1
            ? fontFamily[(markerIndex + 1)..]
            : fontFamily;
    }

    private void ResizeWindow(int width, int height)
    {
        AppWindow.Resize(new SizeInt32(width, height));
    }

    private void HideTitleBar()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter) {
            presenter.SetBorderAndTitleBar(true, false);
        }
    }

    private void DisableTitleBarDoubleClick()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter) {
            presenter.IsMaximizable = false;
        }
    }

    private void ToggleTitleBar()
    {
        if (_isTextLocked) {
            this.SetTitleBar(RootGrid);
        } else {
            this.SetTitleBar(HandleButton);
        }
        DisableTitleBarDoubleClick();
    }

    private void StartWindowDrag()
    {
        var windowHandle = WindowNative.GetWindowHandle(this);
        ReleaseCapture();
        SendMessage(windowHandle, WmNclButtonDown, HtCaption, 0);
    }

    private void ApplyLockState()
    {
        NoteBox.IsReadOnly = _isTextLocked;
        HandleButton.IsEnabled = !_isTextLocked;
        CloseButton.IsEnabled = !_isTextLocked;
        FontSizeSlider.IsEnabled = !_isTextLocked;
        TitleBar.Visibility = _isTextLocked ? Visibility.Collapsed : Visibility.Visible;
        FontSizeSlider.Visibility = _isTextLocked ? Visibility.Collapsed : Visibility.Visible;
        //LockedBodyDragRegion.Visibility = _isTextLocked ? Visibility.Visible : Visibility.Collapsed;
        LockIcon.Glyph = _isTextLocked ? "\uE72E" : "\uE785";
    }

    //private void DragHandle_PointerPressed(object sender, PointerRoutedEventArgs e)
    //{
    //    if (e.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
    //    {
    //        StartWindowDrag();
    //    }
    //}

    private void LockedDragRegion_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_isTextLocked && e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) {
            StartWindowDrag();
        }
    }

    private void LockButton_Click(object sender, RoutedEventArgs e)
    {
        _isTextLocked = !_isTextLocked;
        ApplyLockState();
        ToggleTitleBar();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isTextLocked) {
            Close();
        }
    }

    private void OpenListMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _showListWindow();
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        _isAlwaysOnTop = !_isAlwaysOnTop;

        if (!_isAlwaysOnTop) {
            PinIcon.Glyph = "\uE718";
        } else {
            PinIcon.Glyph = "\uE77A";
        }

        if (AppWindow.Presenter is OverlappedPresenter presenter) {
            presenter.IsAlwaysOnTop = _isAlwaysOnTop;
        }
    }

    private void FontMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_isTextLocked || sender is not MenuFlyoutItem { Tag: string fontFamily }) {
            return;
        }

        _note.FontFamily = fontFamily;
        ApplyFont(_note.FontFamily, _note.FontSize);
        _saveNotes();
    }

    private void FontSizeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading || _isTextLocked || e.NewValue == 24) {
            return;
        }

        _note.FontSize = (int)e.NewValue;
        ApplyFont(_note.FontFamily, _note.FontSize);
        _saveNotes();
    }

    private void NoteBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading || _isTextLocked) {
            return;
        }

        _note.Text = NoteBox.Text;
        _saveNotes();
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    private void ColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        backgroundBrush.Color = args.NewColor;
        RootGrid.Background = backgroundBrush;

        _note.BackgroundColor = backgroundBrush.Color.ToString() ?? "EBC91E";
        _saveNotes();

        bool isLight = IsLight(backgroundBrush.Color.ToString());
        IsDarkBlock.Text = isLight.ToString();
        SetTextColor(isLight);
    }

    private static bool IsLight(string color)
    {
        byte[] bytes = HexToBytes(color);
        return 0.2126 * (int) bytes[0] + 0.7152 * (int) bytes[1] + 0.0722 * (int) bytes[2] > 128;
    }

    readonly Color BLACK = Color.FromArgb(255, 16, 16, 16);
    readonly Color WHITE = Color.FromArgb(255, 255, 255, 255);
    private void SetTextColor(bool isBlack)
    {
        Color textColor = isBlack ? BLACK : WHITE;

        //RootGridFontIconStyleSetter.Value = textColor;
        textBrush.Color = textColor;
        NoteBox.Foreground = textBrush;
        TextColor1.Color = textColor;
        TextColor2.Color = textColor;
        TextColor3.Color = textColor;
        NoteBox.Text = NoteBox.Text + " ";
        NoteBox.Text = NoteBox.Text[..^1];
        HandleButton.Foreground = textBrush;
        PinButton.Foreground = textBrush;
        ColorButton.Foreground = textBrush;
        CloseButton.Foreground = textBrush;
        LockButton.Foreground = textBrush;
        IconColor1.Color = textColor;
        IconColor2.Color = textColor;
        IconColor3.Color = textColor;
        IconColor4.Color = textColor;
        IconColor5.Color = textColor;
        IconColor6.Color = textColor;
    }
}
