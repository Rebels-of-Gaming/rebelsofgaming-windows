using System.Net;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.Shop;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Features.Shop;

public class ShopServiceTests
{
    private static RoGApi Create(Func<HttpRequestMessage, int, HttpResponseMessage> respond, out StubHttpHandler stub)
    {
        stub = new StubHttpHandler(respond);
        return new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: stub);
    }

    [Fact]
    public async Task Overview_reads_products_and_donations()
    {
        using var api = Create((req, _) => req.RequestUri!.AbsolutePath.EndsWith("/products", StringComparison.Ordinal)
            ? StubHttpHandler.Json(HttpStatusCode.OK, """
                {"data":[{"id":"01P","slug":"vip-30","name":"VIP (30 Tage)","description":"Kosmetisch","price":{"amount":499,"currency":"EUR","formatted":"4,99 €"},
                  "type":"role","role":{"name":"vip","label":"VIP","color":"#D4AE3C"},"duration_days":30,"permanent":false,
                  "providers":[{"key":"stripe","label":"Karte","sandbox":false},{"key":"paypal","label":"PayPal","sandbox":false}]}]}
                """)
            : StubHttpHandler.Json(HttpStatusCode.OK, """
                {"data":{"min":100,"max":50000,"currency":"EUR","providers":[{"key":"paypal","label":"PayPal","sandbox":false}],
                  "supporters":[{"name":"Kolibri","month":"2026-10"}]}}
                """), out _);

        var shop = await new ShopService(api.Http).OverviewAsync();

        Assert.True(shop.Enabled);
        var vip = Assert.Single(shop.Products);
        Assert.Equal("4,99 €", vip.Price.Formatted);
        Assert.Equal(30, vip.DurationDays);
        Assert.Equal(["Karte", "PayPal"], vip.Providers);
        Assert.Equal(("Kolibri", "2026-10"), Assert.Single(shop.Donations!.Supporters));
    }

    [Fact]
    public async Task Disabled_shop_and_donations_are_not_errors()
    {
        using var api = Create((req, _) => StubHttpHandler.Json(HttpStatusCode.NotFound,
            req.RequestUri!.AbsolutePath.EndsWith("/products", StringComparison.Ordinal)
                ? """{"type":"about:blank","title":"Shop aus","status":404,"code":"shop_disabled"}"""
                : """{"type":"about:blank","title":"Spenden aus","status":404,"code":"donations_disabled"}""",
            "application/problem+json"), out _);

        var shop = await new ShopService(api.Http).OverviewAsync();

        Assert.False(shop.Enabled);
        Assert.Empty(shop.Products);
        Assert.Null(shop.Donations);
    }

    [Fact]
    public async Task Orders_page_with_cursor()
    {
        using var api = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, """
            {"data":[{"id":"01O","number":"RoG-2026-0042","kind":"product","status":"paid","failure_reason":null,
              "item":{"name":"VIP (30 Tage)","role":"vip","duration_days":30},"price":{"amount":499,"currency":"EUR","formatted":"4,99 €"},
              "provider":"stripe","sandbox":false,"entitlement":{"role":"vip","permanent":false,"expires_at":"2026-12-01T00:00:00Z"},
              "terms_accepted_at":null,"withdrawal_waiver_at":null,"donor_public":false,"donor_name":null,
              "paid_at":"2026-11-01T12:00:00Z","refunded_at":null,"created_at":"2026-11-01T11:59:00Z","receipt_url":"https://rebelsofgaming.com/de/shop/orders/01O"}],
             "links":{},"meta":{"per_page":25,"next_cursor":null}}
            """), out var stub);

        var page = await new ShopService(api.Http).OrdersAsync("abc");

        Assert.Contains("cursor=abc", Assert.Single(stub.Requests).Uri!.Query, StringComparison.Ordinal);
        var order = Assert.Single(page.Items);
        Assert.Equal("paid", order.Status);
        Assert.NotNull(order.ExpiresAt);
        Assert.Equal("https://rebelsofgaming.com/de/shop/orders/01O", order.ReceiptUrl!.ToString());
        Assert.Null(page.NextCursor);
    }
}
