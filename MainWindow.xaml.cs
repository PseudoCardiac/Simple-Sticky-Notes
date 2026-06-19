using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Windows.ApplicationModel.Background;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT.Interop;

using System.Drawing;
using System.Drawing.Text;
using Windows.ApplicationModel.Activation;

namespace SimpleStickyNotes;

public sealed partial class MainWindow : Window
{
    private const int WmNclButtonDown = 0x00A1;
    private const int HtCaption = 0x0002;

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

    private ObservableCollection<string> _selectedFonts = [];
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
        foreach (FontFamily family in _installedFontCollection.Families)
        {
            StandbyFontFamilies.Add(family.Name);
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

    private async void AddFontFamilyMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var input = new TextBox
        {
            PlaceholderText = "Font family name",
            MinWidth = 260
        };

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Add font family",
            Content = input,
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        AddFontFamily(input.Text.Trim());
    }

    private async void ImportLocalFontMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".ttf");
        picker.FileTypeFilter.Add(".otf");
        StorageFolder fontsFolder = await StorageFolder.GetFolderFromPathAsync(@"C:\Windows\Fonts");
        //picker.SuggestedStartFolder = fontsFolder;
        picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;

        var windowHandle = WindowNative.GetWindowHandle(this);
        InitializeWithWindow.Initialize(picker, windowHandle);

        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        Directory.CreateDirectory(FontsDirectory);
        var destination = Path.Combine(FontsDirectory, file.Name);
        File.Copy(file.Path, destination, true);

        var fontName = Path.GetFileNameWithoutExtension(destination);
        AddFontFamily($"{new Uri(destination).AbsoluteUri}#{fontName}");
    }

    private void AddFontFamily(string fontFamily)
    {
        if (string.IsNullOrWhiteSpace(fontFamily) || UsingFontFamilies.Contains(fontFamily))
        {
            return;
        }

        UsingFontFamilies.Add(fontFamily);
        SaveSettings();
        RefreshOpenNoteFonts();
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

        var note = (sender as FrameworkElement).DataContext as NoteItem;
        _notes.Remove(note);
        SaveNotes();
    }

    private void FontList_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items == null) return;

        foreach (string selection in _selectedFonts)
        {
            _draggedFonts.Add(selection);
        }
        
        if (!_draggedFonts.Contains(e.Items[0] as string))
        {
            _draggedFonts.Add(e.Items[0] as string);
        }
        
        _sourceListView = sender as ListView;
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

        if (sourceCollection == null || targetCollection == null) return;

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

    private static void PlaySound()
    {
        ElementSoundPlayer.State = ElementSoundPlayerState.On;
        ElementSoundPlayer.Play(ElementSoundKind.Invoke);
    }

    private void FontList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        foreach (var item in e.AddedItems)
        {
            _selectedFonts.Add(item as string);
        }
        foreach (var item in e.RemovedItems) {
            _selectedFonts.Remove(item as string);
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
    private int _fontSize;
    private string _backgroundColor;
    private string _textColor;

    public NoteItem()
        : this(string.Empty, "Segoe UI", 24, "#EBC91E")
    {
    }

    public NoteItem(string text, string fontFamily, int fontSize, string backgroundColor)
    {
        Id = Guid.NewGuid();
        _text = text;
        _fontFamily = string.IsNullOrWhiteSpace(fontFamily) ? "Segoe UI" : fontFamily;
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

    private static string GetFontDisplayName(string fontFamily)
    {
        var markerIndex = fontFamily.LastIndexOf('#');
        return markerIndex >= 0 && markerIndex < fontFamily.Length - 1
            ? fontFamily[(markerIndex + 1)..]
            : fontFamily;
    }
}