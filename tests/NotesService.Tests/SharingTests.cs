namespace NotesService.Tests;

public sealed class SharingTests : IClassFixture<NotesApiFactory>
{
    private readonly NotesApiFactory factory;

    public SharingTests(NotesApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task Read_share_allows_get_but_not_patch()
    {
        var (owner, _) = await factory.CreateAuthedClientAsync("owner");
        var noteId = await owner.CreateNoteAsync();
        var (reader, readerId) = await factory.CreateAuthedClientAsync("reader");

        var share = await owner.PostAsJsonAsync(
            $"/v1/notes/{noteId}/shares", new { user_id = readerId, permission = "read" });
        Assert.Equal(HttpStatusCode.Created, share.StatusCode);

        var get = await reader.GetAsync($"/v1/notes/{noteId}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        using (var json = await ApiClient.ReadJsonAsync(get))
        {
            Assert.Equal("read", json.RootElement.GetProperty("my_permission").GetString());
        }

        var patch = await reader.SendJsonAsync(
            HttpMethod.Patch, $"/v1/notes/{noteId}", new { body = "nope" }, ifMatch: "\"1\"");
        await ApiClient.AssertErrorAsync(patch, 403, "forbidden");
    }

    [Fact]
    public async Task Edit_share_allows_patch()
    {
        var (owner, _) = await factory.CreateAuthedClientAsync("owner");
        var noteId = await owner.CreateNoteAsync();
        var (editor, editorId) = await factory.CreateAuthedClientAsync("editor");

        await owner.PostAsJsonAsync(
            $"/v1/notes/{noteId}/shares", new { user_id = editorId, permission = "edit" });

        var patch = await editor.SendJsonAsync(
            HttpMethod.Patch, $"/v1/notes/{noteId}", new { body = "edited" }, ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
    }

    [Fact]
    public async Task Resharing_updates_permission_and_returns_200()
    {
        var (owner, _) = await factory.CreateAuthedClientAsync("owner");
        var noteId = await owner.CreateNoteAsync();
        var (target, targetId) = await factory.CreateAuthedClientAsync("target");

        var first = await owner.PostAsJsonAsync(
            $"/v1/notes/{noteId}/shares", new { user_id = targetId, permission = "read" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // Same grant with a new permission is an update, not a creation.
        var second = await owner.PostAsJsonAsync(
            $"/v1/notes/{noteId}/shares", new { user_id = targetId, permission = "edit" });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var get = await target.GetAsync($"/v1/notes/{noteId}");
        using var json = await ApiClient.ReadJsonAsync(get);
        Assert.Equal("edit", json.RootElement.GetProperty("my_permission").GetString());
    }

    [Fact]
    public async Task Share_requests_are_validated()
    {
        var (owner, ownerId) = await factory.CreateAuthedClientAsync("owner");
        var noteId = await owner.CreateNoteAsync();
        var (_, otherId) = await factory.CreateAuthedClientAsync("other");

        // Exactly one of user_id / team_id.
        await ApiClient.AssertErrorAsync(
            await owner.PostAsJsonAsync($"/v1/notes/{noteId}/shares",
                new { user_id = otherId, team_id = "t", permission = "read" }),
            422, "validation_error");
        await ApiClient.AssertErrorAsync(
            await owner.PostAsJsonAsync($"/v1/notes/{noteId}/shares",
                new { permission = "read" }),
            422, "validation_error");

        // Permission vocabulary is closed.
        await ApiClient.AssertErrorAsync(
            await owner.PostAsJsonAsync($"/v1/notes/{noteId}/shares",
                new { user_id = otherId, permission = "admin" }),
            422, "validation_error");

        // No self-shares.
        await ApiClient.AssertErrorAsync(
            await owner.PostAsJsonAsync($"/v1/notes/{noteId}/shares",
                new { user_id = ownerId, permission = "read" }),
            422, "validation_error");

        // Unknown target user.
        await ApiClient.AssertErrorAsync(
            await owner.PostAsJsonAsync($"/v1/notes/{noteId}/shares",
                new { user_id = "does-not-exist", permission = "read" }),
            404, "not_found");
    }

    [Fact]
    public async Task Only_the_owner_can_manage_shares()
    {
        var (owner, _) = await factory.CreateAuthedClientAsync("owner");
        var noteId = await owner.CreateNoteAsync();
        var (reader, readerId) = await factory.CreateAuthedClientAsync("reader");
        var (stranger, strangerId) = await factory.CreateAuthedClientAsync("stranger");

        await owner.PostAsJsonAsync(
            $"/v1/notes/{noteId}/shares", new { user_id = readerId, permission = "read" });

        // Reader can see the note, so managing shares is a 403.
        await ApiClient.AssertErrorAsync(
            await reader.GetAsync($"/v1/notes/{noteId}/shares"), 403, "forbidden");
        await ApiClient.AssertErrorAsync(
            await reader.PostAsJsonAsync($"/v1/notes/{noteId}/shares",
                new { user_id = strangerId, permission = "read" }),
            403, "forbidden");

        // Stranger must not learn the note exists at all.
        await ApiClient.AssertErrorAsync(
            await stranger.GetAsync($"/v1/notes/{noteId}/shares"), 404, "not_found");
    }

    [Fact]
    public async Task Unshare_revokes_access()
    {
        var (owner, _) = await factory.CreateAuthedClientAsync("owner");
        var noteId = await owner.CreateNoteAsync();
        var (reader, readerId) = await factory.CreateAuthedClientAsync("reader");

        await owner.PostAsJsonAsync(
            $"/v1/notes/{noteId}/shares", new { user_id = readerId, permission = "read" });
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync($"/v1/notes/{noteId}")).StatusCode);

        var unshare = await owner.SendJsonAsync(
            HttpMethod.Delete, $"/v1/notes/{noteId}/shares", new { user_id = readerId });
        Assert.Equal(HttpStatusCode.NoContent, unshare.StatusCode);

        await ApiClient.AssertErrorAsync(
            await reader.GetAsync($"/v1/notes/{noteId}"), 404, "not_found");

        // Revoking a grant that no longer exists is a 404.
        await ApiClient.AssertErrorAsync(
            await owner.SendJsonAsync(
                HttpMethod.Delete, $"/v1/notes/{noteId}/shares", new { user_id = readerId }),
            404, "not_found");
    }

    [Fact]
    public async Task Share_listing_shows_user_and_team_grants()
    {
        var (owner, _) = await factory.CreateAuthedClientAsync("owner");
        var noteId = await owner.CreateNoteAsync();
        var (_, readerId) = await factory.CreateAuthedClientAsync("reader");
        var teamId = await owner.CreateTeamAsync();

        await owner.PostAsJsonAsync(
            $"/v1/notes/{noteId}/shares", new { user_id = readerId, permission = "read" });
        await owner.PostAsJsonAsync(
            $"/v1/notes/{noteId}/shares", new { team_id = teamId, permission = "edit" });

        var response = await owner.GetAsync($"/v1/notes/{noteId}/shares");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = await ApiClient.ReadJsonAsync(response);
        var shares = json.RootElement.EnumerateArray().ToArray();
        Assert.Equal(2, shares.Length);
        Assert.Contains(shares, s =>
            s.TryGetProperty("user_id", out var id) && id.GetString() == readerId);
        Assert.Contains(shares, s =>
            s.TryGetProperty("team_id", out var id) && id.GetString() == teamId);
    }
}
