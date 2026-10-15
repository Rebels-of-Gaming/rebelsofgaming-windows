using System.Xml.Linq;
using RoG.Core.Tests.Localization;

namespace RoG.Core.Tests;

/// <summary>Prüft Dateien der WinUI-App, die unter Linux nicht gebaut wird (Version, Manifest, Assets, Schriften).</summary>
public class AppProjectConsistencyTests
{
    private static readonly XNamespace s_foundation = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
    private static readonly XNamespace s_uap = "http://schemas.microsoft.com/appx/manifest/uap/windows10";

    private static string Root => ReswCompletenessTests.RepoRoot();

    private static string RoGVersion() =>
        XDocument.Load(Path.Combine(Root, "Directory.Build.props")).Descendants("RoGVersion").Single().Value;

    private static XDocument Manifest() => XDocument.Load(Path.Combine(Root, "src", "RoG.App", "Package.appxmanifest"));

    [Fact]
    public void Msix_version_matches_rog_version_with_zero_revision()
    {
        var identity = Manifest().Root!.Element(s_foundation + "Identity")!;
        Assert.Equal(RoGVersion() + ".0", (string)identity.Attribute("Version")!);
    }

    [Fact]
    public void Changelog_has_unreleased_section()
    {
        var changelog = File.ReadAllText(Path.Combine(Root, "CHANGELOG.md"));
        Assert.Contains("## [Unveröffentlicht]", changelog);
    }

    [Fact]
    public void Manifest_assets_exist_as_scale_200_or_targetsize_files()
    {
        var manifest = Manifest().ToString();
        var assets = new[] { "StoreLogo", "Square150x150Logo", "Square44x44Logo", "Wide310x150Logo", "Square71x71Logo", "Square310x310Logo", "SplashScreen" };
        var dir = Path.Combine(Root, "src", "RoG.App", "Assets");
        foreach (var asset in assets)
        {
            Assert.Contains($"Assets\\{asset}.png", manifest);
            Assert.True(File.Exists(Path.Combine(dir, $"{asset}.scale-200.png")), $"{asset}.scale-200.png fehlt");
        }
    }

    [Fact]
    public void Startup_task_is_disabled_by_default()
    {
        var startup = Manifest().Descendants().Single(e => e.Name.LocalName == "StartupTask");
        Assert.Equal("false", (string)startup.Attribute("Enabled")!);
    }

    [Fact]
    public void Only_expected_capabilities_are_declared()
    {
        var capabilities = Manifest().Descendants().Where(e => e.Name.LocalName == "Capability")
            .Select(e => (string)e.Attribute("Name")!).Order().ToList();
        Assert.Equal(["internetClient", "runFullTrust"], capabilities);
    }

    [Fact]
    public void Embedded_fonts_ship_with_their_license()
    {
        var fonts = Path.Combine(Root, "src", "RoG.App", "Fonts");
        Assert.True(File.Exists(Path.Combine(fonts, "BarlowCondensed-SemiBold.ttf")));
        Assert.True(File.Exists(Path.Combine(fonts, "BarlowCondensed-Bold.ttf")));
        Assert.Contains("SIL Open Font License", File.ReadAllText(Path.Combine(fonts, "OFL.txt")));
    }

    [Fact]
    public void Splash_background_is_rog_bg()
    {
        var splash = Manifest().Descendants(s_uap + "SplashScreen").Single();
        Assert.Equal("#1C1F26", (string)splash.Attribute("BackgroundColor")!);
    }
}
