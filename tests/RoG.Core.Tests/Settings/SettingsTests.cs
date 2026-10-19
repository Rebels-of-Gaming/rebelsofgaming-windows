using System.Globalization;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Settings;

namespace RoG.Core.Tests.Settings;

public sealed class SettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rog-windows-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public async Task Missing_file_gives_defaults()
    {
        var settings = await new JsonFileSettingsStore(Path.Combine(_dir, "settings.json")).LoadAsync();
        Assert.Equal(LanguageResolver.System, settings.Language);
        Assert.Equal(RoGEnvironment.Production, settings.Environment);
        Assert.True(settings.CloseToTray);
        Assert.False(settings.StartWithWindows);
    }

    [Fact]
    public async Task Window_placement_and_last_section_roundtrip()
    {
        var store = new JsonFileSettingsStore(Path.Combine(_dir, "settings.json"));
        await store.SaveAsync(new AppSettings { Window = new WindowPlacement(10, 20, 1280, 820, true), LastSection = "Nav_Servers" });

        var loaded = await store.LoadAsync();

        Assert.Equal(new WindowPlacement(10, 20, 1280, 820, true), loaded.Window);
        Assert.Equal("Nav_Servers", loaded.LastSection);
    }

    [Fact]
    public void Window_placement_is_fitted_into_the_visible_area()
    {
        // Fenster vom abgesteckten zweiten Monitor (x = 2500) auf einen 1920×1040-Bereich
        var fitted = new WindowPlacement(2500, 100, 1280, 820, false).FitInto(0, 0, 1920, 1040);
        Assert.Equal(new WindowPlacement(640, 100, 1280, 820, false), fitted);

        // zu groß → auf den Bereich verkleinert
        Assert.Equal(new WindowPlacement(0, 0, 1366, 728, false), new WindowPlacement(-50, -50, 2560, 1400, false).FitInto(0, 0, 1366, 728));

        // unbrauchbar klein → Standard
        Assert.Null(new WindowPlacement(0, 0, 100, 100, false).FitInto(0, 0, 1920, 1040));
    }

    [Fact]
    public async Task Settings_roundtrip_atomically()
    {
        var path = Path.Combine(_dir, "sub", "settings.json");
        var store = new JsonFileSettingsStore(path);
        await store.SaveAsync(new AppSettings { Language = "fr", StartWithWindows = true, GuestMode = true });

        var loaded = await store.LoadAsync();

        Assert.Equal("fr", loaded.Language);
        Assert.True(loaded.StartWithWindows);
        Assert.True(loaded.GuestMode);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task Corrupt_file_falls_back_to_defaults()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "settings.json");
        await File.WriteAllTextAsync(path, "{ kaputt");

        var loaded = await new JsonFileSettingsStore(path).LoadAsync();
        Assert.Equal(new AppSettings(), loaded);
    }

    [Fact]
    public async Task Settings_never_contain_the_token()
    {
        var path = Path.Combine(_dir, "settings.json");
        await new JsonFileSettingsStore(path).SaveAsync(new AppSettings());
        var json = await File.ReadAllTextAsync(path);
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("de", "en-US", "de")]
    [InlineData("fr", "de-DE", "fr")]
    [InlineData("system", "en-GB", "en")]
    [InlineData("system", "fr-LU", "fr")]
    [InlineData("system", "lb-LU", "de")]
    [InlineData("system", "es-ES", "de")]
    [InlineData("xx", "en-US", "en")]
    [InlineData(null, "it-IT", "de")]
    public void Language_resolution_defaults_to_german(string? setting, string culture, string expected) =>
        Assert.Equal(expected, LanguageResolver.Resolve(setting, new CultureInfo(culture)));

    [Theory]
    [InlineData("de", "de-DE")]
    [InlineData("en", "en-US")]
    [InlineData("fr", "fr-FR")]
    public void Resource_tags_match_resw_folders(string language, string tag) =>
        Assert.Equal(tag, LanguageResolver.ToResourceTag(language));

    [Fact]
    public void Environments_are_loaded_from_embedded_config()
    {
        var prod = RoGEnvironment.Get(RoGEnvironment.Production);
        Assert.Equal("https://api.rebelsofgaming.com/v1/", prod.ApiBaseUrl.ToString());
        Assert.Equal("https://rebelsofgaming.com/auth/app/authorize", prod.AuthorizeUrl.ToString());
        Assert.Equal("rog-app", prod.ClientId);
        Assert.Equal("https://api.rebelsofgaming.dev/v1/", RoGEnvironment.Get(RoGEnvironment.Staging).ApiBaseUrl.ToString());
        Assert.Same(prod, RoGEnvironment.Get("unbekannt"));
    }

    [Theory]
    [InlineData("de", "https://rebelsofgaming.com/de/u/sariela")]
    [InlineData("en", "https://rebelsofgaming.com/en/u/sariela")]
    [InlineData("fr", "https://rebelsofgaming.com/fr/u/sariela")]
    [InlineData(null, "https://rebelsofgaming.com/de/u/sariela")]
    [InlineData("it", "https://rebelsofgaming.com/de/u/sariela")]
    public void Website_pages_carry_the_language_prefix(string? language, string expected)
    {
        var prod = RoGEnvironment.Get(RoGEnvironment.Production);
        Assert.Equal(expected, prod.ProfilePage(language, "sariela").ToString());
        Assert.Equal($"https://rebelsofgaming.com/{language switch { "en" or "fr" => language, _ => "de" }}/settings/account", prod.WebPage(language, "/settings/account").ToString());
    }

    [Theory]
    [InlineData("Wahke", "Windows-App – PC von Wahke")]
    [InlineData("  Wahke\t", "Windows-App – PC von Wahke")]
    [InlineData("", "Windows-App")]
    [InlineData(null, "Windows-App")]
    public void Device_name_for_windows(string? owner, string expected) => Assert.Equal(expected, DeviceName.ForWindows(owner));

    [Fact]
    public void Device_name_is_capped_at_80_chars()
    {
        var name = DeviceName.ForWindows(new string('x', 200));
        Assert.Equal(DeviceName.MaxLength, name.Length);
        Assert.StartsWith("Windows-App – PC von ", name);
    }

    [Fact]
    public void Scope_list_comes_from_meta_app_scopes_without_excluded_scopes()
    {
        var scopes = AppScopes.FromScopesResponse("""
            {"data":[],"meta":{"app_client":"rog-app","app_scopes":["profile:read","devices:manage","tokens:manage","webhooks:write","servers:power"]}}
            """);
        Assert.Equal(["profile:read", "devices:manage", "servers:power"], scopes);
        Assert.Same(AppScopes.Fallback, AppScopes.FromScopesResponse("{}"));
        Assert.Contains("devices:manage", AppScopes.Fallback);
    }
}
