namespace NotesService.Tests;

public sealed class TeamsTests : IClassFixture<NotesApiFactory>
{
    private readonly NotesApiFactory factory;

    public TeamsTests(NotesApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task Creator_becomes_owner_and_first_member()
    {
        var (client, userId) = await factory.CreateAuthedClientAsync("founder");
        var response = await client.PostAsJsonAsync("/v1/teams", new { name = "platform" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var json = await ApiClient.ReadJsonAsync(response);
        Assert.Equal(userId, json.RootElement.GetProperty("owner_id").GetString());
        var members = json.RootElement.GetProperty("member_ids").EnumerateArray()
            .Select(m => m.GetString())
            .ToArray();
        Assert.Equal(new[] { userId }, members);
    }

    [Fact]
    public async Task Teams_are_invisible_to_non_members()
    {
        var (owner, _) = await factory.CreateAuthedClientAsync("owner");
        var teamId = await owner.CreateTeamAsync();

        var (outsider, _) = await factory.CreateAuthedClientAsync("outsider");
        await ApiClient.AssertErrorAsync(
            await outsider.GetAsync($"/v1/teams/{teamId}"), 404, "not_found");
    }

    [Fact]
    public async Task Only_the_owner_adds_members_and_duplicates_conflict()
    {
        var (owner, _) = await factory.CreateAuthedClientAsync("owner");
        var teamId = await owner.CreateTeamAsync();
        var (member, memberId) = await factory.CreateAuthedClientAsync("member");
        var (_, thirdId) = await factory.CreateAuthedClientAsync("third");

        var add = await owner.PostAsJsonAsync(
            $"/v1/teams/{teamId}/members", new { user_id = memberId });
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);

        // A plain member may not grow the team.
        await ApiClient.AssertErrorAsync(
            await member.PostAsJsonAsync($"/v1/teams/{teamId}/members", new { user_id = thirdId }),
            403, "forbidden");

        // Adding the same member twice conflicts.
        await ApiClient.AssertErrorAsync(
            await owner.PostAsJsonAsync($"/v1/teams/{teamId}/members", new { user_id = memberId }),
            409, "already_exists");

        // Unknown users cannot be added.
        await ApiClient.AssertErrorAsync(
            await owner.PostAsJsonAsync($"/v1/teams/{teamId}/members", new { user_id = "ghost" }),
            404, "not_found");
    }

    [Fact]
    public async Task Team_share_grants_access_to_members_only()
    {
        var (owner, _) = await factory.CreateAuthedClientAsync("owner");
        var (member, memberId) = await factory.CreateAuthedClientAsync("member");
        var (outsider, _) = await factory.CreateAuthedClientAsync("outsider");

        var teamId = await owner.CreateTeamAsync();
        await owner.PostAsJsonAsync($"/v1/teams/{teamId}/members", new { user_id = memberId });

        var noteId = await owner.CreateNoteAsync();
        var share = await owner.PostAsJsonAsync(
            $"/v1/notes/{noteId}/shares", new { team_id = teamId, permission = "read" });
        Assert.Equal(HttpStatusCode.Created, share.StatusCode);

        var memberGet = await member.GetAsync($"/v1/notes/{noteId}");
        Assert.Equal(HttpStatusCode.OK, memberGet.StatusCode);

        await ApiClient.AssertErrorAsync(
            await outsider.GetAsync($"/v1/notes/{noteId}"), 404, "not_found");
    }

    [Fact]
    public async Task Cannot_share_to_a_team_the_caller_does_not_belong_to()
    {
        var (owner, _) = await factory.CreateAuthedClientAsync("owner");
        var noteId = await owner.CreateNoteAsync();

        var (other, _) = await factory.CreateAuthedClientAsync("other");
        var foreignTeamId = await other.CreateTeamAsync();

        // A leaked team ID must not let an outsider inject notes into it.
        await ApiClient.AssertErrorAsync(
            await owner.PostAsJsonAsync(
                $"/v1/notes/{noteId}/shares", new { team_id = foreignTeamId, permission = "read" }),
            404, "not_found");
    }

    [Fact]
    public async Task Effective_permission_is_the_maximum_across_grants()
    {
        var (owner, _) = await factory.CreateAuthedClientAsync("owner");
        var (member, memberId) = await factory.CreateAuthedClientAsync("member");

        var teamId = await owner.CreateTeamAsync();
        await owner.PostAsJsonAsync($"/v1/teams/{teamId}/members", new { user_id = memberId });

        var noteId = await owner.CreateNoteAsync();
        // Direct grant says read; team grant says edit. Edit must win.
        await owner.PostAsJsonAsync(
            $"/v1/notes/{noteId}/shares", new { user_id = memberId, permission = "read" });
        await owner.PostAsJsonAsync(
            $"/v1/notes/{noteId}/shares", new { team_id = teamId, permission = "edit" });

        var get = await member.GetAsync($"/v1/notes/{noteId}");
        using var json = await ApiClient.ReadJsonAsync(get);
        Assert.Equal("edit", json.RootElement.GetProperty("my_permission").GetString());
    }
        
    [Fact]
    public async Task Owner_removes_member_and_access_is_revoked()
    {
        var (owner, _) = await factory.CreateAuthedClientAsync("owner");
        var (member, memberId) = await factory.CreateAuthedClientAsync("member");

        var teamId = await owner.CreateTeamAsync();
        await owner.PostAsJsonAsync($"/v1/teams/{teamId}/members", new { user_id = memberId });

        var noteId = await owner.CreateNoteAsync();
        await owner.PostAsJsonAsync(
            $"/v1/notes/{noteId}/shares", new { team_id = teamId, permission = "read" });
        Assert.Equal(
            HttpStatusCode.OK, (await member.GetAsync($"/v1/notes/{noteId}")).StatusCode);

        var remove = await owner.SendJsonAsync(
            HttpMethod.Delete, $"/v1/teams/{teamId}/members/{memberId}");
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);

        // Removed member loses both team visibility and the note access it granted.
        await ApiClient.AssertErrorAsync(
            await member.GetAsync($"/v1/teams/{teamId}"), 404, "not_found");
        await ApiClient.AssertErrorAsync(
            await member.GetAsync($"/v1/notes/{noteId}"), 404, "not_found");

        // Removing again is a 404, not a silent no-op.
        await ApiClient.AssertErrorAsync(
            await owner.SendJsonAsync(HttpMethod.Delete, $"/v1/teams/{teamId}/members/{memberId}"),
            404, "not_found");
    }

    [Fact]
    public async Task Only_the_owner_can_remove_members()
    {
        var (owner, _) = await factory.CreateAuthedClientAsync("owner");
        var (member, memberId) = await factory.CreateAuthedClientAsync("member");
        var (other, otherId) = await factory.CreateAuthedClientAsync("other");

        var teamId = await owner.CreateTeamAsync();
        await owner.PostAsJsonAsync($"/v1/teams/{teamId}/members", new { user_id = memberId });
        await owner.PostAsJsonAsync($"/v1/teams/{teamId}/members", new { user_id = otherId });

        // A plain member cannot remove another member.
        await ApiClient.AssertErrorAsync(
            await member.SendJsonAsync(HttpMethod.Delete, $"/v1/teams/{teamId}/members/{otherId}"),
            403, "forbidden");
    }

    [Fact]
    public async Task Owner_cannot_be_removed()
    {
        var (owner, ownerId) = await factory.CreateAuthedClientAsync("owner");
        var teamId = await owner.CreateTeamAsync();

        await ApiClient.AssertErrorAsync(
            await owner.SendJsonAsync(HttpMethod.Delete, $"/v1/teams/{teamId}/members/{ownerId}"),
            422, "validation_error");
    }
}
