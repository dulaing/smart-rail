using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace SmartRail.Api.Tests.Operations;

// Operations may use Rail, Access and Catering only through their small interfaces. It must not
// query another module's EF sets or hold its entities. Mapping files (*Model.cs) are the one
// exception: a foreign key has to name the table it points at.
public partial class ModuleBoundaryTests
{
    // Another module's DbSets, and the entity types behind them.
    [GeneratedRegex(@"\b(db|context|dbContext)\.(Stations|ServicePatterns|PatternStops|Journeys|Observations|Accounts|Roles|RoleGrants|JourneyAssignments|AuditEntries|OutboxMessages)\b")]
    private static partial Regex ForeignSet();

    [GeneratedRegex(@"(?<!AccessTarget\.)\b(ScheduledJourney|ServicePattern|PatternStop|Station|Observation|Account|Role|RoleGrant|JourneyAssignment|AuditEntry|OutboxMessage)\b")]
    private static partial Regex ForeignEntity();

    [GeneratedRegex(@"using\s+SmartRail\.Api\.Modules\.(Rail\.(Timetable|Running)|Catering\.Outbox);")]
    private static partial Regex ForeignNamespace();

    private static string OperationsFolder([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "..", "services", "api", "Modules", "Operations"));

    private static string WithoutComments(string source) => Regex.Replace(source, @"//.*", "");

    [Fact]
    public void Operations_code_does_not_reach_into_another_modules_tables()
    {
        var files = Directory.GetFiles(OperationsFolder(), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith("Model.cs", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(files);

        var violations = new List<string>();
        foreach (var file in files)
        {
            var source = WithoutComments(File.ReadAllText(file));
            foreach (var regex in new[] { ForeignSet(), ForeignEntity(), ForeignNamespace() })
            {
                foreach (Match match in regex.Matches(source))
                {
                    violations.Add($"{Path.GetFileName(file)}: {match.Value}");
                }
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void The_check_would_notice_a_violation()
    {
        Assert.Matches(ForeignSet(), "var x = db.Journeys.ToList();");
        Assert.Matches(ForeignEntity(), "new PatternStop()");
        Assert.DoesNotMatch(ForeignEntity(), "new AccessTarget.Station(id)");
        Assert.Matches(ForeignNamespace(), "using SmartRail.Api.Modules.Rail.Timetable;");
    }
}
