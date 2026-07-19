using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NotesService.Auth;
using NotesService.Contracts;
using NotesService.Data;
using NotesService.Domain;

namespace NotesService.Controllers;

[ApiController]
[Authorize]
[Route("v1/notes")]
public sealed class NotesController : ControllerBase
{
    private readonly NotesDbContext db;

    public NotesController(NotesDbContext db)
    {
        this.db = db;
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
            return BadRequest(new { error = message });
        }

        var now = DateTime.UtcNow;
        var note = new NoteEntity
        {
            OwnerId = User.UserId(),
            Title = title,
            Body = body,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Notes.Add(note);
        await db.SaveChangesAsync(cancellationToken);
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
            return BadRequest(new { error = $"limit must be an integer between 1 and {ApiLimits.MaxPageLimit}" });
        }

        if (offset < 0)
        {
            return BadRequest(new { error = "offset must be a non-negative integer" });
        }

        var userId = User.UserId();
        var query = db.Notes
            .AsNoTracking()
            .Where(note => note.OwnerId == userId);

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
        if (note is null || note.OwnerId != User.UserId())
        {
            // 404, not 403: the API must not confirm the note exists.
            return NotFound();
        }

        return Ok(note.ToResponse());
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        var note = await db.Notes.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (note is null || note.OwnerId != User.UserId())
        {
            return NotFound();
        }

        db.Notes.Remove(note);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

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
