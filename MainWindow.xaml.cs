using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.Graphics;
using WinRT.Interop;

using System.Drawing;
using System.Drawing.Text;

namespace SimpleStickyNotes;

public sealed partial class MainWindow : Window
{
    private static readonly string SettingsDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SimpleStickyNotes");

    private static readonly string NotesPath = Path.Combine(SettingsDirectory, "notes.json");
    private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");
    private static readonly string FontsDirectory = Path.Combine(SettingsDirectory, "Fonts");

    private readonly ObservableCollection<NoteItem> _notes = [];
    private readonly Dictionary<Guid, NoteWindow> _openNoteWindows = [];
    public readonly ObservableCollection<string> UsingFontFamilies = [];
    public readonly ObservableCollection<string> StandbyFontFamilies = [];
    public string _defaultFontFamily = "Segoe UI";

    private InstalledFontCollection _installedFontCollection;

    private ObservableCollection<string> _selectedUsingFonts = [];
    private ObservableCollection<string> _selectedStandbyFonts = [];
    private ObservableCollection<string> _draggedFonts = [];
    private ListView? _sourceListView;

    public MainWindow()
    {
        _installedFontCollection = new InstalledFontCollection();
        InitializeComponent();
        ResizeWindow(400, 600);
        CenterWindow();
        LoadSettings();
        LoadNotes();
        ExtendsContentIntoTitleBar = true;
        HideTitleBar();
        SetTitleBar(TitleBar);
        NotesList.ItemsSource = _notes;
    }
    private void CenterWindow()
    {
        // 1. Get the HWND handle for the WinUI 3 window
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        // 2. Get the WindowId from the HWND handle
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);

        // 3. Retrieve the AppWindow object managing the viewport
        var appWindow = AppWindow.GetFromWindowId(windowId);

        if (appWindow != null) {
            // 4. Retrieve the current display area information
            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Nearest);

