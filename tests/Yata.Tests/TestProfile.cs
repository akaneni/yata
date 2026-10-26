using System.IO;
using System.Runtime.CompilerServices;
using Yata.Helpers;

namespace Yata.Tests;

/// <summary>
/// Points the app's data folder at a throwaway one before any test runs. Tests that provoke
/// errors on purpose (a damaged data.json, say) make the app log them, and that log must not land
/// in the real %LOCALAPPDATA%\Yata of whoever runs the tests.
/// </summary>
internal static class TestProfile
{
    public static readonly string DataDir = Path.Combine(Path.GetTempPath(), "yata-tests", $"profile-{Environment.ProcessId}");

#pragma warning disable CA2255 // Has to run before the first test touches AppPaths, which caches the folder.
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Use()
    {
        Environment.SetEnvironmentVariable("YATA_DATA_DIR", DataDir);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            Log.Flush();
            try
            {
                Directory.Delete(DataDir, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Temp folder; whatever is left goes with the next cleanup.
            }
        };
    }
}

public class TestProfileTests
{
    [Fact]
    public void The_app_writes_to_the_throwaway_profile_during_tests()
    {
        Assert.Equal(TestProfile.DataDir, AppPaths.DataDir);
        Assert.StartsWith(TestProfile.DataDir, Log.Directory);
    }
}
