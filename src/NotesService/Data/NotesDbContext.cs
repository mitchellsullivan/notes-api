using Microsoft.EntityFrameworkCore;
using NotesService.Domain;

namespace NotesService.Data;

public sealed class NotesDbContext : DbContext
{
    public NotesDbContext(DbContextOptions<NotesDbContext> options)
        : base(options)
    {
    }

    public DbSet<NoteEntity> Notes => Set<NoteEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NoteEntity>(entity =>
        {
            entity.ToTable("notes");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(32);
            entity.Property(x => x.Title).HasMaxLength(ApiLimits.MaxTitleRunes).IsRequired();
            entity.Property(x => x.Body).HasMaxLength(ApiLimits.MaxContentRunes).IsRequired();
            entity.HasIndex(x => x.UpdatedAt);
        });
    }
}
