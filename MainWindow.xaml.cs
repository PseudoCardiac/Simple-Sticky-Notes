using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

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
    private readonly ObservableCollection<string> _fontFamilies = [];
    public string _defaultFontFamily = "Segoe UI";

    public MainWindow()
    {
        InitializeComponent();
        ResizeWindow(400, 600);
        LoadSettings();
        LoadNotes();
        RebuildRemoveFontMenu();
        NotesList.ItemsSource = _notes;
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
        return _fontFamilies.ToArray();
    }

    private void LoadSettings()
    {
        foreach (var fontFamily in ReadSettings().FontFamilies)
        {
            if (!string.IsNullOrWhiteSpace(fontFamily) && !_fontFamilies.Contains(fontFamily))
            {
                _fontFamilies.Add(fontFamily);
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

    private void SaveSettings()
    {
        Directory.CreateDirectory(SettingsDirectory);

        var settings = new AppSettings([.. _fontFamilies], _defaultFontFamily);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
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

    private void RemoveFontMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: string fontFamily })
        {
            return;
        }

        _fontFamilies.Remove(fontFamily);
        SaveSettings();
        NormalizeNoteFonts();
        RebuildRemoveFontMenu();
        RefreshOpenNoteFonts();
    }

    private void AddFontFamily(string fontFamily)
    {
        if (string.IsNullOrWhiteSpace(fontFamily) || _fontFamilies.Contains(fontFamily))
        {
            return;
        }

        _fontFamilies.Add(fontFamily);
        SaveSettings();
        RebuildRemoveFontMenu();
        RefreshOpenNoteFonts();
    }

    private void NormalizeNoteFonts()
    {
        var fallback = "Segoe UI";

        foreach (var note in _notes.Where(note => !_fontFamilies.Contains(note.FontFamily)))
        {
            note.FontFamily = fallback;
        }

        SaveNotes();
    }

    private void RebuildRemoveFontMenu()
    {
        RemoveFontMenu.Items.Clear();

        foreach (var fontFamily in _fontFamilies)
        {
            var item = new MenuFlyoutItem { Text = GetFontDisplayName(fontFamily), Tag = fontFamily };
            item.Click += RemoveFontMenuItem_Click;
            RemoveFontMenu.Items.Add(item);
        }
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

    public NoteItem()
        : this(string.Empty, "Segoe UI", 24, "EBC91E")
    {
    }

    public NoteItem(string text, string fontFamily, int fontSize, string backgroundColor)
    {
        Id = Guid.NewGuid();
        _text = text;
        _fontFamily = string.IsNullOrWhiteSpace(fontFamily) ? "Segoe UI" : fontFamily;
        _fontSize = fontSize > 0 ? fontSize : 24;
        _backgroundColor = string.IsNullOrWhiteSpace(backgroundColor) ? "EBC91E" : backgroundColor;
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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
