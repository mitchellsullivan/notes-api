using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NotesService.Auth;
using NotesService.Contracts;
using NotesService.Data;
using NotesService.Domain;

namespace NotesService.Controllers;

[Authorize]
[Route("v1/teams")]
public sealed class TeamsController : ApiControllerBase
{
    private readonly NotesDbContext db;

    public TeamsController(NotesDbContext db)
    {
        this.db = db;
    }
    [HttpPost]
    public async Task<IActionResult> Create(
        CreateTeamRequest request,
        CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        var nameLength = name.EnumerateRunes().Count();
        if (nameLength is < 1 or > ApiLimits.MaxNameRunes)
        {
            return ValidationError($"name must be between 1 and {ApiLimits.MaxNameRunes} characters");
        }

        var now = DateTime.UtcNow;
        var userId = User.UserId();
        var team = new TeamEntity
        {
            Name = name,
            OwnerId = userId,
            CreatedAt = now
        };
        team.Members.Add(new TeamMemberEntity
        {
            TeamId = team.Id,
            UserId = userId,
            JoinedAt = now
        });
        db.Teams.Add(team);
        await db.SaveChangesAsync(cancellationToken);

        return StatusCode(StatusCodes.Status201Created, team.ToResponse());
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken cancellationToken)
    {
        var userId = User.UserId();
        var team = await db.Teams
            .AsNoTracking()
            .Include(x => x.Members)
            .SingleOrDefaultAsync(
                x => x.Id == id && x.Members.Any(member => member.UserId == userId),
                cancellationToken);

        return team is null ? NotFoundError("team not found") : Ok(team.ToResponse());
    }

    [HttpPost("{id}/members")]
    public async Task<IActionResult> AddMember(
        string id,
        AddTeamMemberRequest request,
        CancellationToken cancellationToken)
    {
        var callerId = User.UserId();
        var team = await db.Teams
            .Include(x => x.Members)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (team is null || team.Members.All(member => member.UserId != callerId))
        {
            return NotFoundError("team not found");
        }

        if (team.OwnerId != callerId)
        {
            return ForbiddenError("only the team owner can add members");
        }

        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            return ValidationError("user_id is required");
        }

        if (!await db.Users.AnyAsync(x => x.Id == request.UserId, cancellationToken))
        {
            return NotFoundError();
        }

        if (team.Members.Any(member => member.UserId == request.UserId))
        {
            return Error(StatusCodes.Status409Conflict, "already_exists", "resource already exists");
        }

        team.Members.Add(new TeamMemberEntity
        {
            TeamId = team.Id,
            UserId = request.UserId,
            JoinedAt = DateTime.UtcNow
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Error(StatusCodes.Status409Conflict, "already_exists", "resource already exists");
        }

        return Ok(team.ToResponse());
    }

    [HttpDelete("{id}/members/{userId}")]
    public async Task<IActionResult> RemoveMember(
        string id,
        string userId,
        CancellationToken cancellationToken)
    {
        var callerId = User.UserId();
        var team = await db.Teams
            .Include(x => x.Members)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (team is null || team.Members.All(member => member.UserId != callerId))
        {
            return NotFoundError("team not found");
        }

        if (team.OwnerId != callerId)
        {
            return ForbiddenError("only the team owner can remove members");
        }

        if (userId == team.OwnerId)
        {
            return ValidationError("the team owner cannot be removed");
        }

        var member = team.Members.SingleOrDefault(x => x.UserId == userId);
        if (member is null)
        {
            return NotFoundError();
        }

        team.Members.Remove(member);
        await db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }
}
