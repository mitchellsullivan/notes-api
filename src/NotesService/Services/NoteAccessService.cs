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

        var direct = await db.UserNoteShares
            .AsNoTracking()
            .Where(x => x.NoteId == noteId && x.UserId == userId)
            .Select(x => (PermissionLevel?)x.Permission)
            .SingleOrDefaultAsync(cancellationToken);

        if (direct == PermissionLevel.Edit)
        {
            return PermissionLevel.Edit;
        }

        var teamPermissions = await db.TeamNoteShares
            .AsNoTracking()
            .Where(share =>
                share.NoteId == noteId &&
                share.Team.Members.Any(member => member.UserId == userId))
            .Select(share => share.Permission)
            .ToListAsync(cancellationToken);
        var teamPermission = teamPermissions.Count == 0
            ? (PermissionLevel?)null
            : teamPermissions.Max();

        if (teamPermission == PermissionLevel.Edit)
        {
            return PermissionLevel.Edit;
        }

        return direct ?? teamPermission;
    }
}
