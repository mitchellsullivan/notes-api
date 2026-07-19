using System.Text;
using System.Text.Json;

namespace NotesService.Serialization;

public sealed class SnakeCaseNamingPolicy : JsonNamingPolicy
{
    public static SnakeCaseNamingPolicy Instance { get; } = new SnakeCaseNamingPolicy();

    private SnakeCaseNamingPolicy()
    {
    }

    public override string ConvertName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        var builder = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var current = name[i];
            if (!char.IsUpper(current))
            {
                builder.Append(current);
                continue;
            }

            var hasPrevious = i > 0;
            var previousIsLowerOrDigit = hasPrevious &&
                (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1]));
            var nextIsLower = i + 1 < name.Length && char.IsLower(name[i + 1]);

            if (hasPrevious && (previousIsLowerOrDigit || nextIsLower))
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(current));
        }

        return builder.ToString();
    }
}
