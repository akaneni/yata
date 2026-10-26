using System.Text.Json;
using Yata.Services;

namespace Yata.Tests;

/// <summary>Open-Meteo responses, WMO codes and the rain heads-up.</summary>
public class WeatherTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 14, 20, 0);

    private static WeatherReport? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return Weather.Parse(doc.RootElement, Now);
    }

    private static string Forecast(string chances, int code = 2, int isDay = 1) => $$"""
        {
          "current": {"time":"2026-10-26T14:15","temperature_2m":23.6,"weather_code":{{code}},"is_day":{{isDay}}},
          "hourly": {
            "time": ["2026-10-26T13:00","2026-10-26T14:00","2026-10-26T15:00","2026-10-26T16:00","2026-10-26T17:00"],
            "precipitation_probability": [{{chances}}]
          },
          "daily": {"time":["2026-10-26","2026-10-27"],"temperature_2m_max":[27.4,25.0],"temperature_2m_min":[17.6,16.0]}
        }
        """;

    [Fact]
    public void Current_conditions_and_today_range_are_read()
    {
        var report = Parse(Forecast("0,0,0,0,0"))!;

        Assert.Equal("24°", report.TemperatureText);
        Assert.Equal("Parcialmente nublado", report.Description);
        Assert.Equal(WeatherIcon.PartlyCloudy, report.Icon);
        Assert.Equal(27.4, report.High);
        Assert.Equal(17.6, report.Low);
        Assert.Null(report.RainAt);
    }

    [Fact]
    public void Rain_within_two_hours_is_flagged_with_its_hour()
    {
        var report = Parse(Forecast("90,10,30,70,95"))!;

        // 13:00 already passed; 16:00 is the first likely hour inside the window.
        Assert.Equal(new DateTime(2026, 10, 26, 16, 0, 0), report.RainAt);
        Assert.Equal(70, report.RainChance);
    }

    [Fact]
    public void Rain_beyond_the_window_is_ignored()
    {
        // Only 17:00 is likely, but that's more than 2 hours after 14:20.
        Assert.Null(Parse(Forecast("0,0,0,0,95"))!.RainAt);
    }

    [Fact]
    public void Current_hour_counts_as_soon()
    {
        Assert.Equal(new DateTime(2026, 10, 26, 14, 0, 0), Parse(Forecast("0,80,0,0,0"))!.RainAt);
    }

    [Fact]
    public void Missing_current_block_gives_no_report() => Assert.Null(Parse("""{"hourly":{}}"""));

    [Theory]
    [InlineData(0, 1, WeatherIcon.Clear)]
    [InlineData(1, 0, WeatherIcon.ClearNight)]
    [InlineData(3, 1, WeatherIcon.Cloudy)]
    [InlineData(45, 1, WeatherIcon.Fog)]
    [InlineData(53, 1, WeatherIcon.Rain)]
    [InlineData(81, 1, WeatherIcon.Rain)]
    [InlineData(75, 1, WeatherIcon.Snow)]
    [InlineData(95, 1, WeatherIcon.Storm)]
    [InlineData(99, 0, WeatherIcon.Storm)]
    public void Wmo_codes_map_to_icons(int code, int isDay, WeatherIcon expected) =>
        Assert.Equal(expected, Weather.IconFor(code, isDay == 1));

    [Fact]
    public void Night_flag_is_read()
    {
        Assert.Equal(WeatherIcon.ClearNight, Parse(Forecast("0,0,0,0,0", code: 0, isDay: 0))!.Icon);
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(51, true)]
    [InlineData(95, true)]
    public void Wet_codes_suppress_the_rain_notice(int code, bool wet) => Assert.Equal(wet, Weather.IsWet(code));

    [Fact]
    public void Coordinates_are_coarsened_to_about_a_kilometer()
    {
        Assert.Equal(-22.91, Weather.Coarse(-22.90556));
        Assert.Equal(-47.06, Weather.Coarse(-47.06083));
    }

    [Fact]
    public void Places_carry_state_and_country()
    {
        using var doc = JsonDocument.Parse("""
            {"results":[
              {"name":"Campinas","latitude":-22.90556,"longitude":-47.06083,"admin1":"São Paulo","country":"Brasil"},
              {"name":"Sem coordenadas"},
              {"name":"Lugar","latitude":1,"longitude":2}
            ]}
            """);
        var places = Weather.ParsePlaces(doc.RootElement);

        Assert.Equal(2, places.Count);
        Assert.Equal("São Paulo, Brasil", places[0].Detail);
        Assert.Equal("", places[1].Detail);
    }

    [Fact]
    public void No_results_is_an_empty_list()
    {
        using var doc = JsonDocument.Parse("""{"generationtime_ms":0.5}""");
        Assert.Empty(Weather.ParsePlaces(doc.RootElement));
    }
}
