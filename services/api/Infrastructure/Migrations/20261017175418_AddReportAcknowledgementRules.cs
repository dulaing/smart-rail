using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartRail.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReportAcknowledgementRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_driver_reports_received_at_id",
                schema: "changes",
                table: "driver_reports",
                columns: new[] { "received_at", "id" });

            // A report is a record of what was sent. Only its acknowledgement can ever be set, and
            // the first one is final, whatever application code or script touches the row.
            migrationBuilder.Sql("""
                CREATE FUNCTION changes.guard_driver_report_updates() RETURNS trigger AS $$
                BEGIN
                    IF NEW.id IS DISTINCT FROM OLD.id
                       OR NEW.journey_id IS DISTINCT FROM OLD.journey_id
                       OR NEW.submitted_by_id IS DISTINCT FROM OLD.submitted_by_id
                       OR NEW.client_report_id IS DISTINCT FROM OLD.client_report_id
                       OR NEW.reason_code IS DISTINCT FROM OLD.reason_code
                       OR NEW.received_at IS DISTINCT FROM OLD.received_at THEN
                        RAISE EXCEPTION 'only the acknowledgement of changes.driver_reports can be set';
                    END IF;
                    IF OLD.acknowledged_at IS NOT NULL
                       AND (NEW.acknowledged_by_id IS DISTINCT FROM OLD.acknowledged_by_id
                            OR NEW.acknowledged_at IS DISTINCT FROM OLD.acknowledged_at) THEN
                        RAISE EXCEPTION 'the first acknowledgement of a driver report is final';
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER driver_reports_guard
                BEFORE UPDATE ON changes.driver_reports
                FOR EACH ROW EXECUTE FUNCTION changes.guard_driver_report_updates();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS driver_reports_guard ON changes.driver_reports;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS changes.guard_driver_report_updates();");

            migrationBuilder.DropIndex(
                name: "IX_driver_reports_received_at_id",
                schema: "changes",
                table: "driver_reports");
        }
    }
}
