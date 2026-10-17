using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartRail.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIncidents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "incidents",
                schema: "changes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_report_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reason_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    opened_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incidents", x => x.id);
                    table.CheckConstraint("ck_incidents_reason", "reason_code IN ('TRAIN_FAULT', 'SIGNAL_DELAY', 'OBSTRUCTION', 'CANNOT_CONTINUE')");
                    table.CheckConstraint("ck_incidents_resolution", "(status = 'RESOLVED') = (resolved_at IS NOT NULL)");
                    table.CheckConstraint("ck_incidents_status", "status IN ('OPEN', 'RESOLVED')");
                    table.ForeignKey(
                        name: "FK_incidents_accounts_opened_by_id",
                        column: x => x.opened_by_id,
                        principalSchema: "identity",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_incidents_driver_reports_source_report_id",
                        column: x => x.source_report_id,
                        principalSchema: "changes",
                        principalTable: "driver_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_incidents_opened_at_id",
                schema: "changes",
                table: "incidents",
                columns: new[] { "opened_at", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_incidents_opened_by_id",
                schema: "changes",
                table: "incidents",
                column: "opened_by_id");

            migrationBuilder.CreateIndex(
                name: "ux_incidents_source_report_id",
                schema: "changes",
                table: "incidents",
                column: "source_report_id",
                unique: true);

            // An incident only ever moves from OPEN to RESOLVED, once. Nothing else about it
            // changes, whatever application code or script touches the row.
            migrationBuilder.Sql("""
                CREATE FUNCTION changes.guard_incident_updates() RETURNS trigger AS $$
                BEGIN
                    IF NEW.id IS DISTINCT FROM OLD.id
                       OR NEW.source_report_id IS DISTINCT FROM OLD.source_report_id
                       OR NEW.reason_code IS DISTINCT FROM OLD.reason_code
                       OR NEW.opened_by_id IS DISTINCT FROM OLD.opened_by_id
                       OR NEW.opened_at IS DISTINCT FROM OLD.opened_at THEN
                        RAISE EXCEPTION 'only the resolution of changes.incidents can be set';
                    END IF;
                    IF OLD.status = 'RESOLVED'
                       AND (NEW.status IS DISTINCT FROM OLD.status OR NEW.resolved_at IS DISTINCT FROM OLD.resolved_at) THEN
                        RAISE EXCEPTION 'a resolved incident is final';
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER incidents_guard
                BEFORE UPDATE ON changes.incidents
                FOR EACH ROW EXECUTE FUNCTION changes.guard_incident_updates();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "incidents",
                schema: "changes");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS changes.guard_incident_updates();");
        }
    }
}
