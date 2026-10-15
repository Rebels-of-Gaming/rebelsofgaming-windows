using System.Text.RegularExpressions;
using System.Xml.Linq;
using RoG.Core.Localization;

namespace RoG.Core.Tests.Localization;

/// <summary>
/// Prüft die Übersetzungen der WinUI-App (src/RoG.App/Strings/*/Resources.resw) unter Linux – die App selbst baut nur
/// unter Windows, die Texte sollen trotzdem bei jedem Commit vollständig sein.
/// </summary>
public partial class ReswCompletenessTests
{
    private static readonly string[] s_languages = ["de-DE", "en-US", "fr-FR"];

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RoG.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("RoG.sln nicht gefunden.");
    }

    private static Dictionary<string, string> Load(string language)
    {
        var path = Path.Combine(RepoRoot(), "src", "RoG.App", "Strings", language, "Resources.resw");
        return XDocument.Load(path).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? string.Empty, StringComparer.Ordinal);
    }

    [Fact]
    public void All_languages_have_the_same_keys()
    {
        var de = Load("de-DE");
        foreach (var language in s_languages.Skip(1))
        {
            var other = Load(language);
            Assert.Empty(de.Keys.Except(other.Keys));
            Assert.Empty(other.Keys.Except(de.Keys));
        }
    }

    [Fact]
    public void No_value_is_empty()
    {
        foreach (var language in s_languages)
        {
            var empty = Load(language).Where(kv => string.IsNullOrWhiteSpace(kv.Value)).Select(kv => $"{language}:{kv.Key}").ToList();
            Assert.Empty(empty);
        }
    }

    [Fact]
    public void Every_code_key_exists()
    {
        var de = Load("de-DE");
        var missing = StringKeys.All().Where(k => !de.ContainsKey(k)).ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void Code_keys_contain_no_dots()
    {
        Assert.All(StringKeys.All(), k => Assert.DoesNotContain('.', k));
    }

    [Fact]
    public void Every_xuid_in_xaml_has_texts()
    {
        var de = Load("de-DE");
        var appDir = Path.Combine(RepoRoot(), "src", "RoG.App");
        var uids = Directory.EnumerateFiles(appDir, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(f => UidPattern().Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
            .Distinct()
            .ToList();

        Assert.NotEmpty(uids);
        var missing = uids.Where(uid => !de.Keys.Any(k => k.StartsWith(uid + ".", StringComparison.Ordinal))).ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void Navigation_tags_are_string_keys()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", "RoG.App", "MainWindow.xaml"));
        var tags = TagPattern().Matches(xaml).Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(tags);
        Assert.All(tags, t => Assert.Contains(t, StringKeys.All()));
    }

    [GeneratedRegex("x:Uid=\"([^\"]+)\"")]
    private static partial Regex UidPattern();

    [GeneratedRegex("Tag=\"(Nav_[A-Za-z]+)\"")]
    private static partial Regex TagPattern();
}
