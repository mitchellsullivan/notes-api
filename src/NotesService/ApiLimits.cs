namespace NotesService;

public static class ApiLimits
{
    public const long MaxBodyBytes = 1 << 20;
    public const int MaxNameRunes = 100;
    public const int MaxTitleRunes = 200;
    public const int MaxContentRunes = 100_000;
    public const int DefaultPageLimit = 50;
    public const int MaxPageLimit = 100;
}
