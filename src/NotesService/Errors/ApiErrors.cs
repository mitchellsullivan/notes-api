namespace NotesService.Errors;

public sealed record ApiErrorEnvelope(ApiErrorDetail Error);
public sealed record ApiErrorDetail(string Code, string Message);

public static class ApiErrors
{
    public static ApiErrorEnvelope Envelope(string code, string message) =>
        new(new ApiErrorDetail(code, message));
}
