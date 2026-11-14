using Gst.Api.Tests.Support;
using System.Net;
using System.Text.Json;

namespace Gst.Api.Tests;

[Collection(EndpointCollection.Name)]
public class RefreshRateLimitTests
{
    [Fact]
    public async Task Refresh_ThirtyOneRequestsInAMinute_ThirtyFirstReturns429()
    {
        using var factory = new ApiFactory(); // apna instance — limiter state alag
        var client = factory.CreateApiClient();

        // Cookie nahi => 401 (DB nahi chhuta); limiter controller se pehle chalta hai
        var statuses = new List<HttpStatusCode>();
        HttpResponseMessage? last = null;
        for (var i = 0; i < 31; i++)
        {
            last = await client.PostAsync("/api/auth/refresh", null);
            statuses.Add(last.StatusCode);
        }

        Assert.Equal(31, statuses.Count);
        Assert.All(statuses.Take(30), s => Assert.Equal(HttpStatusCode.Unauthorized, s));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[30]);
        var root = JsonDocument.Parse(await last!.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(429, root.GetProperty("code").GetInt32());
        Assert.Equal("Too many requests. Please try again later.", root.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Logout_ThirtyFiveRequestsInAMinute_NeverRateLimited()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateApiClient();
        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 35; i++)
            statuses.Add((await client.PostAsync("/api/auth/logout", null)).StatusCode);

        Assert.Equal(35, statuses.Count);
        Assert.All(statuses, s => Assert.Equal(HttpStatusCode.OK, s));
    }
}
