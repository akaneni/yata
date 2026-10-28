using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Yata.Helpers;

/// <summary>Thumbnails for pictures, the shell's associated icon for everything else.</summary>
internal static class FileIcons
{
    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_LARGEICON = 0x0;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    private static readonly string[] PictureExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff" };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    /// <summary>Returns the image and whether it is a picture thumbnail (vs. a shell icon).</summary>
    public static (ImageSource? Image, bool IsPicture) For(string path)
    {
        if (Array.IndexOf(PictureExtensions, Path.GetExtension(path).ToLowerInvariant()) >= 0)
        {
            var thumbnail = LoadThumbnail(path);
            if (thumbnail != null)
                return (thumbnail, true);
        }
        return (LoadShellIcon(path), false);
    }

    /// <summary>
    /// The icon for the file's type, from its name alone: the file isn't touched, so a path on a
    /// server that doesn't answer can't hang the notch.
    /// </summary>
    public static ImageSource? ForType(string path)
    {
        uint attributes = Path.HasExtension(path) ? FILE_ATTRIBUTE_NORMAL : FILE_ATTRIBUTE_DIRECTORY;
        return LoadShellIcon(path, attributes, SHGFI_USEFILEATTRIBUTES);
    }

    private static ImageSource? LoadThumbnail(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.DecodePixelWidth = 112;
            image.CacheOption = BitmapCacheOption.OnLoad; // Don't keep the file locked.
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null; // Corrupt or unsupported picture: fall back to the icon.
        }
    }

    private static ImageSource? LoadShellIcon(string path, uint attributes = 0, uint flags = 0)
    {
        var info = new SHFILEINFO();
        if (SHGetFileInfo(path, attributes, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON | flags) == IntPtr.Zero ||
            info.hIcon == IntPtr.Zero)
            return null;

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }
}
