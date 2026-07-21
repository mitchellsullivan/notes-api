using NotesService.Domain;

namespace NotesService.Contracts;

public sealed record UserResponse(string Id, string Name, DateTime CreatedAt);

public sealed record TeamResponse(
    string Id,
    string Name,
    string OwnerId,
    DateTime CreatedAt,
    IReadOnlyList<string> MemberIds);

public sealed record NoteResponse(
    string Id,
    string OwnerId,
    string Title,
    string Body,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int Version);

public sealed record ShareResponse(
    string NoteId,
    string? UserId,
    string? TeamId,
    string Permission,
    DateTime CreatedAt);

public sealed record NotesPageResponse(
    IReadOnlyList<NoteResponse> Items,
    int Count,
    int Limit,
    int Offset);

public sealed record NoteDetailResponse(NoteResponse Note, string MyPermission);

public static class ResponseMappings
{
    public static UserResponse ToResponse(this UserEntity user) =>
        new(user.Id, user.Name, user.CreatedAt);

    public static TeamResponse ToResponse(this TeamEntity team) =>
        new(
            team.Id,
            team.Name,
            team.OwnerId,
            team.CreatedAt,
            team.Members.Select(x => x.UserId).OrderBy(id => id, StringComparer.Ordinal).ToArray());

    public static NoteResponse ToResponse(this NoteEntity note) =>
        new(
            note.Id,
            note.OwnerId,
            note.Title,
            note.Body,
            note.CreatedAt,
            note.UpdatedAt,
            note.Version);

    public static ShareResponse ToResponse(this UserNoteShareEntity share) =>
        new(share.NoteId, share.UserId, null, share.Permission.ToApiValue(), share.CreatedAt);

    public static ShareResponse ToResponse(this TeamNoteShareEntity share) =>
        new(share.NoteId, null, share.TeamId, share.Permission.ToApiValue(), share.CreatedAt);
}

public sealed record CreateUserResponse(UserResponse User, string Token);