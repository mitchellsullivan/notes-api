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

        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            return ValidationError("user_id is required");
        }

        if (!PermissionLevelExtensions.TryParseApiValue(request.Permission, out var permission))
        {
            return ValidationError("permission must be \"read\" or \"edit\"");
        }

        if (request.UserId == User.UserId())
        {
            return ValidationError("cannot share a note with yourself");
        }

        if (!await db.Users.AnyAsync(x => x.Id == request.UserId, cancellationToken))
        {
            return NotFoundError();
        }

        var now = DateTime.UtcNow;
        var share = await db.UserNoteShares.SingleOrDefaultAsync(
            x => x.NoteId == noteId && x.UserId == request.UserId,
            cancellationToken);
        var created = share is null;
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

        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            return ValidationError("user_id is required");
        }

        var share = await db.UserNoteShares.SingleOrDefaultAsync(
            x => x.NoteId == noteId && x.UserId == request.UserId,
            cancellationToken);
        if (share is null)
        {
            return NotFoundError();
        }

        db.UserNoteShares.Remove(share);
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
        var shareEntities = await db.UserNoteShares
            .AsNoTracking()
            .Where(x => x.NoteId == noteId)
            .ToListAsync(cancellationToken);

        return shareEntities
            .Select(x => x.ToResponse())
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.UserId ?? string.Empty, StringComparer.Ordinal)
            .ToArray();
    }
}
