using Microsoft.AspNetCore.Mvc;
using NotesService.Errors;

namespace NotesService.Controllers;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected ObjectResult Error(int status, string code, string message) =>
        StatusCode(status, ApiErrors.Envelope(code, message));

    protected ObjectResult ValidationError(string message) =>
        Error(StatusCodes.Status422UnprocessableEntity, "validation_error", message);

    protected ObjectResult NotFoundError(string message = "resource not found") =>
        Error(StatusCodes.Status404NotFound, "not_found", message);

    protected ObjectResult ForbiddenError(string message) =>
        Error(StatusCodes.Status403Forbidden, "forbidden", message);

    protected ObjectResult VersionConflict() =>
        Error(
            StatusCodes.Status412PreconditionFailed,
            "version_conflict",
            "the note was modified by another request; re-fetch and retry");
}
