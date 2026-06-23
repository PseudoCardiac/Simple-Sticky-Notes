using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using Windows.Graphics;
using Windows.UI;

namespace SimpleStickyNotes;


public sealed partial class NoteWindow : Window, INotifyPropertyChanged
{
    private readonly NoteItem _note;
    private readonly Func<IReadOnlyList<string>> _getFontFamilies;
    private readonly Action _saveNotes;
    private readonly Action _showListWindow;
    private bool _isLoading;
    private bool _isTextLocked;
    private bool _isAlwaysOnTop;

    private SolidColorBrush backgroundBrush;
    private SolidColorBrush textBrush;

    private string _fontFamily;
    private string _fontWeight;
    private string _fontStyle;
    private string _fontStretch;
    private int _fontSize;

    private Thickness _topPadding;
    private Thickness _noTopPadding;

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

        _fontFamily = note.FontFamily;
        _fontWeight = note.FontWeight;
        _fontStyle = note.FontStyle;
        _fontStretch = note.FontStretch;
        _fontSize = note.FontSize;

        _topPadding = new();
        _noTopPadding = new();
        _topPadding.Top = 50;

        backgroundBrush = new();
        textBrush = new();

        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        BuildFontMenu();
        HideTitleBar();
        ResizeWindow(400, 400);
        LoadNote();
        ApplyLockState();
        SetTitleBar(LockedTitleDragRegion);
        DisableTitleBarDoubleClick();
        SetBackgroundColor();
        SetTextColor(IsLight(_note.BackgroundColor));
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    public string NoteFontFamily
    {
        get => _fontFamily;
        set
        {
            if (_fontFamily == value) {
                return;
            }

            _fontFamily = value;
            OnPropertyChanged();
        }
    }
    public string NoteFontWeight
    {
        get => _fontWeight;
        set
        {
            if (_fontWeight == value) {
                return;
            }

            _fontWeight = value;
            OnPropertyChanged();
        }
    }
    public string NoteFontStyle
    {
        get => _fontStyle;
        set
        {
            if (_fontStyle == value) {
                return;
            }

            _fontStyle = value;
            OnPropertyChanged();
        }
    }
    public string NoteFontStretch
    {
        get => _fontStretch;
        set
        {
            if (_fontStretch == value) {
                return;
            }

            _fontStretch = value;
            OnPropertyChanged();
        }
    }
    public int NoteFontSize
    {
        get => _fontSize;
        set
        {
            if (Math.Abs(_fontSize - value) < 0.01) {
                return;
            }

            _fontSize = Math.Clamp(value, 24, 144);
            OnPropertyChanged();
        }
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
        BuildFontMenu();
    }

    private readonly List<string> _fontWeights = [
        "Thin",
        "ExtraLight",
        "Light",
        "SemiLight",
        "Normal",
        "Medium",
        "SemiBold",
        "Bold",
        "ExtraBold",
        "Black",
        "ExtraBlack"
    ];
    private readonly List<string> _fontStyles = [
        "Normal",
        "Italic",
        "Oblique"
    ];
    private readonly List<string> _fontStretches = [
        "UltraCondensed",
        "ExtraCondensed",
        "Condensed",
        "SemiCondensed",
        "Normal",
        "SemiExpanded",
        "Expanded",
        "ExtraExpanded",
        "UltraExpanded"
    ];
    private void BuildFontMenu()
    {
        FontMenu.Items.Clear();

        var fontWeightMenu = new MenuFlyoutSubItem() { Name = "FontWeightMenu", Text = "Font Weight" };
        var fontStyleMenu = new MenuFlyoutSubItem() { Name = "FontStyleMenu", Text = "Font Style" };
        var fontStretchMenu = new MenuFlyoutSubItem() { Name = "FontStretchMenu", Text = "Font Stretch" };

        FontMenu.Items.Add(fontWeightMenu);
        FontMenu.Items.Add(fontStyleMenu);
        FontMenu.Items.Add(fontStretchMenu);
        FontMenu.Items.Add(new MenuFlyoutSeparator());

        foreach (string fontWeight in _fontWeights)
        {
            RadioMenuFlyoutItem item = new() { Text = fontWeight, Tag = "fontWeight" };
            item.Click += FontMenuItem_Click;

            if (_note.FontWeight == fontWeight) item.IsChecked = true;

            fontWeightMenu.Items.Add(item);
        }
        foreach (string fontStyle in _fontStyles) {
            RadioMenuFlyoutItem item = new() { Text = fontStyle, Tag = "fontStyle" };
            item.Click += FontMenuItem_Click;

            if (_note.FontStyle == fontStyle) item.IsChecked = true;

            fontStyleMenu.Items.Add(item);
        }
        foreach (string fontStretch in _fontStretches) {
            RadioMenuFlyoutItem item = new() { Text = fontStretch, Tag = "fontStretch" };
            item.Click += FontMenuItem_Click;

            if (_note.FontWeight == fontStretch) item.IsChecked = true;

            fontStretchMenu.Items.Add(item);
        }
        foreach (string fontFamily in _getFontFamilies())
        {
            var item = new RadioMenuFlyoutItem { Text = fontFamily, Tag = "fontFamily" };
            item.Click += FontMenuItem_Click;
            
            if (_note.FontFamily == fontFamily) item.IsChecked = true;

            FontMenu.Items.Add(item);
        }
    }