            if (displayArea != null) {
                var displayBounds = displayArea.WorkArea;
                var windowSize = appWindow.Size;

                // 5. Calculate centered coordinates considering taskbars/scaling
                int centerX = displayBounds.X + ((displayBounds.Width - windowSize.Width) / 2);
                int centerY = displayBounds.Y + ((displayBounds.Height - windowSize.Height) / 2);

                // 6. Move the window to the calculated point
                appWindow.Move(new PointInt32(centerX, centerY));
            }
        }
    }
    private void HideTitleBar()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter) {
            presenter.SetBorderAndTitleBar(true, false);
        }
    }
    public void ShowListWindow()
    {
        Activate();
    }

    public void SaveNotes()
    {
        Directory.CreateDirectory(SettingsDirectory);

        var json = JsonSerializer.Serialize(_notes, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(NotesPath, json);
    }

    public IReadOnlyList<string> GetFontFamilies()
    {
        return UsingFontFamilies.ToArray();
    }

    private void LoadSettings()
    {
        string prevFont = "";
        string currFont = "";
        foreach (FontFamily family in _installedFontCollection.Families)
        {
            currFont = FontObject.GetFontFamilyName(family);
            if (prevFont == currFont) continue;

            StandbyFontFamilies.Add(currFont);
            prevFont = currFont;
        }

        foreach (var fontFamily in ReadSettings().UsingFontFamilies)
        {
            if (!string.IsNullOrWhiteSpace(fontFamily) && !UsingFontFamilies.Contains(fontFamily))
            {
                UsingFontFamilies.Add(fontFamily);
            }
        }

        HashSet<string> standbySet = [.. StandbyFontFamilies];
        HashSet<string> usingSet = [.. UsingFontFamilies];
        foreach (string font in usingSet)
        {
            if (!standbySet.Contains(font))
            {
                UsingFontFamilies.Remove(font);
            }
        }

        foreach (string font in UsingFontFamilies) 
        {
            StandbyFontFamilies.Remove(font);
        }

        if (!string.IsNullOrWhiteSpace(ReadSettings().DefaultFontFamily))
        {
            _defaultFontFamily = ReadSettings().DefaultFontFamily;
        }
    }

    private AppSettings ReadSettings()
    {
        if (!File.Exists(SettingsPath))
        {
            return AppSettings.Default;
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? AppSettings.Default;
        }
        catch
        {
            return AppSettings.Default;
        }
    }

    JsonSerializerOptions jsonSerializerOptions = new()
    { WriteIndented = true };

    private void SaveSettings()
    {
        Directory.CreateDirectory(SettingsDirectory);

        var settings = new AppSettings([.. UsingFontFamilies], [.. StandbyFontFamilies], _defaultFontFamily);
        var json = JsonSerializer.Serialize(settings, jsonSerializerOptions);
        File.WriteAllText(SettingsPath, json);
    }

    private void LoadNotes()
    {
        foreach (var note in ReadNotes())
        {
            _notes.Add(note);
        }
    }

    private NoteItem[] ReadNotes()
    {
        try
        {
            var json = File.ReadAllText(NotesPath);
            return JsonSerializer.Deserialize<NoteItem[]>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }


    private void OpenNote(NoteItem note)
    {
        if (_openNoteWindows.TryGetValue(note.Id, out var existingWindow))
        {
            existingWindow.Activate();
            return;
        }

        var noteWindow = new NoteWindow(note, GetFontFamilies, SaveNotes, ShowListWindow);
        _openNoteWindows[note.Id] = noteWindow;
        noteWindow.Closed += (_, _) => _openNoteWindows.Remove(note.Id);
        noteWindow.Activate();
    }

    private void ResizeWindow(int width, int height)
    {
        var windowHandle = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(windowHandle);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new SizeInt32(width, height));
    }

    private void NewNoteButton_Click(object sender, RoutedEventArgs e)
    {
        var note = new NoteItem();
        _notes.Insert(0, note);
        SaveNotes();
        OpenNote(note);
    }

    private void NotesList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is NoteItem note)
        {
            OpenNote(note);
        }
    }

    private void RefreshOpenNoteFonts()
    {
        foreach (var noteWindow in _openNoteWindows.Values)
        {
            noteWindow.RefreshFonts();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender == null) return;

        if (sender is FrameworkElement elem) {
            NoteItem? note = elem.DataContext as NoteItem;
            if (note == null) return;
            _notes.Remove(note);
            SaveNotes();
        }
    }

    private void FontList_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items == null) return;

        _sourceListView = sender as ListView;
        
        if (_sourceListView == UsingFontList)
        {
            foreach (string selection in _selectedUsingFonts) {
                _draggedFonts.Add(selection);
            }
        } else if (_sourceListView == StandbyFontList)
        {
            foreach (string selection in _selectedStandbyFonts) {
                _draggedFonts.Add(selection);
            }
        }

        string item = (string) e.Items[0];
        if (!_draggedFonts.Contains(item)) {
            _draggedFonts.Add(item);
        }

    }

    private void FontList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        ResetDragState();
        SaveSettings();
        RefreshOpenNoteFonts();
    }

    private void FontList_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
    }

    private async void FontList_Drop(object sender, DragEventArgs e)
    {
        if (_draggedFonts.Count < 1 || _sourceListView == null) return;

        var targetListView = sender as ListView;

        if (targetListView == null) return;

        var sourceCollection = _sourceListView.ItemsSource as ObservableCollection<string>;
        var targetCollection = targetListView.ItemsSource as ObservableCollection<string>;

        if (sourceCollection == null || targetCollection == null || sourceCollection == targetCollection) return;

        foreach (string font in _draggedFonts)
        {
            sourceCollection.Remove(font);
            targetCollection.Add(font);
        }
        ResetDragState();
    }

    private void ResetDragState()
    {
        _draggedFonts = [];
        _sourceListView = null;
    }

    private void FontList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ListView? lv = sender as ListView;
        if (lv == null) return;

        if (lv == UsingFontList)
        {
            foreach (var item in e.AddedItems) {
                if (item is string font) _selectedUsingFonts.Add(font);
            }
            foreach (var item in e.RemovedItems) {
                if (item is string font) _selectedUsingFonts.Remove(font);
            }
        } else if (lv == StandbyFontList)
        {
            foreach (var item in e.AddedItems) {
                if (item is string font) _selectedStandbyFonts.Add(font);
            }
            foreach (var item in e.RemovedItems) {
                if (item is string font) _selectedStandbyFonts.Remove(font);
            }
        }
    }

    private sealed record AppSettings(string[] UsingFontFamilies, string[]StandbyFontFamilies, string DefaultFontFamily)
    {
        public static AppSettings Default { get; } = new(
        [
            "Segoe UI",
            "Malgun Gothic",
            "Arial",
            "Calibri",
            "Consolas",
            "Georgia",
            "Times New Roman"
        ],
        [],
        "Segoe UI"
        );
    }
}

