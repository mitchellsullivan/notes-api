namespace NotesService.Tests;

public sealed class AuthTests : IClassFixture<NotesApiFactory>
{
    private readonly NotesApiFactory factory;

    public AuthTests(NotesApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task Health_endpoint_is_anonymous()
    {
        var response = await factory.CreateClient().GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = await ApiClient.ReadJsonAsync(response);
        Assert.Equal("ok", json.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Missing_token_yields_401_with_www_authenticate()
    {
        var response = await factory.CreateClient().GetAsync("/v1/me");
        await ApiClient.AssertErrorAsync(response, 401, "unauthorized");
        Assert.Contains(response.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");
    }

    [Fact]
    public async Task Invalid_token_yields_401()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "Authorization", "Bearer " + new string('a', 64));
        var response = await client.GetAsync("/v1/me");
        await ApiClient.AssertErrorAsync(response, 401, "unauthorized");
    }

    [Fact]
    public async Task Scheme_name_is_case_insensitive()
    {
        var anonymous = factory.CreateClient();
        var (_, token) = await anonymous.CreateUserAsync("case-insensitive");

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"bearer {token}");
        var response = await client.GetAsync("/v1/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Created_user_can_fetch_itself()
    {
        var (client, userId) = await factory.CreateAuthedClientAsync("alice");
        var response = await client.GetAsync("/v1/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = await ApiClient.ReadJsonAsync(response);
        Assert.Equal(userId, json.RootElement.GetProperty("id").GetString());
        Assert.Equal("alice", json.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task User_name_is_validated()
    {
        var client = factory.CreateClient();

        var empty = await client.PostAsJsonAsync("/v1/users", new { name = "   " });
        await ApiClient.AssertErrorAsync(empty, 422, "validation_error");

        var tooLong = await client.PostAsJsonAsync("/v1/users", new { name = new string('x', 101) });
        await ApiClient.AssertErrorAsync(tooLong, 422, "validation_error");
    }

    [Fact]
    public async Task Unknown_json_fields_are_rejected()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/v1/users", new { name = "bob", role = "admin" });
        await ApiClient.AssertErrorAsync(response, 400, "invalid_request");
    }
}
