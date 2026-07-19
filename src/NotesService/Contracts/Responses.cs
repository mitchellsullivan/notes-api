using NotesService.Domain;

namespace NotesService.Contracts;

public sealed record NoteResponse(
    string Id,
    string Title,
    string Body,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record NotesPageResponse(
    IReadOnlyList<NoteResponse> Items,
    int Count,
    int Limit,
    int Offset);

public static class ResponseMappings
{
    public static NoteResponse ToResponse(this NoteEntity note) =>
        new(note.Id, note.Title, note.Body, note.CreatedAt, note.UpdatedAt);
}
