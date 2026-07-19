using Microsoft.EntityFrameworkCore;

namespace NotesService.Errors;

public sealed class ApiExceptionMiddleware
{
    private readonly RequestDelegate next;
    private readonly ILogger<ApiExceptionMiddleware> logger;

    public ApiExceptionMiddleware(
        RequestDelegate next,
        ILogger<ApiExceptionMiddleware> logger)
    {
        this.next = next;
        this.logger = logger;
    }
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status413PayloadTooLarge,
                "request_too_large",
                $"request body exceeds {ApiLimits.MaxBodyBytes} bytes");
        }
        catch (DbUpdateException exception)
        {
            logger.LogError(exception, "Database update failed");
            await WriteErrorAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "internal_error",
                "an internal error occurred");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled request failure");
            await WriteErrorAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "internal_error",
                "an internal error occurred");
        }
    }

    private static async Task WriteErrorAsync(
        HttpContext context,
        int status,
        string code,
        string message)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(ApiErrors.Envelope(code, message));
    }
}
