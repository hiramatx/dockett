# Dockett

A dual pane file manager for Windows, built with C# and [Avalonia UI](https://avaloniaui.net/).

- Navigation pane with quick access folders, drives (with usage bars), and network drives
- Two file panes, each with its own tabs, history, sorting, and filter
- Address bar with clickable breadcrumbs; click the empty space (or Ctrl+L) to type a path
- Folders refresh automatically when files change, keeping your selection and scroll position; if a folder is deleted, the tab moves up to the nearest one that still exists
- Preview panel for images, text and code files, and folder details
- Tools strip: copy and move to the other pane, rename, new folder, compress to zip, compare folders, properties, delete to Recycle Bin
- Drag and drop between panes, onto folder rows, and to or from Explorer. Same drive moves, another drive copies; hold Ctrl to copy or Shift to move
- Right-click menu with open in new tab or other pane, Open with, cut/copy/paste (works with Explorer's clipboard), copy path, Open in Terminal, and Show in Explorer. "Show more options" (or Shift+right-click) opens the full Windows menu with shell extensions like 7-Zip and Send to
- Copy, move, rename and delete go through the Windows shell, so you get the native progress and conflict dialogs and Explorer's Undo
- Remembers your tabs, their sort order, the active pane, the preview panel, and the window size, position, and panel widths between sessions (saved in `%APPDATA%\Dockett\session.json`). Folders that no longer exist reopen at the nearest parent that does
- Light and dark themes follow Windows

## Build and run

Requires the .NET 10 SDK on Windows.

```
dotnet run --project src/Dockett
```

Optionally pass start folders for the left and right panes (these replace the restored tabs for that run):

```
dotnet run --project src/Dockett -- "C:\Projects" "D:\Backup"
```

Run the tests with `dotnet test`. Clipboard tests are skipped by default because they replace what's on the clipboard; set `DOCKETT_CLIPBOARD_TESTS=1` to include them.

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
| Ctrl+X / Ctrl+C / Ctrl+V | Cut / copy / paste files |
| Ctrl+Shift+C | Copy path |
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
