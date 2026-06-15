# Simple Sticky Notes

A minimal WinUI 3 sticky-note style app for Windows 11.

## Features

- Plain text memo surface
- Separate note list window and memo windows
- List window default size: 400 x 600
- Memo window default size: 200 x 200
- Custom memo title bar with drag handle, text lock, and close controls
- Text lock disables editing, closing, and font-size changes
- Font list customization from the list window right-click menu
- Font family selection from the editor right-click menu
- Font size slider in the memo window footer
- Always-on-top toggle from the memo right-click menu
- Automatic local save
- No rich text formatting controls

## Build

```powershell
dotnet restore
dotnet build -c Debug
dotnet run -c Debug
```

This sample targets `win-x64` by default. After building, the executable is created under:

```text
bin\Debug\net9.0-windows10.0.19041.0\win-x64\SimpleStickyNotes.exe
```

Notes are saved to:

```text
%LOCALAPPDATA%\SimpleStickyNotes\notes.json
```
