using RoG.Core.Features.Admin;

namespace RoG.Core.Tests.Features;

public class AdminOverviewTests
{
    [Fact]
    public void Kpis_and_system_status_are_parsed()
    {
        using var kpis = System.Text.Json.JsonDocument.Parse("""
            {"data":{"kpis":[{"key":"members","label":"Mitglieder","value":42,"url":"https://admin.rebelsofgaming.com/members"},
                             {"key":"online","label":"Online","value":null,"url":null}],"system":null}}
            """);
        using var system = System.Text.Json.JsonDocument.Parse("""
            {"data":{"status":"degraded","checked_at":"2026-10-11T20:15:00Z","app":{"version":"0.12.0"},
              "checks":[{"key":"database","label":"Datenbank","ok":true,"value":"mariadb","critical":true,"help":null},
                        {"key":"mail","label":"Mailversand","ok":false,"value":"auth failed","critical":false,"help":"SMTP prüfen"},
                        {"key":"twitch","label":"Twitch","ok":null,"value":"nicht eingerichtet","critical":false,"help":null}]}}
            """);

        var k = AdminOverviewService.ParseKpis(kpis.RootElement);
        var s = AdminOverviewService.ParseSystem(system.RootElement);

        Assert.Equal("42", k[0].Value);
        Assert.Equal("–", k[1].Value);
        Assert.Equal("degraded", s!.Status);
        Assert.Equal([true, false, null], s.Checks.Select(c => c.Ok));
        Assert.Equal("SMTP prüfen", s.Checks[1].Help);
        Assert.Null(AdminOverviewService.ParseSystem(System.Text.Json.JsonDocument.Parse("""{"data":null}""").RootElement));
    }
}
