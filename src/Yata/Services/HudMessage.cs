namespace Yata.Services;

/// <summary>A short-lived status shown in the compact notch (volume, battery, Caps Lock, ...).</summary>
/// <param name="Glyph">Segoe Fluent Icons glyph.</param>
/// <param name="Text">Main label.</param>
/// <param name="Value">Optional value on the right, e.g. "62%".</param>
/// <param name="Level">Optional 0..1 level drawn as a bar.</param>
/// <param name="Warning">Highlights the icon, e.g. for low battery.</param>
/// <param name="DurationMs">How long the message stays up.</param>
/// <param name="Important">Reminders: held back while the notch is open or hidden, instead of dropped.</param>
public sealed record HudMessage(
    string Glyph,
    string Text,
    string? Value = null,
    double? Level = null,
    bool Warning = false,
    int DurationMs = 1800,
    bool Important = false);

internal static class Glyphs
{
    public const string Volume0 = "";
    public const string Volume1 = "";
    public const string Volume2 = "";
    public const string Volume3 = "";
    public const string Mute = "";
    public const string Brightness = "";
    public const string Battery = "";
    public const string BatteryLow = "";
    public const string Charging = "";
    public const string Headphones = "";
    public const string Speakers = "";
    public const string Keyboard = "";
    public const string Wifi = "";
    public const string Ethernet = "";
    public const string Offline = "";
    public const string Lock = "";
    public const string Timer = "";
    public const string Water = "";
    public const string Bell = "";
}
