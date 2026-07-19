namespace NotesService.Tests;

public sealed class NotesCrudTests : IClassFixture<NotesApiFactory>
{
    private readonly NotesApiFactory factory;

    public NotesCrudTests(NotesApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task Create_trims_and_returns_the_note()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/v1/notes", new { title = "  standup  ", body = "blockers: none" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var json = await ApiClient.ReadJsonAsync(response);
        var root = json.RootElement;
        Assert.Equal("standup", root.GetProperty("title").GetString()); // trimmed
        Assert.Equal("blockers: none", root.GetProperty("body").GetString());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task Create_validates_title_and_body()
    {
        var client = factory.CreateClient();

        var emptyTitle = await client.PostAsJsonAsync("/v1/notes", new { title = " ", body = "b" });
        Assert.Equal(HttpStatusCode.BadRequest, emptyTitle.StatusCode);

        var longTitle = await client.PostAsJsonAsync(
            "/v1/notes", new { title = new string('t', 201), body = "b" });
        Assert.Equal(HttpStatusCode.BadRequest, longTitle.StatusCode);
    }

    [Fact]
    public async Task Get_returns_the_note()
    {
        var client = factory.CreateClient();
        var noteId = await client.CreateNoteAsync(title: "fetch me");

        var response = await client.GetAsync($"/v1/notes/{noteId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = await ApiClient.ReadJsonAsync(response);
        Assert.Equal("fetch me", json.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Delete_removes_the_note()
    {
        var client = factory.CreateClient();
        var noteId = await client.CreateNoteAsync();

        var deleted = await client.DeleteAsync($"/v1/notes/{noteId}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var gone = await client.GetAsync($"/v1/notes/{noteId}");
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }
}
