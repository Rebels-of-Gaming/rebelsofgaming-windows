using RoG.Core.Config;
using RoG.Core.Navigation;

namespace RoG.Core.Tests.Navigation;

public class AppRouteTests
{
    private static readonly RoGEnvironment s_env = RoGEnvironment.Get(RoGEnvironment.Production);

    [Theory]
    [InlineData("https://rebelsofgaming.com/de/news/patch-notes", AppSection.News, "patch-notes")]
    [InlineData("https://rebelsofgaming.com/en/news", AppSection.News, null)]
    [InlineData("https://rebelsofgaming.com/u/wahke", AppSection.Members, "wahke")]
    [InlineData("https://www.rebelsofgaming.com/squads/et", AppSection.Squads, "et")]
    [InlineData("https://rebelsofgaming.com/fr/events/01JABC", AppSection.Calendar, "01JABC")]
    [InlineData("https://rebelsofgaming.com/matches/rog-vs-xyz", AppSection.Matches, "rog-vs-xyz")]
    [InlineData("https://rebelsofgaming.com/forum/general/thread-1", AppSection.Forum, "general/thread-1")]
    [InlineData("https://rebelsofgaming.com/de/privacy", AppSection.Info, "privacy")]
    [InlineData("https://rebelsofgaming.com/", AppSection.Home, null)]
    [InlineData("https://wiki.rebelsofgaming.com/Enemy_Territory", AppSection.Wiki, "Enemy_Territory")]
    public void Maps_platform_urls(string url, AppSection section, string? key)
    {
        var route = AppRoute.FromWebUrl(new Uri(url), s_env);
        Assert.NotNull(route);
        Assert.Equal(section, route.Section);
        Assert.Equal(key, route.Key);
    }

    [Theory]
    [InlineData("https://example.com/de/news/x")]
    [InlineData("https://rebelsofgaming.com/admin/users")]
    [InlineData("ftp://rebelsofgaming.com/news/x")]
    public void Unknown_urls_stay_in_the_browser(string url) => Assert.Null(AppRoute.FromWebUrl(new Uri(url), s_env));
}

public class AppUriTests
{
    private static readonly RoG.Core.Config.RoGEnvironment s_env = RoG.Core.Config.RoGEnvironment.Get(RoG.Core.Config.RoGEnvironment.Production);

    [Theory]
    [InlineData("rog://news/patch-notes", AppSection.News, "patch-notes")]
    [InlineData("rog://u/wahke", AppSection.Members, "wahke")]
    [InlineData("rog://events/01JABC", AppSection.Calendar, "01JABC")]
    [InlineData("rog://servers", AppSection.Servers, null)]
    [InlineData("rog://notifications", AppSection.Notifications, null)]
    [InlineData("rog://search?q=enemy%20territory", AppSection.Search, "enemy territory")]
    [InlineData("rog://open?url=https%3A%2F%2Frebelsofgaming.com%2Fde%2Fnews%2Fx", AppSection.News, "x")]
    [InlineData("ROG://Messages", AppSection.Messages, null)]
    [InlineData("rog://votes", AppSection.Votes, null)]
    [InlineData("rog://uploads", AppSection.Uploads, null)]
    [InlineData("rog://admin", AppSection.Admin, null)]
    public void Maps_app_links(string uri, AppSection section, string? key)
    {
        var route = AppRoute.FromAppUri(new Uri(uri), s_env);
        Assert.NotNull(route);
        Assert.Equal(section, route.Section);
        Assert.Equal(key, route.Key);
    }

    [Theory]
    [InlineData("rog://auth/callback?code=x&state=y")]
    [InlineData("rog://search")]
    [InlineData("rog://open?url=https%3A%2F%2Fevil.example%2Fx")]
    [InlineData("https://rebelsofgaming.com/news/x")]
    public void Ignores_foreign_or_incomplete_links(string uri) => Assert.Null(AppRoute.FromAppUri(new Uri(uri), s_env));
}