    private void LoadNote()
    {
        _isLoading = true;
        NoteBox.Text = _note.Text;
        FontSizeSlider.Value = Math.Clamp(_note.FontSize, 24, 144);
        BuildFontMenu();
        ApplyFont();
        _isLoading = false;
    }

    private void ApplyFont()
    {
        _note.FontFamily = NoteFontFamily;
        _note.FontWeight = NoteFontWeight;
        _note.FontStyle = NoteFontStyle;
        _note.FontStretch = NoteFontStretch;
        _note.FontSize = NoteFontSize;
    }

    private void ResizeWindow(int width, int height)
    {
        AppWindow.Move(new PointInt32(Cursor.Position.X - width / 2, Cursor.Position.Y - height / 2));
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
            this.SetTitleBar(LockedTitleDragRegion);
        }
        DisableTitleBarDoubleClick();
    }
    private void ApplyLockState()
    {
        NoteBox.IsReadOnly = _isTextLocked;
        CloseButton.IsEnabled = !_isTextLocked;
        FontSizeSlider.IsEnabled = !_isTextLocked;
        TitleBar.Visibility = _isTextLocked ? Visibility.Collapsed : Visibility.Visible;
        FontSizeSlider.Visibility = _isTextLocked ? Visibility.Collapsed : Visibility.Visible;
        //LockedBodyDragRegion.Visibility = _isTextLocked ? Visibility.Visible : Visibility.Collapsed;
        LockIcon.Glyph = _isTextLocked ? "\uE72E" : "\uE785";
        NoteBox.Padding = _isTextLocked ? _topPadding : _noTopPadding;
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
        if (_isTextLocked || sender is not MenuFlyoutItem { Tag: string tag }) {
            return;
        }

        var item = sender as MenuFlyoutItem;
        if (item == null) return;

        switch (item.Tag)
        {
            case "fontFamily":
                NoteFontFamily = item.Text;
                _note.FontFamily = NoteFontFamily;
                break;

            case "fontWeight":
                NoteFontWeight = item.Text;
                _note.FontWeight = NoteFontWeight;
                break;

            case "fontStyle":
                NoteFontStyle = item.Text;
                _note.FontStyle = NoteFontStyle;
                break;

            case "fontStretch":
                NoteFontStretch = item.Text;
                _note.FontStretch = NoteFontStretch;
                break;
        }

        ApplyFont();
        _saveNotes();
    }

    private void FontSizeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading || _isTextLocked || e.NewValue == 24) {
            return;
        }

        NoteFontSize = (int)e.NewValue;
        ApplyFont();
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

    private void ColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        backgroundBrush.Color = args.NewColor;
        RootGrid.Background = backgroundBrush;

        _note.BackgroundColor = backgroundBrush.Color.ToString() ?? "#EBC91E";
        _saveNotes();

        bool isLight = IsLight(backgroundBrush.Color.ToString());
        SetTextColor(isLight);
    }

    private static bool IsLight(string color)
    {
        if (!color.StartsWith('#') || (color.Length != 7 && color.Length != 9)) {
            return true;
        }
        byte[] bytes = HexToBytes(color);
        return 0.2126 * (int) bytes[0] + 0.7152 * (int) bytes[1] + 0.0722 * (int) bytes[2] > 128;
    }

    readonly Color BLACK = Color.FromArgb(255, 16, 16, 16);
    readonly Color WHITE = Color.FromArgb(255, 255, 255, 255);
    private void SetTextColor(bool isBlack)
    {
        Color textColor = isBlack ? BLACK : WHITE;
        textBrush.Color = textColor;

        NoteBox.Foreground = textBrush;
        TextColor1.Color = textColor;
        TextColor2.Color = textColor;
        TextColor3.Color = textColor;
        NoteBox.Text = NoteBox.Text + " ";
        NoteBox.Text = NoteBox.Text[..^1];
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
