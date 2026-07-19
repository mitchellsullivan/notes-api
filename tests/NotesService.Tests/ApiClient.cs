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

    public static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());
}
