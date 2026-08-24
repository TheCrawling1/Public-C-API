using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace ApiRouter.Tests.Integration;

/// <summary>
/// Drives the real HTTP pipeline (auth, authorization, dispatch execution, SSRF guard)
/// through an in-memory server — the highest-risk paths previously exercised only by hand.
/// </summary>
public class ApiIntegrationTests : IClassFixture<ApiRouterFactory>
{
    private const string AdminKey = "demo-key-please-change";

    private readonly ApiRouterFactory _factory;

    public ApiIntegrationTests(ApiRouterFactory factory) => _factory = factory;

    private static HttpRequestMessage Req(HttpMethod method, string url, string? body = null, string? apiKey = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        if (apiKey is not null)
        {
            request.Headers.Add("X-Api-Key", apiKey);
        }

        return request;
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <summary>Creates a user via the admin key and returns the one-time raw API key.</summary>
    private async Task<string> CreateUserAsync(HttpClient client, bool isAdmin = false)
    {
        var body = $"{{\"name\":\"u\",\"isAdmin\":{(isAdmin ? "true" : "false")}}}";
        var response = await client.SendAsync(Req(HttpMethod.Post, "/api/users", body, AdminKey));
        response.EnsureSuccessStatusCode();
        return (await BodyAsync(response)).GetProperty("apiKey").GetString()!;
    }

    [Fact]
    public async Task Health_Is_Anonymous()
    {
        var response = await _factory.CreateClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Dispatch_Without_Key_Is_Unauthorized()
    {
        var response = await _factory.CreateClient().SendAsync(
            Req(HttpMethod.Post, "/api/dispatches", "{\"mode\":\"Sequential\",\"steps\":[{\"targetKey\":\"wallpaper\"}]}"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Dispatch_With_Bad_Key_Is_Unauthorized()
    {
        var response = await _factory.CreateClient().SendAsync(
            Req(HttpMethod.Post, "/api/dispatches", "{\"mode\":\"Sequential\",\"steps\":[{\"targetKey\":\"wallpaper\"}]}", "not-a-real-key"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Listing_Users_Without_Key_Is_Unauthorized()
    {
        var response = await _factory.CreateClient().GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_User_Returns_Key_Once_And_List_Never_Exposes_It()
    {
        var client = _factory.CreateClient();

        var create = await client.SendAsync(Req(HttpMethod.Post, "/api/users", "{\"name\":\"alice\"}", AdminKey));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await BodyAsync(create);
        Assert.False(string.IsNullOrWhiteSpace(created.GetProperty("apiKey").GetString()));

        var listText = await (await client.SendAsync(Req(HttpMethod.Get, "/api/users", apiKey: AdminKey)))
            .Content.ReadAsStringAsync();
        Assert.DoesNotContain("apiKey", listText); // the list representation never carries a key
    }

    [Fact]
    public async Task NonAdmin_Cannot_Manage_But_Can_Dispatch()
    {
        var client = _factory.CreateClient();
        var userKey = await CreateUserAsync(client, isAdmin: false);

        var manage = await client.SendAsync(Req(HttpMethod.Get, "/api/users", apiKey: userKey));
        Assert.Equal(HttpStatusCode.Forbidden, manage.StatusCode);

        // The seeded demo user is admin with an allow-all rule, but a brand-new user has no
        // rules, so its dispatch is accepted (201) yet every step is denied by default.
        var dispatch = await client.SendAsync(Req(HttpMethod.Post, "/api/dispatches",
            "{\"mode\":\"Sequential\",\"steps\":[{\"targetKey\":\"wallpaper\",\"parameters\":{}}]}", userKey));
        Assert.Equal(HttpStatusCode.Created, dispatch.StatusCode);

        var body = await BodyAsync(dispatch);
        Assert.Equal("Denied", body.GetProperty("status").GetString());
        Assert.Equal("Denied", body.GetProperty("steps")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Unknown_Target_Fails_The_Step()
    {
        var response = await _factory.CreateClient().SendAsync(Req(HttpMethod.Post, "/api/dispatches",
            "{\"mode\":\"Sequential\",\"steps\":[{\"targetKey\":\"does-not-exist\"}]}", AdminKey));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var step = (await BodyAsync(response)).GetProperty("steps")[0];
        Assert.Equal("Failed", step.GetProperty("status").GetString());
        Assert.Contains("not found", step.GetProperty("error").GetString()!);
    }

    [Fact]
    public async Task Wallpaper_Action_Blocks_Ssrf_To_Metadata_Endpoint()
    {
        var response = await _factory.CreateClient().SendAsync(Req(HttpMethod.Post, "/api/dispatches",
            "{\"mode\":\"Sequential\",\"steps\":[{\"targetKey\":\"wallpaper\",\"parameters\":{\"imageUrl\":\"http://169.254.169.254/latest/meta-data/\"}}]}",
            AdminKey));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var step = (await BodyAsync(response)).GetProperty("steps")[0];
        Assert.Equal("Failed", step.GetProperty("status").GetString());
        Assert.Contains("rejected", step.GetProperty("error").GetString()!);
    }

    [Fact]
    public async Task Dispatch_Is_Not_Readable_By_Another_User()
    {
        var client = _factory.CreateClient();

        // Admin creates a dispatch it owns.
        var created = await client.SendAsync(Req(HttpMethod.Post, "/api/dispatches",
            "{\"mode\":\"Sequential\",\"steps\":[{\"targetKey\":\"does-not-exist\"}]}", AdminKey));
        var id = (await BodyAsync(created)).GetProperty("id").GetInt32();

        // A different user gets 404 (not 403) for it — ids can't be enumerated.
        var otherKey = await CreateUserAsync(client);
        var read = await client.SendAsync(Req(HttpMethod.Get, $"/api/dispatches/{id}", apiKey: otherKey));
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
    }

    [Fact]
    public async Task Dispatch_With_Too_Many_Steps_Is_Rejected()
    {
        var steps = string.Join(",", Enumerable.Repeat("{\"targetKey\":\"wallpaper\",\"parameters\":{}}", 26));
        var response = await _factory.CreateClient().SendAsync(Req(HttpMethod.Post, "/api/dispatches",
            $"{{\"mode\":\"Sequential\",\"steps\":[{steps}]}}", AdminKey));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Dispatch_With_No_Steps_Is_Rejected()
    {
        var response = await _factory.CreateClient().SendAsync(Req(HttpMethod.Post, "/api/dispatches",
            "{\"mode\":\"Sequential\",\"steps\":[]}", AdminKey));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Duplicate_Target_Key_Is_Conflict()
    {
        // "httpbin" is seeded, so re-creating it must 409.
        var response = await _factory.CreateClient().SendAsync(Req(HttpMethod.Post, "/api/targets",
            "{\"key\":\"httpbin\",\"name\":\"dup\",\"kind\":\"Http\",\"baseUrl\":\"https://example.com\"}", AdminKey));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Deactivated_User_Key_No_Longer_Authenticates()
    {
        var client = _factory.CreateClient();

        var create = await client.SendAsync(Req(HttpMethod.Post, "/api/users", "{\"name\":\"temp\"}", AdminKey));
        var created = await BodyAsync(create);
        var id = created.GetProperty("id").GetInt32();
        var key = created.GetProperty("apiKey").GetString()!;

        // Key works before deactivation.
        var before = await client.SendAsync(Req(HttpMethod.Post, "/api/dispatches",
            "{\"mode\":\"Sequential\",\"steps\":[{\"targetKey\":\"wallpaper\",\"parameters\":{}}]}", key));
        Assert.Equal(HttpStatusCode.Created, before.StatusCode);

        // Admin deactivates the user via PATCH.
        var patch = await client.SendAsync(Req(HttpMethod.Patch, $"/api/users/{id}", "{\"isActive\":false}", AdminKey));
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

        // The key is now rejected.
        var after = await client.SendAsync(Req(HttpMethod.Post, "/api/dispatches",
            "{\"mode\":\"Sequential\",\"steps\":[{\"targetKey\":\"wallpaper\",\"parameters\":{}}]}", key));
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task Schedule_Can_Be_Created_And_Listed()
    {
        var client = _factory.CreateClient();
        var key = await CreateUserAsync(client);

        var body = "{\"name\":\"nightly\",\"intervalSeconds\":60,\"isActive\":false," +
                   "\"dispatch\":{\"mode\":\"Sequential\",\"steps\":[{\"targetKey\":\"wallpaper\",\"parameters\":{}}]}}";
        var create = await client.SendAsync(Req(HttpMethod.Post, "/api/schedules", body, key));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var list = await BodyAsync(await client.SendAsync(Req(HttpMethod.Get, "/api/schedules", apiKey: key)));
        Assert.Equal(1, list.GetArrayLength());
        Assert.Equal("nightly", list[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Schedule_Patch_With_Too_Many_Steps_Is_Rejected()
    {
        var client = _factory.CreateClient();
        var key = await CreateUserAsync(client);

        var body = "{\"name\":\"n\",\"intervalSeconds\":60,\"isActive\":false," +
                   "\"dispatch\":{\"mode\":\"Sequential\",\"steps\":[{\"targetKey\":\"wallpaper\",\"parameters\":{}}]}}";
        var id = (await BodyAsync(await client.SendAsync(Req(HttpMethod.Post, "/api/schedules", body, key))))
            .GetProperty("id").GetInt32();

        // The step cap must also apply on PATCH, not just POST.
        var steps = string.Join(",", Enumerable.Repeat("{\"targetKey\":\"wallpaper\",\"parameters\":{}}", 26));
        var patchBody = $"{{\"dispatch\":{{\"mode\":\"Sequential\",\"steps\":[{steps}]}}}}";
        var patch = await client.SendAsync(Req(HttpMethod.Patch, $"/api/schedules/{id}", patchBody, key));
        Assert.Equal(HttpStatusCode.BadRequest, patch.StatusCode);
    }
}
