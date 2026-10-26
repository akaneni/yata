using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Yata.Helpers;

/// <summary>
/// A small JSON file encrypted with DPAPI for the current Windows user: other accounts on the PC,
/// or the file copied to another machine, can't read it.
/// </summary>
internal static class SecretFile
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Yata.SecretFile.v1");

    public static T? Read<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path))
                return null;
            byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<T>(plain);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        {
            // Unreadable (another user's file, corrupted): behave as if it weren't there.
            Log.Warn("secrets", "Arquivo cifrado ilegível; tratado como ausente", ex);
            return null;
        }
    }

    public static void Write<T>(string path, T value)
    {
        byte[] cipher = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(value), Entropy, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        File.WriteAllBytes(temp, cipher);
        File.Move(temp, path, overwrite: true);
    }

    public static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("secrets", "Não foi possível apagar o arquivo cifrado", ex);
        }
    }
}
