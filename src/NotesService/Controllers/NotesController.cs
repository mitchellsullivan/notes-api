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
[Route("v1/notes")]
public sealed class NotesController : ApiControllerBase
{
    private readonly NotesDbContext db;
    private readonly NoteAccessService access;

    public NotesController(NotesDbContext db, NoteAccessService access)
    {
        this.db = db;
        this.access = access;
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        CreateNoteRequest request,
        CancellationToken cancellationToken)
    {
        var title = request.Title?.Trim() ?? string.Empty;
        var body = request.Body ?? string.Empty;
        if (!TryValidateNote(title, body, out var message))
        {
            return ValidationError(message);
        }

        var now = DateTime.UtcNow;
        var note = new NoteEntity
        {
            OwnerId = User.UserId(),
            Title = title,
            Body = body,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1
        };

        db.Notes.Add(note);
        await db.SaveChangesAsync(cancellationToken);
        SetEtag(note.Version);
        return StatusCode(StatusCodes.Status201Created, note.ToResponse());
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? q,
        [FromQuery] int limit = ApiLimits.DefaultPageLimit,
        [FromQuery] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > ApiLimits.MaxPageLimit)
        {
            return ValidationError($"limit must be an integer between 1 and {ApiLimits.MaxPageLimit}");
        }

        if (offset < 0)
        {
            return ValidationError("offset must be a non-negative integer");
        }

        var userId = User.UserId();
        var query = db.Notes
            .AsNoTracking()
            .Where(note =>
                note.OwnerId == userId ||
                note.UserShares.Any(share => share.UserId == userId));

        var normalizedQuery = q?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(normalizedQuery))
        {
            query = query.Where(note =>
                note.Title.ToLower().Contains(normalizedQuery) ||
                note.Body.ToLower().Contains(normalizedQuery));
        }

        var count = await query.CountAsync(cancellationToken);
        var noteEntities = await query
            .OrderByDescending(note => note.UpdatedAt)
            .ThenBy(note => note.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
        var notes = noteEntities.Select(note => note.ToResponse()).ToArray();

        return Ok(new NotesPageResponse(notes, count, limit, offset));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken cancellationToken)
    {
        var note = await db.Notes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (note is null)
        {
            return NotFoundError("note not found");
        }

        var permission = await access.GetPermissionAsync(id, User.UserId(), cancellationToken);
        if (permission is null)
        {
            // 404, not 403: the API must not confirm the note exists.
            return NotFoundError("note not found");
        }

        SetEtag(note.Version);
        return Ok(new { note = note.ToResponse(), my_permission = permission.Value.ToApiValue() });
    }

    [HttpPatch("{id}")]
    public async Task<IActionResult> Update(
        string id,
        UpdateNoteRequest request,
        CancellationToken cancellationToken)
    {
        var note = await db.Notes.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (note is null)
        {
            return NotFoundError("note not found");
        }

        var permission = await access.GetPermissionAsync(id, User.UserId(), cancellationToken);
        if (permission is null)
        {
            return NotFoundError("note not found");
        }

        if (permission != PermissionLevel.Edit)
        {
            return ForbiddenError("you have read-only access to this note");
        }

        if (!TryReadIfMatch(out var expectedVersion, out var preconditionError))
        {
            return preconditionError!;
        }

        if (expectedVersion != note.Version)
        {
            return VersionConflict();
        }

        if (request.Title is null && request.Body is null)
        {
            return ValidationError("nothing to update: provide title and/or body");
        }

        var title = request.Title is null ? note.Title : request.Title.Trim();
        var body = request.Body ?? note.Body;
        if (!TryValidateNote(title, body, out var message))
        {
            return ValidationError(message);
        }

        db.Entry(note).Property(x => x.Version).OriginalValue = expectedVersion;
        note.Title = title;
        note.Body = body;
        note.UpdatedAt = DateTime.UtcNow;
        note.Version++;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return VersionConflict();
        }

        SetEtag(note.Version);
        return Ok(note.ToResponse());
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        var note = await db.Notes.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (note is null)
        {
            return NotFoundError("note not found");
        }

        var permission = await access.GetPermissionAsync(id, User.UserId(), cancellationToken);
        if (permission is null)
        {
            return NotFoundError("note not found");
        }

        if (note.OwnerId != User.UserId())
        {
            return ForbiddenError("only the owner can delete a note");
        }

        if (!TryReadIfMatch(out var expectedVersion, out var preconditionError))
        {
            return preconditionError!;
        }

        if (expectedVersion != note.Version)
        {
            return VersionConflict();
        }

        db.Entry(note).Property(x => x.Version).OriginalValue = expectedVersion;
        db.Notes.Remove(note);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return VersionConflict();
        }

        return NoContent();
    }

    private bool TryReadIfMatch(out int version, out IActionResult? error)
    {
        var raw = Request.Headers.IfMatch.ToString().Trim();
        if (raw.Length < 3 || raw[0] != '"' || raw[^1] != '"' ||
            !int.TryParse(raw[1..^1], out version) || version < 1)
        {
            error = Error(
                StatusCodes.Status428PreconditionRequired,
                "precondition_required",
                "If-Match must contain the note's ETag, e.g. If-Match: \"3\"");
            version = 0;
            return false;
        }

        error = null;
        return true;
    }

    private void SetEtag(int version) => Response.Headers.ETag = $"\"{version}\"";

    private static bool TryValidateNote(string title, string body, out string message)
    {
        var titleLength = title.EnumerateRunes().Count();
        if (titleLength is < 1 or > ApiLimits.MaxTitleRunes)
        {
            message = $"title must be between 1 and {ApiLimits.MaxTitleRunes} characters";
            return false;
        }

        if (body.EnumerateRunes().Count() > ApiLimits.MaxContentRunes)
        {
            message = $"body must not exceed {ApiLimits.MaxContentRunes} characters";
            return false;
        }

        message = string.Empty;
        return true;
    }
}
