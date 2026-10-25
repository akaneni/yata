using System;
using System.IO;

namespace Yata.Helpers;

/// <summary>Where Yata keeps its local data (%LOCALAPPDATA%\Yata).</summary>
internal static class AppPaths
{
    private static readonly Lazy<string> Root = new(Resolve);

    public static string DataDir => Root.Value;

    private static string Resolve()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string root = Path.Combine(local, "Yata");

        // The app was called "Notch" before; carry its data over once.
        string legacy = Path.Combine(local, "Notch");
        if (!Directory.Exists(root) && Directory.Exists(legacy))
        {
            try
            {
                Directory.Move(legacy, root);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Keep using the old folder rather than lose data.
                return legacy;
            }
        }
        return root;
    }
}
