namespace NotesService.Tests;

public static class ApiClient
{
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
