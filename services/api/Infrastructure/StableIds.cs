using System.Security.Cryptography;
using System.Text;

namespace SmartRail.Api.Infrastructure;

public static class StableIds
{
    // The same kind and key always give the same GUID, so a re-seed recognises its own rows.
    public static Guid For(string kind, string key) =>
        new(MD5.HashData(Encoding.UTF8.GetBytes($"smartrail:{kind}:{key}")));
}
