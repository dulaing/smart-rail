using System.Text.Json;
using SmartRail.Api.Modules.Rail.Running;

namespace SmartRail.Api.Tests.Rail;

// Runs the language-neutral freshness vectors against the C# rule.
public class FreshnessVectorTests
{
    private sealed record Vectors(int AgeingAfterSeconds, int UnknownAfterSeconds, List<VectorCase> Cases);

    private sealed record VectorCase(string Name, DateTimeOffset? ObservedAt, DateTimeOffset Now, string Freshness);

    private static Vectors Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Rail", "freshness-vectors.json");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        return JsonSerializer.Deserialize<Vectors>(File.ReadAllText(path), options)!;
    }

    [Fact]
    public void Vectors_use_the_thresholds_the_server_supplies()
    {
        var vectors = Load();

        Assert.Equal(FreshnessRules.AgeingAfterSeconds, vectors.AgeingAfterSeconds);
        Assert.Equal(FreshnessRules.UnknownAfterSeconds, vectors.UnknownAfterSeconds);
    }

    [Fact]
    public void Every_vector_case_gets_its_expected_freshness()
    {
        var vectors = Load();

        Assert.NotEmpty(vectors.Cases);
        foreach (var c in vectors.Cases)
        {
            Assert.True(
                c.Freshness == FreshnessRules.Classify(c.ObservedAt, c.Now).ToCode(),
                $"Vector '{c.Name}' expected {c.Freshness}.");
        }
    }
}
