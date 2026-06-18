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
    public readonly ObservableCollection<string> FontFamilies = [];
    public string _defaultFontFamily = "Segoe UI";

    private InstalledFontCollection _installedFontCollection;
    private FontFamily[] _installedFontFamilies;

    public MainWindow()
    {
        InitializeComponent();
        ResizeWindow(400, 600);
        LoadSettings();
        LoadNotes();
        ExtendsContentIntoTitleBar = true;
        HideTitleBar();
        SetTitleBar(TitleBar);
        NotesList.ItemsSource = _notes;

        _installedFontCollection = new InstalledFontCollection();
        _installedFontFamilies = _installedFontCollection.Families;
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
        return FontFamilies.ToArray();
    }

    private void LoadSettings()
    {
        foreach (var fontFamily in ReadSettings().FontFamilies)
        {
            if (!string.IsNullOrWhiteSpace(fontFamily) && !FontFamilies.Contains(fontFamily))
            {
                FontFamilies.Add(fontFamily);
            }
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

        var settings = new AppSettings([.. FontFamilies], _defaultFontFamily);
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

    private void Font_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: string fontFamily })
        {
            return;
        }

        FontFamilies.Remove(fontFamily);
        SaveSettings();
        NormalizeNoteFonts();
        RefreshOpenNoteFonts();
    }

    private void AddFontFamily(string fontFamily)
    {
        if (string.IsNullOrWhiteSpace(fontFamily) || FontFamilies.Contains(fontFamily))
        {
            return;
        }

        FontFamilies.Add(fontFamily);
        SaveSettings();
        RefreshOpenNoteFonts();
    }

    private void RemoveFontFamily(string fontFamily)
    {
        if (string.IsNullOrWhiteSpace(fontFamily) || FontFamilies.Contains(fontFamily)) {
            return;
        }

        FontFamilies.Remove(fontFamily);
        SaveSettings();
        RefreshOpenNoteFonts();
    }

    private void NormalizeNoteFonts()
    {
        var fallback = "Segoe UI";

        foreach (var note in _notes.Where(note => !FontFamilies.Contains(note.FontFamily)))
        {
            note.FontFamily = fallback;
        }

        SaveNotes();
    }

    private static string GetFontDisplayName(string fontFamily)
    {
        var markerIndex = fontFamily.LastIndexOf('#');
        return markerIndex >= 0 && markerIndex < fontFamily.Length - 1
            ? fontFamily[(markerIndex + 1)..]
            : fontFamily;
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

    private void FontMenuFlyoutItem_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedContextItem != null) {
            FontFamilies.Remove(_selectedContextItem);    

            SaveSettings();
            RefreshOpenNoteFonts();
        }
    }
    
    private string? _selectedContextItem;
    private void FontList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement element &&
        element.DataContext is string item) {
            _selectedContextItem = item;
        }
    }

    private async void MainWindow_Drop(object sender, DragEventArgs e)
    {
        //InstalledFontCollection installedFontCollection = new();
        //FontFamily[] fontFamilies;
        //fontFamilies = installedFontCollection.Families;

        //for (int i = 0; i < fontFamilies.Length; ++i)
        //{
        //    AddFontFamily(fontFamilies[i].Name);
        //}
    }

    private void MainWindow_DragOver(object sender, DragEventArgs e)
    {
        // Check if the item being dragged contains files
        if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems)) {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Drop to add font";
            e.DragUIOverride.IsCaptionVisible = true;
        } else {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.None;
        }
    }

    private sealed record AppSettings(string[] FontFamilies, string DefaultFontFamily)
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
            return string.IsNullOrWhiteSpace(preview) ? "No text" : preview;
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