namespace NotesService.Domain;

public enum PermissionLevel
{
    Read = 1,
    Edit = 2
}

public static class PermissionLevelExtensions
{
    public static string ToApiValue(this PermissionLevel permission) =>
        permission == PermissionLevel.Edit ? "edit" : "read";

    public static bool TryParseApiValue(string? value, out PermissionLevel permission)
    {
        switch (value)
        {
            case "read":
                permission = PermissionLevel.Read;
                return true;
            case "edit":
                permission = PermissionLevel.Edit;
                return true;
            default:
                permission = default;
                return false;
        }
    }
}
