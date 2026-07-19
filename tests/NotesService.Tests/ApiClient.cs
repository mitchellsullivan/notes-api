using System.Net.Http.Headers;

namespace NotesService.Tests;

public static class ApiClient
{
    public static async Task<(string Id, string Token)> CreateUserAsync(
        this HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/v1/users", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ReadJsonAsync(response);
        return (
            json.RootElement.GetProperty("user").GetProperty("id").GetString()!,
            json.RootElement.GetProperty("token").GetString()!);
    }

    /// <summary>Creates a fresh user and returns a client authenticated as them.</summary>
    public static async Task<(HttpClient Client, string UserId)> CreateAuthedClientAsync(
        this NotesApiFactory factory, string name = "user")
    {
        var client = factory.CreateClient();
        var (id, token) = await client.CreateUserAsync(name);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return (client, id);
    }

    public static async Task<string> CreateNoteAsync(
        this HttpClient client, string title = "title", string body = "body")
    {
        var response = await client.PostAsJsonAsync("/v1/notes", new { title, body });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ReadJsonAsync(response);
        return json.RootElement.GetProperty("id").GetString()!;
    }

    /// <summary>Builds a request manually — PATCH-with-JSON and If-Match aren't
    /// covered by the built-in convenience extensions on .NET 6.</summary>
    public static Task<HttpResponseMessage> SendJsonAsync(
        this HttpClient client,
        HttpMethod method,
        string url,
        object? payload = null,
        string? ifMatch = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload);
        }

        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return client.SendAsync(request);
    }

    public static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    /// <summary>Asserts the status code and the error envelope's machine-readable code.</summary>
    public static async Task AssertErrorAsync(HttpResponseMessage response, int status, string code)
    {
        Assert.Equal(status, (int)response.StatusCode);
        using var json = await ReadJsonAsync(response);
        Assert.Equal(code, json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    public static string? Etag(this HttpResponseMessage response) =>
        response.Headers.ETag?.Tag;
}
