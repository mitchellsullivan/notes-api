using Microsoft.EntityFrameworkCore;
using NotesService.Data;
using NotesService.Domain;

namespace NotesService.Services;

public sealed class NoteAccessService
{
    private readonly NotesDbContext db;

    public NoteAccessService(NotesDbContext db)
    {
        this.db = db;
    }

    public async Task<PermissionLevel?> GetPermissionAsync(
        string noteId,
        string userId,
        CancellationToken cancellationToken)
    {
        var ownerId = await db.Notes
            .AsNoTracking()
            .Where(x => x.Id == noteId)
            .Select(x => x.OwnerId)
            .SingleOrDefaultAsync(cancellationToken);

        if (ownerId is null)
        {
            return null;
        }

        if (ownerId == userId)
        {
            return PermissionLevel.Edit;
        }

        return await db.UserNoteShares
            .AsNoTracking()
            .Where(x => x.NoteId == noteId && x.UserId == userId)
            .Select(x => (PermissionLevel?)x.Permission)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
