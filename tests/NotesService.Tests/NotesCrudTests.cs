using System.Text;

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
        Assert.Equal(userId, root.GetProperty("owner_id").GetString());
        Assert.Equal(1, root.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task Create_validates_title_and_body()
    {
        var (client, _) = await factory.CreateAuthedClientAsync();

        var emptyTitle = await client.PostAsJsonAsync("/v1/notes", new { title = " ", body = "b" });
        await ApiClient.AssertErrorAsync(emptyTitle, 422, "validation_error");

        var longTitle = await client.PostAsJsonAsync(
            "/v1/notes", new { title = new string('t', 201), body = "b" });
        await ApiClient.AssertErrorAsync(longTitle, 422, "validation_error");

        var unknownField = await client.PostAsJsonAsync(
            "/v1/notes", new { title = "t", body = "b", pinned = true });
        await ApiClient.AssertErrorAsync(unknownField, 400, "invalid_request");
    }

    [Fact]
    public async Task Malformed_json_body_is_a_400()
    {
        var (client, _) = await factory.CreateAuthedClientAsync();

        var malformed = await client.PostAsync(
            "/v1/notes",
            new StringContent("{not json", Encoding.UTF8, "application/json"));
        await ApiClient.AssertErrorAsync(malformed, 400, "invalid_request");

        // An empty-but-present JSON body is also malformed, not "no fields
        // to update" — that distinction only applies to PATCH.
        var empty = await client.PostAsync(
            "/v1/notes",
            new StringContent(string.Empty, Encoding.UTF8, "application/json"));
        await ApiClient.AssertErrorAsync(empty, 400, "invalid_request");
    }

    [Fact]
    public async Task Missing_content_type_is_415_not_our_error_envelope()
    {
        // No Content-Type at all means ASP.NET can't select an input
        // formatter, so this is rejected before model binding runs —
        // before RejectUnknownJsonFieldsFilter or the custom
        // InvalidModelStateResponseFactory ever see the request. It is
        // therefore a bare framework 415, not our JSON error envelope.
        // This pins that as known, intentional behavior rather than a gap.
        var (client, _) = await factory.CreateAuthedClientAsync();
        var response = await client.PostAsync("/v1/notes", content: null);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task Get_own_note_reports_edit_permission()
    {
        var (client, _) = await factory.CreateAuthedClientAsync();
        var noteId = await client.CreateNoteAsync();

        var response = await client.GetAsync($"/v1/notes/{noteId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("\"1\"", response.Etag());

        using var json = await ApiClient.ReadJsonAsync(response);
        Assert.Equal("edit", json.RootElement.GetProperty("my_permission").GetString());
        Assert.Equal(noteId, json.RootElement.GetProperty("note").GetProperty("id").GetString());
    }

    [Fact]
    public async Task Unshared_note_is_invisible_to_other_users()
    {
        var (owner, _) = await factory.CreateAuthedClientAsync("owner");
        var noteId = await owner.CreateNoteAsync();

        var (stranger, _) = await factory.CreateAuthedClientAsync("stranger");
        // 404, not 403: the API must not confirm the note exists.
        var response = await stranger.GetAsync($"/v1/notes/{noteId}");
        await ApiClient.AssertErrorAsync(response, 404, "not_found");
    }

    [Fact]
    public async Task Patch_requires_if_match()
    {
        var (client, _) = await factory.CreateAuthedClientAsync();
        var noteId = await client.CreateNoteAsync();

        var noHeader = await client.SendJsonAsync(
            HttpMethod.Patch, $"/v1/notes/{noteId}", new { body = "x" });
        await ApiClient.AssertErrorAsync(noHeader, 428, "precondition_required");

        var unquoted = await client.SendJsonAsync(
            HttpMethod.Patch, $"/v1/notes/{noteId}", new { body = "x" }, ifMatch: "1");
        await ApiClient.AssertErrorAsync(unquoted, 428, "precondition_required");
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
        await ApiClient.AssertErrorAsync(stale, 412, "version_conflict");
    }

    [Fact]
    public async Task Patch_with_no_fields_is_rejected()
    {
        var (client, _) = await factory.CreateAuthedClientAsync();
        var noteId = await client.CreateNoteAsync();

        var response = await client.SendJsonAsync(
            HttpMethod.Patch, $"/v1/notes/{noteId}", new { }, ifMatch: "\"1\"");
        await ApiClient.AssertErrorAsync(response, 422, "validation_error");
    }

    [Fact]
    public async Task Delete_requires_if_match_and_removes_the_note()
    {
        var (client, _) = await factory.CreateAuthedClientAsync();
        var noteId = await client.CreateNoteAsync();

        var noHeader = await client.SendJsonAsync(HttpMethod.Delete, $"/v1/notes/{noteId}");
        await ApiClient.AssertErrorAsync(noHeader, 428, "precondition_required");

        var stale = await client.SendJsonAsync(
            HttpMethod.Delete, $"/v1/notes/{noteId}", ifMatch: "\"99\"");
        await ApiClient.AssertErrorAsync(stale, 412, "version_conflict");

        var deleted = await client.SendJsonAsync(
            HttpMethod.Delete, $"/v1/notes/{noteId}", ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var gone = await client.GetAsync($"/v1/notes/{noteId}");
        await ApiClient.AssertErrorAsync(gone, 404, "not_found");
    }

    [Fact]
    public async Task Only_the_owner_can_delete_even_with_edit_share()
    {
        var (owner, _) = await factory.CreateAuthedClientAsync("owner");
        var noteId = await owner.CreateNoteAsync();

        var (editor, editorId) = await factory.CreateAuthedClientAsync("editor");
        var share = await owner.PostAsJsonAsync(
            $"/v1/notes/{noteId}/shares", new { user_id = editorId, permission = "edit" });
        Assert.Equal(HttpStatusCode.Created, share.StatusCode);

        // Editor can see the note, so this is a 403, not a 404.
        var response = await editor.SendJsonAsync(
            HttpMethod.Delete, $"/v1/notes/{noteId}", ifMatch: "\"1\"");
        await ApiClient.AssertErrorAsync(response, 403, "forbidden");
    }
}
