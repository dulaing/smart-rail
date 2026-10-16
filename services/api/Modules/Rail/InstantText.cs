using System.Globalization;
using System.Text.RegularExpressions;

namespace SmartRail.Api.Modules.Rail;

// Reads a timestamp that a caller wrote as text.
public static partial class InstantText
{
    // The offset must be written out ("Z" or "+05:30"); a bare local time would be a guess.
    public static bool TryParse(string? value, out DateTimeOffset instant)
    {
        instant = default;
        return value is not null
            && OffsetSuffix().IsMatch(value)
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out instant);
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T.*(Z|[+-]\d{2}:\d{2})$")]
    private static partial Regex OffsetSuffix();
}
