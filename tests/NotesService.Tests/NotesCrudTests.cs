namespace NotesService.Tests;

public sealed class NotesCrudTests : IClassFixture<NotesApiFactory>
{
    private readonly NotesApiFactory factory;

    public NotesCrudTests(NotesApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task Create_returns_note_with_etag_version_1()
    {
        var (client, userId) = await factory.CreateAuthedClientAsync();
        var response = await client.PostAsJsonAsync(
            "/v1/notes", new { title = "  standup  ", body = "blockers: none" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("\"1\"", response.Etag());

        using var json = await ApiClient.ReadJsonAsync(response);
        var root = json.RootElement;
        Assert.Equal("standup", root.GetProperty("title").GetString()); // trimmed
        Assert.Equal("blockers: none", root.GetProperty("body").GetString());
        Assert.Equal(userId, root.GetProperty("ownerId").GetString());
        Assert.Equal(1, root.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task Create_validates_title_and_body()
    {
        var (client, _) = await factory.CreateAuthedClientAsync();

        var emptyTitle = await client.PostAsJsonAsync("/v1/notes", new { title = " ", body = "b" });
        Assert.Equal(HttpStatusCode.BadRequest, emptyTitle.StatusCode);

        var longTitle = await client.PostAsJsonAsync(
            "/v1/notes", new { title = new string('t', 201), body = "b" });
        Assert.Equal(HttpStatusCode.BadRequest, longTitle.StatusCode);
    }

    [Fact]
    public async Task Get_returns_the_note()
    {
        var (client, _) = await factory.CreateAuthedClientAsync();
        var noteId = await client.CreateNoteAsync(title: "fetch me");

        var response = await client.GetAsync($"/v1/notes/{noteId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("\"1\"", response.Etag());

        using var json = await ApiClient.ReadJsonAsync(response);
        Assert.Equal("fetch me", json.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Unshared_note_is_invisible_to_other_users()
    {
        var (owner, _) = await factory.CreateAuthedClientAsync("owner");
        var noteId = await owner.CreateNoteAsync();

        var (stranger, _) = await factory.CreateAuthedClientAsync("stranger");
        // 404, not 403: the API must not confirm the note exists.
        var response = await stranger.GetAsync($"/v1/notes/{noteId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Patch_requires_if_match()
    {
        var (client, _) = await factory.CreateAuthedClientAsync();
        var noteId = await client.CreateNoteAsync();

        var noHeader = await client.SendJsonAsync(
            HttpMethod.Patch, $"/v1/notes/{noteId}", new { body = "x" });
        Assert.Equal((HttpStatusCode)428, noHeader.StatusCode);

        var unquoted = await client.SendJsonAsync(
            HttpMethod.Patch, $"/v1/notes/{noteId}", new { body = "x" }, ifMatch: "1");
        Assert.Equal((HttpStatusCode)428, unquoted.StatusCode);
    }

    [Fact]
    public async Task Patch_with_current_version_succeeds_and_bumps_etag()
    {
        var (client, _) = await factory.CreateAuthedClientAsync();
        var noteId = await client.CreateNoteAsync(body: "v1 body");

        var response = await client.SendJsonAsync(
            HttpMethod.Patch, $"/v1/notes/{noteId}", new { body = "v2 body" }, ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("\"2\"", response.Etag());

        using var json = await ApiClient.ReadJsonAsync(response);
        Assert.Equal("v2 body", json.RootElement.GetProperty("body").GetString());
        Assert.Equal(2, json.RootElement.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task Patch_with_stale_version_is_a_conflict()
    {
        var (client, _) = await factory.CreateAuthedClientAsync();
        var noteId = await client.CreateNoteAsync();

        var first = await client.SendJsonAsync(
            HttpMethod.Patch, $"/v1/notes/{noteId}", new { body = "second" }, ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // A writer still holding version 1 must not clobber the update above.
        var stale = await client.SendJsonAsync(
            HttpMethod.Patch, $"/v1/notes/{noteId}", new { body = "lost update" }, ifMatch: "\"1\"");
        Assert.Equal((HttpStatusCode)412, stale.StatusCode);
    }

    [Fact]
    public async Task Patch_with_no_fields_is_rejected()
    {
        var (client, _) = await factory.CreateAuthedClientAsync();
        var noteId = await client.CreateNoteAsync();

        var response = await client.SendJsonAsync(
            HttpMethod.Patch, $"/v1/notes/{noteId}", new { }, ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Delete_requires_if_match_and_removes_the_note()
    {
        var (client, _) = await factory.CreateAuthedClientAsync();
        var noteId = await client.CreateNoteAsync();

        var noHeader = await client.SendJsonAsync(HttpMethod.Delete, $"/v1/notes/{noteId}");
        Assert.Equal((HttpStatusCode)428, noHeader.StatusCode);

        var stale = await client.SendJsonAsync(
            HttpMethod.Delete, $"/v1/notes/{noteId}", ifMatch: "\"99\"");
        Assert.Equal((HttpStatusCode)412, stale.StatusCode);

        var deleted = await client.SendJsonAsync(
            HttpMethod.Delete, $"/v1/notes/{noteId}", ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var gone = await client.GetAsync($"/v1/notes/{noteId}");
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }
}
