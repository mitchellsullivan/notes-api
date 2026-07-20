using NpgsqlTypes;

namespace NotesService.Domain;

public sealed class UserEntity
{
    public string Id { get; set; } = NewId();
    public string Name { get; set; } = null!;
    public string TokenHash { get; set; } = null!;
    public DateTime CreatedAt { get; set; }

    public ICollection<TeamMemberEntity> TeamMemberships { get; set; } = new List<TeamMemberEntity>();
    public ICollection<NoteEntity> OwnedNotes { get; set; } = new List<NoteEntity>();

    private static string NewId() => Guid.NewGuid().ToString("N");
}

public sealed class TeamEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = null!;
    public string OwnerId { get; set; } = null!;
    public DateTime CreatedAt { get; set; }

    public UserEntity Owner { get; set; } = null!;
    public ICollection<TeamMemberEntity> Members { get; set; } = new List<TeamMemberEntity>();
    public ICollection<TeamNoteShareEntity> NoteShares { get; set; } = new List<TeamNoteShareEntity>();
}

public sealed class TeamMemberEntity
{
    public string TeamId { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public DateTime JoinedAt { get; set; }

    public TeamEntity Team { get; set; } = null!;
    public UserEntity User { get; set; } = null!;
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

    /// <summary>
    /// Postgres-only: a database-generated tsvector over Title + Body,
    /// indexed with GIN. Ignored entirely on the SQLite provider.
    /// </summary>
    public NpgsqlTsVector? SearchVector { get; set; }

    public UserEntity Owner { get; set; } = null!;
    public ICollection<UserNoteShareEntity> UserShares { get; set; } = new List<UserNoteShareEntity>();
    public ICollection<TeamNoteShareEntity> TeamShares { get; set; } = new List<TeamNoteShareEntity>();
}

public sealed class UserNoteShareEntity
{
    public string NoteId { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public PermissionLevel Permission { get; set; }
    public DateTime CreatedAt { get; set; }

    public NoteEntity Note { get; set; } = null!;
    public UserEntity User { get; set; } = null!;
}

public sealed class TeamNoteShareEntity
{
    public string NoteId { get; set; } = null!;
    public string TeamId { get; set; } = null!;
    public PermissionLevel Permission { get; set; }
    public DateTime CreatedAt { get; set; }

    public NoteEntity Note { get; set; } = null!;
    public TeamEntity Team { get; set; } = null!;
}
