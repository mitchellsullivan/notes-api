namespace NotesService.Contracts;

public sealed class CreateNoteRequest
{
    public string? Title { get; set; }
    public string? Body { get; set; }
}