public sealed class NoteItem : INotifyPropertyChanged
{
    private string _text;
    private string _fontFamily;
    private string _fontWeight;
    private string _fontStyle;
    private string _fontStretch;
    private int _fontSize;
    private string _backgroundColor;
    private string _textColor;

    public NoteItem()
        : this(string.Empty, "Segoe UI", "Normal", "Normal", "Normal", 24, "#EBC91E")
    {
    }

    public NoteItem(string text, string fontFamily, string fontWeight, string fontStyle, string fontStretch, int fontSize, string backgroundColor)
    {
        Id = Guid.NewGuid();
        _text = text;
        _fontFamily = string.IsNullOrWhiteSpace(fontFamily) ? "Segoe UI" : fontFamily;
        _fontWeight = string.IsNullOrWhiteSpace(fontWeight) ? "Normal" : fontWeight;
        _fontStyle = string.IsNullOrWhiteSpace(fontStyle) ? "Normal" : fontStyle;
        _fontStretch = string.IsNullOrWhiteSpace(fontStretch) ? "Normal" : fontStretch;
        _fontSize = fontSize > 0 ? fontSize : 24;
        _backgroundColor = string.IsNullOrWhiteSpace(backgroundColor) ? "#EBC91E" : backgroundColor;
        _textColor = IsLight(backgroundColor) ? "#1A1A1A" : "#FFFFFF";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; init; }

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value)
            {
                return;
            }

            _text = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Preview));
        }
    }

    public string FontFamily
    {
        get => _fontFamily;
        set
        {
            if (_fontFamily == value)
            {
                return;
            }

            _fontFamily = value;
            OnPropertyChanged();
        }
    }
    public string FontWeight
    {
        get => _fontWeight;
        set
        {
            if (_fontWeight == value)
            {
                return;
            }

            _fontWeight = value;
            OnPropertyChanged();
        }
    }
    public string FontStyle
    {
        get => _fontStyle;
        set
        {
            if (_fontStyle == value)
            {
                return;
            }

            _fontStyle = value;
            OnPropertyChanged();
        }
    }
    public string FontStretch
    {
        get => _fontStretch;
        set
        {
            if (_fontStretch == value)
            {
                return;
            }

            _fontStretch = value;
            OnPropertyChanged();
        }
    }
    public int FontSize
    {
        get => _fontSize;
        set
        {
            if (Math.Abs(_fontSize - value) < 0.01)
            {
                return;
            }

            _fontSize = value;
            OnPropertyChanged();
        }
    }
    public string BackgroundColor
    {
        get => _backgroundColor;
        set
        {
            if (_backgroundColor == value) {
                return;
            }

            _backgroundColor = value;

            TextColor = IsLight(value) ? "#1A1A1A" : "#FFFFFF";

            OnPropertyChanged();
        }
    }

    [JsonIgnore]
    public string Preview
    {
        get
        {
            var preview = Text.Replace(Environment.NewLine, " ").Trim();
            return string.IsNullOrWhiteSpace(preview) ? "" : preview;
        }
    }

    public string TextColor
    {
        get => _textColor;
        set
        {
            if (_textColor == value) {
                return;
            }

            _textColor = value;
            OnPropertyChanged();
        }
    }

    private static byte[] HexToBytes(string hex)
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

    private static bool IsLight(string color)
    {
        if (!color.StartsWith('#') || (color.Length != 7 && color.Length != 9)) {
            return true;
        }
        byte[] bytes = HexToBytes(color);
        return 0.2126 * (int) bytes[0] + 0.7152 * (int) bytes[1] + 0.0722 * (int) bytes[2] > 128;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}