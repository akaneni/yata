using System;
using System.Globalization;
using System.Linq;

namespace Yata.Helpers;

/// <summary>
/// The language Yata speaks: Portuguese on a Portuguese Windows, English everywhere else, unless
/// the user picked one in Settings. Chosen once at startup, before any window is built.
/// </summary>
internal static class Lang
{
    /// <summary>Settings values; null in the settings follows Windows.</summary>
    public static readonly string[] Supported = { "en", "pt-BR" };

    // Read before Apply changes it.
    private static readonly CultureInfo Windows = CultureInfo.CurrentUICulture;

    /// <summary>For dates and numbers written in words or in the interface's own formats.</summary>
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en-US");

    /// <summary>What Windows alone would pick.</summary>
    public static string FromWindows => Resolve(null, Windows);

    internal static string Resolve(string? choice, CultureInfo windows) =>
        choice != null && Supported.Contains(choice) ? choice
        : windows.TwoLetterISOLanguageName == "pt" ? "pt-BR"
        : "en";

    public static void Apply(string? choice)
    {
        string language = Resolve(choice, Windows);
        var ui = CultureInfo.GetCultureInfo(language);
        CultureInfo.CurrentUICulture = ui;
        CultureInfo.DefaultThreadCurrentUICulture = ui;
        // Also for threads that already exist (watchers raise notices from their own threads).
        Strings.Culture = ui;
        Culture = CultureInfo.GetCultureInfo(language == "en" ? "en-US" : language);
    }

    /// <summary>"today at 16:42", or "27/10 at 16:42" for another day: when something last happened.</summary>
    public static string When(DateTime at, DateTime now)
    {
        string time = at.ToString("HH:mm", Culture);
        return at.Date == now.Date
            ? string.Format(Strings.Time_TodayAt, time)
            : string.Format(Strings.Time_DayAt, at.ToString(Culture.DateTimeFormat.MonthDayPattern, Culture), time);
    }

    /// <summary>Each language in its own words, as language pickers do.</summary>
    public static string Name(string language) => language == "pt-BR" ? "Português" : "English";
}
