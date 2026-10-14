using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Dockett.Models;

namespace Dockett.Views;

public static class Converters
{
    public static readonly IValueConverter FileKindIcon = new FuncValueConverter<FileKind, Geometry?>(kind => Icon(kind switch
    {
        FileKind.Folder => "Icon.Folder",
        FileKind.Image => "Icon.Image",
        FileKind.Text or FileKind.Document => "Icon.FileText",
        FileKind.Code => "Icon.FileCode",
        FileKind.Archive => "Icon.Archive",
        _ => "Icon.File",
    }));

    public static readonly IValueConverter NavIconGeometry = new FuncValueConverter<NavIcon, Geometry?>(icon => Icon(icon switch
    {
        NavIcon.Home => "Icon.Home",
        NavIcon.Desktop => "Icon.Desktop",
        NavIcon.Documents => "Icon.FileText",
        NavIcon.Downloads => "Icon.Download",
        NavIcon.Pictures => "Icon.Image",
        NavIcon.Network => "Icon.Network",
        _ => "Icon.Drive",
    }));

    static Geometry? Icon(string key) =>
        Application.Current?.TryGetResource(key, null, out var value) == true ? value as Geometry : null;
}
