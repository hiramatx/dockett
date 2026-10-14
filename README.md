# Dockett

A dual pane file manager for Windows, built with C# and [Avalonia UI](https://avaloniaui.net/).

- Navigation pane with quick access folders, drives (with usage bars), and network drives
- Two file panes, each with its own tabs, history, sorting, and filter
- Address bar with clickable breadcrumbs; click the empty space (or Ctrl+L) to type a path
- Preview panel for images, text and code files, and folder details
- Tools strip: copy and move to the other pane, rename, new folder, compress to zip, compare folders, properties, delete to Recycle Bin
- Copy, move, rename and delete go through the Windows shell, so you get the native progress and conflict dialogs and Explorer's Undo
- Light and dark themes follow Windows

## Build and run

Requires the .NET 10 SDK on Windows.

```
dotnet run --project src/Dockett
```

Optionally pass start folders for the left and right panes:

```
dotnet run --project src/Dockett -- "C:\Projects" "D:\Backup"
```

Run the tests with `dotnet test`.

## Keyboard

| Key | Action |
| --- | --- |
| Tab | Switch pane |
| Enter / Backspace | Open / go up |
| Alt+Left / Alt+Right / Alt+Up | Back / forward / up |
| F2 | Rename |
| F3 | Toggle preview panel |
| F5 / F6 | Copy / move to the other pane |
| F7 | New folder |
| Del | Delete to Recycle Bin |
| Alt+Enter | Properties |
| Ctrl+T / Ctrl+W | New tab / close tab |
| Ctrl+Tab / Ctrl+Shift+Tab | Next / previous tab |
| Ctrl+L or Alt+D | Type a path |
| Ctrl+F | Filter the current folder |
| Ctrl+R | Refresh |

## Project layout

```
src/Dockett/
  Models/        FileItem, NavItem, file type and formatting helpers
  Services/      Directory listing, drives, Windows shell operations
  ViewModels/    MainViewModel, PaneViewModel (tabs), TabViewModel (one folder), PreviewViewModel
  Views/         MainWindow, FilePaneView, DialogWindow
  Resources/     Theme colors, icon geometries, styles
tests/Dockett.Tests/
tools/make_icon.py   Regenerates Assets/dockett.ico and dockett.png
```
