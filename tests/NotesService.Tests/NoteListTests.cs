namespace NotesService.Tests;

public sealed class NoteListTests : IClassFixture<NotesApiFactory>
{
    private readonly NotesApiFactory factory;

    public NoteListTests(NotesApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task List_paginates_with_a_stable_total_count()
    {
        // Lists are per-user now, so a fresh user is a clean slate — the
        // unique-marker trick from before auth is no longer needed.
        var (client, _) = await factory.CreateAuthedClientAsync();
        for (var i = 0; i < 3; i++)
        {
            await client.CreateNoteAsync(title: $"note {i}");
        }

        var firstPage = await client.GetAsync("/v1/notes?limit=2&offset=0");
        Assert.Equal(HttpStatusCode.OK, firstPage.StatusCode);
        using (var json = await ApiClient.ReadJsonAsync(firstPage))
        {
            Assert.Equal(3, json.RootElement.GetProperty("count").GetInt32());
            Assert.Equal(2, json.RootElement.GetProperty("items").GetArrayLength());
        }

        var secondPage = await client.GetAsync("/v1/notes?limit=2&offset=2");
        using (var json = await ApiClient.ReadJsonAsync(secondPage))
        {
            Assert.Equal(3, json.RootElement.GetProperty("count").GetInt32());
            Assert.Equal(1, json.RootElement.GetProperty("items").GetArrayLength());
        }
    }

    [Fact]
    public async Task Search_matches_title_and_body_case_insensitively()
    {
        var (client, _) = await factory.CreateAuthedClientAsync();
        await client.CreateNoteAsync(title: "Grocery list", body: "eggs, flour");
        await client.CreateNoteAsync(title: "Standup", body: "need GROCERIES after work");
        await client.CreateNoteAsync(title: "Unrelated", body: "nothing here");

        var response = await client.GetAsync("/v1/notes?q=groc");
        using var json = await ApiClient.ReadJsonAsync(response);
        Assert.Equal(2, json.RootElement.GetProperty("count").GetInt32());
    }
}
