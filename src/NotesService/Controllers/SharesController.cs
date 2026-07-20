using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NotesService.Auth;
using NotesService.Contracts;
using NotesService.Data;
using NotesService.Domain;
using NotesService.Services;

namespace NotesService.Controllers;

[Authorize]
[Route("v1/notes/{noteId}/shares")]
public sealed class SharesController : ApiControllerBase
{
    private readonly NotesDbContext db;
    private readonly NoteAccessService access;

    public SharesController(NotesDbContext db, NoteAccessService access)
    {
        this.db = db;
        this.access = access;
    }
    [HttpPost]
    public async Task<IActionResult> Share(
        string noteId,
        ShareNoteRequest request,
        CancellationToken cancellationToken)
    {
        var note = await GetOwnedNoteAsync(noteId, cancellationToken);
        if (note.Result is not null)
        {
            return note.Result;
        }

        if (string.IsNullOrWhiteSpace(request.UserId) == string.IsNullOrWhiteSpace(request.TeamId))
        {
            return ValidationError("provide exactly one of user_id or team_id");
        }

        if (!PermissionLevelExtensions.TryParseApiValue(request.Permission, out var permission))
        {
            return ValidationError("permission must be \"read\" or \"edit\"");
        }

        var now = DateTime.UtcNow;
        bool created;
        if (!string.IsNullOrWhiteSpace(request.UserId))
        {
            if (request.UserId == User.UserId())
            {
                return ValidationError("cannot share a note with yourself");
            }

            if (!await db.Users.AnyAsync(x => x.Id == request.UserId, cancellationToken))
            {
                return NotFoundError();
            }

            var share = await db.UserNoteShares.SingleOrDefaultAsync(
                x => x.NoteId == noteId && x.UserId == request.UserId,
                cancellationToken);
            created = share is null;
            if (share is null)
            {
                db.UserNoteShares.Add(new UserNoteShareEntity
                {
                    NoteId = noteId,
                    UserId = request.UserId,
                    Permission = permission,
                    CreatedAt = now
                });
            }
            else
            {
                share.Permission = permission;
                share.CreatedAt = now;
            }
        }
        else
        {
            var team = await db.Teams
                .AsNoTracking()
                .Include(x => x.Members)
                .SingleOrDefaultAsync(x => x.Id == request.TeamId, cancellationToken);
            if (team is null)
            {
                return NotFoundError();
            }

            // A caller may only target a team they belong to. This prevents
            // injecting notes into an unrelated team with a leaked team ID.
            if (team.Members.All(member => member.UserId != User.UserId()))
            {
                return NotFoundError();
            }

            var share = await db.TeamNoteShares.SingleOrDefaultAsync(
                x => x.NoteId == noteId && x.TeamId == request.TeamId,
                cancellationToken);
            created = share is null;
            if (share is null)
            {
                db.TeamNoteShares.Add(new TeamNoteShareEntity
                {
                    NoteId = noteId,
                    TeamId = request.TeamId!,
                    Permission = permission,
                    CreatedAt = now
                });
            }
            else
            {
                share.Permission = permission;
                share.CreatedAt = now;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return StatusCode(
            created ? StatusCodes.Status201Created : StatusCodes.Status200OK,
            await ListSharesAsync(noteId, cancellationToken));
    }

    [HttpGet]
    public async Task<IActionResult> List(string noteId, CancellationToken cancellationToken)
    {
        var note = await GetOwnedNoteAsync(noteId, cancellationToken);
        return note.Result ?? Ok(await ListSharesAsync(noteId, cancellationToken));
    }

    [HttpDelete]
    public async Task<IActionResult> Unshare(
        string noteId,
        UnshareNoteRequest request,
        CancellationToken cancellationToken)
    {
        var note = await GetOwnedNoteAsync(noteId, cancellationToken);
        if (note.Result is not null)
        {
            return note.Result;
        }

        if (string.IsNullOrWhiteSpace(request.UserId) == string.IsNullOrWhiteSpace(request.TeamId))
        {
            return ValidationError("provide exactly one of user_id or team_id");
        }

        if (!string.IsNullOrWhiteSpace(request.UserId))
        {
            var share = await db.UserNoteShares.SingleOrDefaultAsync(
                x => x.NoteId == noteId && x.UserId == request.UserId,
                cancellationToken);
            if (share is null)
            {
                return NotFoundError();
            }
            db.UserNoteShares.Remove(share);
        }
        else
        {
            var share = await db.TeamNoteShares.SingleOrDefaultAsync(
                x => x.NoteId == noteId && x.TeamId == request.TeamId,
                cancellationToken);
            if (share is null)
            {
                return NotFoundError();
            }
            db.TeamNoteShares.Remove(share);
        }

        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<(NoteEntity? Note, IActionResult? Result)> GetOwnedNoteAsync(
        string noteId,
        CancellationToken cancellationToken)
    {
        var note = await db.Notes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == noteId, cancellationToken);
        if (note is null || await access.GetPermissionAsync(noteId, User.UserId(), cancellationToken) is null)
        {
            return (null, NotFoundError("note not found"));
        }

        if (note.OwnerId != User.UserId())
        {
            return (null, ForbiddenError("only the owner can manage shares"));
        }

        return (note, null);
    }

    private async Task<IReadOnlyList<ShareResponse>> ListSharesAsync(
        string noteId,
        CancellationToken cancellationToken)
    {
        var userShareEntities = await db.UserNoteShares
            .AsNoTracking()
            .Where(x => x.NoteId == noteId)
            .ToListAsync(cancellationToken);
        var teamShareEntities = await db.TeamNoteShares
            .AsNoTracking()
            .Where(x => x.NoteId == noteId)
            .ToListAsync(cancellationToken);

        return userShareEntities.Select(x => x.ToResponse())
            .Concat(teamShareEntities.Select(x => x.ToResponse()))
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.UserId ?? x.TeamId ?? string.Empty, StringComparer.Ordinal)
            .ToArray();
    }
}
