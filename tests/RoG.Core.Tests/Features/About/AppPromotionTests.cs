using RoG.Core.Features.About;
using RoG.Core.Navigation;
using RoG.Core.Settings;

namespace RoG.Core.Tests.Features.About;

public class AppPromotionTests
{
    private static readonly DateTimeOffset s_now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private static AppSettings Ready() => new()
    {
        FirstRunAt = s_now.AddDays(-30),
        TourVersion = FeatureTour.Version,
        ReviewMoments = ReviewPrompt.MomentsNeeded,
    };

    [Fact]
    public void Share_text_lists_windows_first_and_skips_missing_stores()
    {
        var links = StoreLinks.From("https://apps.example/rog", null, "https://ms.example/rog");

        Assert.Equal(
            "RoG\nMicrosoft Store: https://ms.example/rog\nApp Store: https://apps.example/rog",
            links.ShareText("RoG", "Microsoft Store", "App Store", "Google Play"));
    }

    [Fact]
    public void Nothing_to_share_without_links()
    {
        var links = StoreLinks.From(null, "  ", "javascript:alert(1)");

        Assert.False(links.HasAny);
        Assert.Null(links.ShareText("RoG", "a", "b", "c"));
    }

    [Fact]
    public void Asks_for_a_review_only_when_everything_fits()
    {
        Assert.True(ReviewPrompt.ShouldAsk(Ready(), s_now, isStoreInstall: true));
        Assert.False(ReviewPrompt.ShouldAsk(Ready(), s_now, isStoreInstall: false));
        Assert.False(ReviewPrompt.ShouldAsk(Ready() with { ReviewNeverAsk = true }, s_now, true));
        Assert.False(ReviewPrompt.ShouldAsk(Ready() with { TourVersion = 0 }, s_now, true));
        Assert.False(ReviewPrompt.ShouldAsk(Ready() with { GuestMode = true }, s_now, true));
        Assert.False(ReviewPrompt.ShouldAsk(Ready() with { FirstRunAt = s_now.AddDays(-2) }, s_now, true));
        Assert.False(ReviewPrompt.ShouldAsk(Ready() with { FirstRunAt = null }, s_now, true));
        Assert.False(ReviewPrompt.ShouldAsk(Ready() with { ReviewMoments = ReviewPrompt.MomentsNeeded - 1 }, s_now, true));
    }

    [Fact]
    public void Asks_at_most_every_120_days()
    {
        var asked = ReviewPrompt.Asked(Ready(), s_now, never: false);

        Assert.Equal(0, asked.ReviewMoments);
        Assert.False(asked.ReviewNeverAsk);
        var again = asked with { ReviewMoments = ReviewPrompt.MomentsNeeded };
        Assert.False(ReviewPrompt.ShouldAsk(again, s_now.AddDays(119), true));
        Assert.True(ReviewPrompt.ShouldAsk(again, s_now.AddDays(120), true));
    }

    [Fact]
    public void Never_ask_sticks()
    {
        var never = ReviewPrompt.Asked(Ready(), s_now, never: true);
        var later = ReviewPrompt.Asked(never, s_now.AddDays(200), never: false);

        Assert.True(later.ReviewNeverAsk);
        Assert.False(ReviewPrompt.ShouldAsk(later with { ReviewMoments = 5 }, s_now.AddDays(400), true));
    }

    [Fact]
    public void Moments_are_capped()
    {
        var settings = new AppSettings();
        for (var i = 0; i < 10; i++)
        {
            settings = ReviewPrompt.Moment(settings);
        }

        Assert.Equal(ReviewPrompt.MomentsNeeded, settings.ReviewMoments);
    }

    [Fact]
    public void Tour_shows_once_for_signed_in_users()
    {
        Assert.True(FeatureTour.ShouldShow(new AppSettings(), signedIn: true));
        Assert.False(FeatureTour.ShouldShow(new AppSettings(), signedIn: false));
        Assert.False(FeatureTour.ShouldShow(new AppSettings { GuestMode = true }, signedIn: true));
        Assert.False(FeatureTour.ShouldShow(FeatureTour.Seen(new AppSettings()), signedIn: true));
    }

    [Fact]
    public void Tour_has_four_to_six_distinct_cards()
    {
        Assert.InRange(FeatureTour.Cards.Count, 4, 6);
        Assert.Equal(FeatureTour.Cards.Count, FeatureTour.Cards.Select(c => c.Key).Distinct().Count());
        Assert.Contains(FeatureTour.Cards, c => c.Section == AppSection.Forum);
    }

    [Fact]
    public void Review_uri_points_to_the_store_product()
    {
        Assert.Equal("ms-windows-store", StoreProduct.ReviewUri.Scheme);
        Assert.Contains(StoreProduct.Id, StoreProduct.ReviewUri.OriginalString, StringComparison.Ordinal);
    }
}
