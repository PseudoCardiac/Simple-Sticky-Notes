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
    private Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicController acrylicController;

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

        acrylicController = new()
        {
            Kind = DesktopAcrylicKind.Thin
        };

        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        BuildContextMenu();
        HideTitleBar();
        ResizeWindow(400, 400);
        LoadNote();
        ApplyLockState();
        SetTitleBar(HandleButton);
        TrySetDesktopAcrylicBackdrop();
        DisableTitleBarDoubleClick();
    }

    bool TrySetDesktopAcrylicBackdrop()
    {
        if (DesktopAcrylicController.IsSupported()) {
            DesktopAcrylicBackdrop DesktopAcrylicBackdrop = new();
            this.SystemBackdrop = DesktopAcrylicBackdrop;

            return true; // Succeeded.
        }

        return false; // DesktopAcrylic is not supported on this system.
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
        foreach (var fontFamily in _getFontFamilies())
        {
            var item = new MenuFlyoutItem { Text = GetFontDisplayName(fontFamily), Tag = fontFamily };
            item.Click += FontMenuItem_Click;
            fontMenu.Items.Add(item);
        }
    }

    private void LoadNote()
    {
        _isLoading = true;
        NoteBox.Text = _note.Text;
        FontSizeSlider.Value = Math.Clamp(_note.FontSize, 12, 72);
        ApplyFont(_note.FontFamily, _note.FontSize);
        _isLoading = false;
    }

    private void ApplyFont(string fontFamily, double fontSize)
    {
        var families = _getFontFamilies();
        if (families.Count > 0 && !families.Contains(fontFamily))
        {
            fontFamily = families[0];
            _note.FontFamily = fontFamily;
        }

        if (string.IsNullOrWhiteSpace(fontFamily))
        {
            fontFamily = NoteItem.DefaultFontFamily;
        }

        NoteBox.FontFamily = new FontFamily(fontFamily);
        NoteBox.FontSize = Math.Clamp(fontSize, 12, 72);
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
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
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
        if ( _isTextLocked )
        {
            this.SetTitleBar(RootGrid);
        } else
        {
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
        if (_isTextLocked && e.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
        {
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
        if (!_isTextLocked)
        {
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

        if (!_isAlwaysOnTop)
        {
            PinIcon.Glyph = "\uE718";
        } else
        {
            PinIcon.Glyph = "\uE77A";
        }

        if (AppWindow.Presenter is OverlappedPresenter presenter) {
            presenter.IsAlwaysOnTop = _isAlwaysOnTop;
        }
    }

    private void FontMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_isTextLocked || sender is not MenuFlyoutItem { Tag: string fontFamily })
        {
            return;
        }

        _note.FontFamily = fontFamily;
        ApplyFont(_note.FontFamily, _note.FontSize);
        _saveNotes();
    }

    private void FontSizeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading || _isTextLocked)
        {
            return;
        }

        _note.FontSize = e.NewValue;
        ApplyFont(_note.FontFamily, _note.FontSize);
        _saveNotes();
    }

    private void NoteBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading || _isTextLocked)
        {
            return;
        }

        _note.Text = NoteBox.Text;
        _saveNotes();
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    private void MenuFlyoutItem_Click(object sender, RoutedEventArgs e)
    {

    }

    private void OpenColorPickerItem_Click(object sender, RoutedEventArgs e)
    {
        // Ensure you close the child window before closing the parent window to avoid application crash.
        var childWindow = new Window()
        {
            ExtendsContentIntoTitleBar = true,
            SystemBackdrop = new MicaBackdrop(),
            Content = new Page()
            {
                Content = new TextBlock()
                {
                    Text = "New child window!",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                }
            }
        };

        childWindow.AppWindow.ResizeClient(new SizeInt32(500, 500));
        childWindow.Activate();
    }
}
