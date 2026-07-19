using System.Text.Json;
using System.Text.Json.Serialization;

namespace NotesService.Contracts;

public abstract class JsonRequest
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnmappedFields { get; set; }
}

public sealed class CreateUserRequest : JsonRequest
{
    public string? Name { get; set; }
}

public sealed class CreateNoteRequest : JsonRequest
{
    public string? Title { get; set; }
    public string? Body { get; set; }
}

public sealed class UpdateNoteRequest : JsonRequest
{
    public string? Title { get; set; }
    public string? Body { get; set; }
}
