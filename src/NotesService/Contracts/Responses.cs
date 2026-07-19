using NotesService.Domain;

namespace NotesService.Contracts;

public sealed record UserResponse(string Id, string Name, DateTime CreatedAt);

public sealed record NoteResponse(
    string Id,
    string OwnerId,
    string Title,
    string Body,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int Version);

public sealed record NotesPageResponse(
    IReadOnlyList<NoteResponse> Items,
    int Count,
    int Limit,
    int Offset);

public static class ResponseMappings
{
    public static UserResponse ToResponse(this UserEntity user) =>
        new(user.Id, user.Name, user.CreatedAt);

    public static NoteResponse ToResponse(this NoteEntity note) =>
        new(
            note.Id,
            note.OwnerId,
            note.Title,
            note.Body,
            note.CreatedAt,
            note.UpdatedAt,
            note.Version);
}
