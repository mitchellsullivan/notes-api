using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NotesService.Auth;
using NotesService.Contracts;
using NotesService.Data;
using NotesService.Domain;

namespace NotesService.Controllers;

[Route("v1/users")]
public sealed class UsersController : ApiControllerBase
{
    private readonly NotesDbContext db;

    public UsersController(NotesDbContext db)
    {
        this.db = db;
    }
    [AllowAnonymous]
    [HttpPost]
    public async Task<IActionResult> Create(
        CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        var nameLength = name.EnumerateRunes().Count();
        if (nameLength is < 1 or > ApiLimits.MaxNameRunes)
        {
            return ValidationError($"name must be between 1 and {ApiLimits.MaxNameRunes} characters");
        }

        var token = TokenService.CreateToken();
        var user = new UserEntity
        {
            Name = name,
            TokenHash = TokenService.HashToken(token),
            CreatedAt = DateTime.UtcNow
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        return StatusCode(
            StatusCodes.Status201Created,
            new { user = user.ToResponse(), token });
    }
}

[Authorize]
[Route("v1/me")]
public sealed class MeController : ApiControllerBase
{
    private readonly NotesDbContext db;

    public MeController(NotesDbContext db)
    {
        this.db = db;
    }
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var user = await db.Users
            .AsNoTracking()
            .SingleAsync(x => x.Id == User.UserId(), cancellationToken);
        return Ok(user.ToResponse());
    }
}
