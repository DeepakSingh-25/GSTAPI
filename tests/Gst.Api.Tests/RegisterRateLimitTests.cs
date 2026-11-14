using Gst.Api.Tests.Support;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Gst.Api.Tests;

[Collection(EndpointCollection.Name)]
public class RegisterRateLimitTests
{
    [Fact]
    public async Task Register_ElevenRequestsInAMinute_EleventhReturns429()
    {
        using var factory = new ApiFactory(); // apna instance — limiter state alag
        var client = factory.CreateApiClient();

        // Invalid body => 400 (bcrypt / DB nahi), limiter controller se pehle chalta hai
        var statuses = new List<HttpStatusCode>();
        HttpResponseMessage? last = null;
        for (var i = 0; i < 11; i++)
        {
            last = await client.PostAsJsonAsync("/api/auth/register", new { });
            statuses.Add(last.StatusCode);
        }

        Assert.All(statuses.Take(10), s => Assert.Equal(HttpStatusCode.BadRequest, s));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[10]);
        var root = JsonDocument.Parse(await last!.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(429, root.GetProperty("code").GetInt32());
        Assert.Equal("Too many requests. Please try again later.", root.GetProperty("error").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("response").ValueKind);
    }
}
