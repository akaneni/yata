using System.IO;
using Yata.Helpers;
using Yata.Services;

namespace Yata.Tests;

/// <summary>data.json survives damage and never overwrites a file it couldn't read.</summary>
public sealed class AppStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "yata-tests", Guid.NewGuid().ToString("N"));
    private string DataFile => Path.Combine(_dir, "data.json");

    public AppStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Saves_and_loads_back()
    {
        var store = AppStore.Load(DataFile);
        store.Settings.FocusMinutes = 40;
        store.SaveNow();

        var again = AppStore.Load(DataFile);
        Assert.Equal(40, again.Settings.FocusMinutes);
        Assert.False(again.RecoveredFromDamage);
        Assert.False(File.Exists(DataFile + ".tmp"));
    }

    [Fact]
    public void Only_the_first_start_is_new_and_water_reminders_wait_to_be_asked_for()
    {
        var store = AppStore.Load(DataFile);
        Assert.True(store.IsNew);
        Assert.False(store.Data.Water.RemindersEnabled);
        store.SaveNow();

        Assert.False(AppStore.Load(DataFile).IsNew);
    }

    [Fact]
    public void Existing_water_reminders_stay_on()
    {
        File.WriteAllText(DataFile, """{"Water":{"RemindersEnabled":true}}""");
        Assert.True(AppStore.Load(DataFile).Data.Water.RemindersEnabled);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{\"Settings\": {\"FocusMinutes\": 4")]
    public void A_damaged_file_is_set_aside_and_the_app_starts_fresh(string content)
    {
        File.WriteAllText(DataFile, content);

        var store = AppStore.Load(DataFile);

        Assert.True(store.RecoveredFromDamage);
        Assert.Equal(25, store.Settings.FocusMinutes);
        Assert.Equal(content, File.ReadAllText(DataFile + ".bad"));
    }

    [Fact]
    public void A_file_that_cant_be_read_is_never_overwritten()
    {
        File.WriteAllText(DataFile, "{\"Settings\": {\"FocusMinutes\": 50}}");
        AppStore store;
        using (new FileStream(DataFile, FileMode.Open, FileAccess.Read, FileShare.None))
            store = AppStore.Load(DataFile);

        store.Settings.FocusMinutes = 10;
        store.SaveNow();

        Assert.False(store.RecoveredFromDamage);
        Assert.Contains("50", File.ReadAllText(DataFile));
    }

    [Fact]
    public void Missing_parts_and_out_of_range_numbers_dont_crash_the_start()
    {
        File.WriteAllText(DataFile, """
            {"Settings":null,"Tasks":[null,{"Text":null}],"Shelf":null,"Water":{"Days":null,"GoalMl":-1},
             "TaskDeletes":null,"Updates":null}
            """);
        var store = AppStore.Load(DataFile);

        Assert.NotNull(store.Settings);
        Assert.Equal("", Assert.Single(store.Data.Tasks).Text);
        Assert.Empty(store.Data.Shelf);
        Assert.Empty(store.Data.TaskDeletes);
        Assert.NotNull(store.Data.Water.Days);
        Assert.Equal(500, store.Data.Water.GoalMl);
        Assert.NotNull(store.Data.Updates);
        Assert.Equal(StoreData.CurrentSchema, store.Data.SchemaVersion);
    }

    [Fact]
    public void Numbers_go_back_into_the_settings_ranges()
    {
        File.WriteAllText(DataFile, """{"Settings":{"HoverDelayMs":-5,"FocusMinutes":0,"SessionsPerCycle":99}}""");
        var settings = AppStore.Load(DataFile).Settings;

        Assert.Equal(0, settings.HoverDelayMs);
        Assert.Equal(5, settings.FocusMinutes);
        Assert.Equal(8, settings.SessionsPerCycle);
    }

    [Fact]
    public void A_file_from_before_the_schema_version_gets_it_and_nothing_else_changes()
    {
        var data = new StoreData { SchemaVersion = 0 };
        Assert.False(data.Normalize());
        Assert.Equal(StoreData.CurrentSchema, data.SchemaVersion);
    }
}

/// <summary>The diagnostics text helps with an issue without saying anything about the user.</summary>
public class DiagnosticsTests
{
    [Fact]
    public void Leaves_out_place_coordinates_and_task_list()
    {
        var settings = new SettingsData
        {
            WeatherEnabled = true,
            WeatherPlace = "Campinas, São Paulo",
            WeatherLatitude = -22.91,
            WeatherLongitude = -47.06,
            TasksSyncEnabled = true,
            TasksListName = "Compras da casa",
            Monitor = @"\\.\DISPLAY2",
        };
        var displays = new[] { new DisplayInfo(@"\\.\DISPLAY1", "Dell U2720Q", 0, 0, 3840, 2160, true) };

        string text = Diagnostics.Describe(settings, GoogleAccountStatus.Connected, displays, "Windows 26H2 (build 26300)",
            Path.Combine(Path.GetTempPath(), "yata-tests", "no-logs"));

        Assert.Contains("Tempo: ligado", text);
        Assert.Contains("3840×2160 principal", text);
        foreach (string personal in new[] { "Campinas", "22.91", "22,91", "47.06", "Compras", "DISPLAY", "Dell" })
            Assert.DoesNotContain(personal, text);
    }
}
