namespace NotesService.Tests;

public sealed class NoteListTests : IClassFixture<NotesApiFactory>
{
    private readonly NotesApiFactory factory;

    public NoteListTests(NotesApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task List_paginates_with_a_stable_total_count()
    {
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
    public async Task List_validates_paging_parameters()
    {
        var (client, _) = await factory.CreateAuthedClientAsync();

        await ApiClient.AssertErrorAsync(
            await client.GetAsync("/v1/notes?limit=0"), 422, "validation_error");
        await ApiClient.AssertErrorAsync(
            await client.GetAsync("/v1/notes?limit=101"), 422, "validation_error");
        await ApiClient.AssertErrorAsync(
            await client.GetAsync("/v1/notes?offset=-1"), 422, "validation_error");
        // Unbindable value is malformed input, not a validation failure.
        await ApiClient.AssertErrorAsync(
            await client.GetAsync("/v1/notes?limit=lots"), 400, "invalid_request");
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

    [Fact]
    public async Task List_includes_notes_shared_directly_and_via_team()
    {
        var (owner, _) = await factory.CreateAuthedClientAsync("owner");
        var (member, memberId) = await factory.CreateAuthedClientAsync("member");

        var directNoteId = await owner.CreateNoteAsync(title: "direct share");
        var directShare = await owner.PostAsJsonAsync(
            $"/v1/notes/{directNoteId}/shares", new { user_id = memberId, permission = "read" });
        Assert.Equal(HttpStatusCode.Created, directShare.StatusCode);

        var teamId = await owner.CreateTeamAsync();
        var add = await owner.PostAsJsonAsync(
            $"/v1/teams/{teamId}/members", new { user_id = memberId });
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);

        var teamNoteId = await owner.CreateNoteAsync(title: "team share");
        var teamShare = await owner.PostAsJsonAsync(
            $"/v1/notes/{teamNoteId}/shares", new { team_id = teamId, permission = "read" });
        Assert.Equal(HttpStatusCode.Created, teamShare.StatusCode);

        var response = await member.GetAsync("/v1/notes");
        using var json = await ApiClient.ReadJsonAsync(response);
        var ids = json.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetString())
            .ToArray();

        Assert.Contains(directNoteId, ids);
        Assert.Contains(teamNoteId, ids);
        // The member's own list contains nothing else.
        Assert.Equal(2, json.RootElement.GetProperty("count").GetInt32());
    }
}
