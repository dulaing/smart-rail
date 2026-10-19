using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace SmartRail.Api.Modules.Operations;

// Where the next page of a list starts: strictly after this time and ID. The client treats
// the text as opaque and sends it back unchanged.
public sealed record PageCursor(DateTimeOffset Time, Guid Id)
{
    public const int DefaultSize = 50;
    public const int MaxSize = 100;

    public static int SizeFor(int? requested) => Math.Clamp(requested ?? DefaultSize, 1, MaxSize);

    public string Encode() =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes($"{Time.UtcTicks.ToString(CultureInfo.InvariantCulture)}.{Id:N}"));

    public static bool TryDecode(string text, out PageCursor cursor)
    {
        cursor = null!;
        try
        {
            var parts = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(text)).Split('.');
            if (parts.Length == 2
                && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                && ticks <= DateTime.MaxValue.Ticks
                && Guid.TryParseExact(parts[1], "N", out var id))
            {
                cursor = new PageCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
                return true;
            }
        }
        catch (FormatException)
        {
        }

        return false;
    }
}
