namespace NotesService.Domain;

public sealed class UserEntity
{
    public string Id { get; set; } = NewId();
    public string Name { get; set; } = null!;
    public string TokenHash { get; set; } = null!;
    public DateTime CreatedAt { get; set; }

    public ICollection<NoteEntity> OwnedNotes { get; set; } = new List<NoteEntity>();

    private static string NewId() => Guid.NewGuid().ToString("N");
}

public sealed class NoteEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OwnerId { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int Version { get; set; } = 1;

    public UserEntity Owner { get; set; } = null!;
}
