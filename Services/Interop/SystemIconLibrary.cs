using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace OpenTaskbar.Services;

/// <summary>
/// Icons that ship with Windows (imageres.dll, shell32.dll) for the Start button gallery. An icon is stored as
/// "file.dll,index" so it survives updates and needs no copy of the image.
/// </summary>
public static class SystemIconLibrary
{
    public static readonly string[] Libraries = ["imageres.dll", "shell32.dll"];

    /// <summary>Parses "imageres.dll,15" (a bare file name is looked up in System32).</summary>
    public static (string Path, int Index)? Parse(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return null;
        var comma = spec.LastIndexOf(',');
        if (comma <= 0 || !int.TryParse(spec[(comma + 1)..], out var index) || index < 0) return null;
        var file = spec[..comma].Trim();
        if (!file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) return null;
        var path = Path.IsPathRooted(file) ? file : Path.Combine(Environment.SystemDirectory, file);
        return (path, index);
    }

    public static int Count(string library) =>
        (int)ExtractIconEx(Path.Combine(Environment.SystemDirectory, library), -1, null, null, 0);

    public static BitmapSource? Load(string? spec)
    {
        if (Parse(spec) is not { } icon || !File.Exists(icon.Path)) return null;
        var large = new nint[1];
        try
        {
            if (ExtractIconEx(icon.Path, icon.Index, large, null, 1) == 0 || large[0] == 0) return null;
            var image = Imaging.CreateBitmapSourceFromHIcon(large[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
        finally
        {
            if (large[0] != 0) DestroyIcon(large[0]);
        }
    }

    /// <summary>The signed-in user's picture when Windows keeps a plain image of it (default account picture).</summary>
    public static string? AccountPicture()
    {
        foreach (var candidate in new[]
                 {
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "User Account Pictures", "user.png"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "User Account Pictures", "user-192.png")
                 })
        {
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, nint[]? large, nint[]? small, uint count);

    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
}
