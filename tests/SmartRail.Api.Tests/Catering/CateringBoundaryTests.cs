using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace SmartRail.Api.Tests.Catering;

// Catering reads Rail, Operations and Access only through their named interfaces. It must not query
// another module's EF sets or hold its entities or rules. Mapping files (*Model.cs) are the one
// exception: a foreign key has to name the table it points at.
public partial class CateringBoundaryTests
{
    [GeneratedRegex(@"\b(db|context|dbContext)\.(Stations|ServicePatterns|PatternStops|Journeys|Observations|Accounts|Roles|RoleGrants|JourneyAssignments|AuditEntries|JourneyVersions|ServiceChanges|DriverReports|Incidents)\b")]
    private static partial Regex ForeignSet();

    [GeneratedRegex(@"\b(ScheduledJourney|ServicePattern|PatternStop|Observation|Account|RoleGrant|JourneyAssignment|AuditEntry|ServiceChange|ChangeStore|ConfirmedState|JourneyOutcome|RunningCalculator|DriverReport|Incident)\b")]
    private static partial Regex ForeignEntity();

    [GeneratedRegex(@"using\s+SmartRail\.Api\.Modules\.(Rail\.(Timetable|Running)|Operations\.(Incidents|Reports));")]
    private static partial Regex ForeignNamespace();

    private static string CateringFolder([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "..", "services", "api", "Modules", "Catering"));

    private static string WithoutComments(string source) => Regex.Replace(source, @"//.*", "");

    [Fact]
    public void Catering_code_does_not_reach_into_another_modules_tables_or_rules()
    {
        var files = Directory.GetFiles(CateringFolder(), "*.cs", SearchOption.AllDirectories)
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
        Assert.Matches(ForeignNamespace(), "using SmartRail.Api.Modules.Rail.Running;");
    }
}
