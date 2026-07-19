using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using NotesService.Contracts;

namespace NotesService.Errors;

public sealed class RejectUnknownJsonFieldsFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        var hasUnknownFields = context.ActionArguments.Values
            .OfType<JsonRequest>()
            .Any(request => request.UnmappedFields is { Count: > 0 });

        if (hasUnknownFields)
        {
            context.Result = new BadRequestObjectResult(
                ApiErrors.Envelope(
                    "invalid_request",
                    "request body is missing, malformed, or contains unsupported fields"));
        }
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }
}
