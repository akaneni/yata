using System;
using System.IO;

namespace Yata.Helpers;

/// <summary>
/// Replaces a file all at once: written next to it, flushed to disk, then moved over. A crash or a
/// power cut leaves either the old file or the new one, never an empty or half-written one (NTFS
/// journals the rename, not the data, so the flush has to come first).
/// </summary>
internal static class AtomicFile
{
    public static void Write(string path, ReadOnlySpan<byte> bytes, string tempSuffix = ".tmp")
    {
        string temp = path + tempSuffix;
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temp, path, overwrite: true);
    }
}
