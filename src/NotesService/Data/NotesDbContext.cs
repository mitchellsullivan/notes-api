using Microsoft.EntityFrameworkCore;
using NotesService.Domain;

namespace NotesService.Data;

public sealed class NotesDbContext : DbContext
{
    public NotesDbContext(DbContextOptions<NotesDbContext> options)
        : base(options)
    {
    }
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<TeamEntity> Teams => Set<TeamEntity>();
    public DbSet<TeamMemberEntity> TeamMembers => Set<TeamMemberEntity>();
    public DbSet<NoteEntity> Notes => Set<NoteEntity>();
    public DbSet<UserNoteShareEntity> UserNoteShares => Set<UserNoteShareEntity>();
    public DbSet<TeamNoteShareEntity> TeamNoteShares => Set<TeamNoteShareEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserEntity>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(32);
            entity.Property(x => x.Name).HasMaxLength(ApiLimits.MaxNameRunes).IsRequired();
            entity.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => x.TokenHash).IsUnique();
        });

        modelBuilder.Entity<TeamEntity>(entity =>
        {
            entity.ToTable("teams");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(32);
            entity.Property(x => x.Name).HasMaxLength(ApiLimits.MaxNameRunes).IsRequired();
            entity.HasOne(x => x.Owner)
                .WithMany()
                .HasForeignKey(x => x.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TeamMemberEntity>(entity =>
        {
            entity.ToTable("team_members");
            entity.HasKey(x => new { x.TeamId, x.UserId });
            entity.HasOne(x => x.Team)
                .WithMany(x => x.Members)
                .HasForeignKey(x => x.TeamId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.User)
                .WithMany(x => x.TeamMemberships)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<NoteEntity>(entity =>
        {
            entity.ToTable("notes");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(32);
            entity.Property(x => x.Title).HasMaxLength(ApiLimits.MaxTitleRunes).IsRequired();
            entity.Property(x => x.Body).HasMaxLength(ApiLimits.MaxContentRunes).IsRequired();
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasIndex(x => x.UpdatedAt);
            if (Database.IsNpgsql())
            {
                // Database-maintained tsvector: Postgres recomputes it on
                // every insert/update, so it can never drift from Title/Body
                // the way an application-maintained index could.
                entity.HasGeneratedTsVectorColumn(
                        x => x.SearchVector!,
                        "english",
                        x => new { x.Title, x.Body })
                    .HasIndex(x => x.SearchVector)
                    .HasMethod("GIN");
            }
            else
            {
                entity.Ignore(x => x.SearchVector);
            }
            entity.HasOne(x => x.Owner)
                .WithMany(x => x.OwnedNotes)
                .HasForeignKey(x => x.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UserNoteShareEntity>(entity =>
        {
            entity.ToTable("user_note_shares");
            entity.HasCheckConstraint(
                "CK_user_note_shares_permission",
                "\"Permission\" IN (1, 2)");
            entity.HasKey(x => new { x.NoteId, x.UserId });
            entity.Property(x => x.Permission).HasConversion<int>();
            entity.HasOne(x => x.Note)
                .WithMany(x => x.UserShares)
                .HasForeignKey(x => x.NoteId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TeamNoteShareEntity>(entity =>
        {
            entity.ToTable("team_note_shares");
            entity.HasCheckConstraint(
                "CK_team_note_shares_permission",
                "\"Permission\" IN (1, 2)");
            entity.HasKey(x => new { x.NoteId, x.TeamId });
            entity.Property(x => x.Permission).HasConversion<int>();
            entity.HasOne(x => x.Note)
                .WithMany(x => x.TeamShares)
                .HasForeignKey(x => x.NoteId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Team)
                .WithMany(x => x.NoteShares)
                .HasForeignKey(x => x.TeamId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
