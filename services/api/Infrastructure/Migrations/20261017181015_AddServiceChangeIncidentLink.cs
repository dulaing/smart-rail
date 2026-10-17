using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartRail.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceChangeIncidentLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "incident_id",
                schema: "changes",
                table: "service_changes",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_service_changes_incident_id",
                schema: "changes",
                table: "service_changes",
                column: "incident_id");

            migrationBuilder.AddForeignKey(
                name: "FK_service_changes_incidents_incident_id",
                schema: "changes",
                table: "service_changes",
                column: "incident_id",
                principalSchema: "changes",
                principalTable: "incidents",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // A withdrawal keeps the incident of the change it withdraws, even once that incident
            // is resolved, and can never name another one.
            migrationBuilder.Sql("""
                CREATE FUNCTION changes.require_withdrawal_incident() RETURNS trigger AS $$
                BEGIN
                    IF NEW.action = 'WITHDRAW' AND NEW.incident_id IS DISTINCT FROM
                       (SELECT incident_id FROM changes.service_changes WHERE id = NEW.supersedes_change_id) THEN
                        RAISE EXCEPTION 'a withdrawal keeps the incident of the change it withdraws';
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER service_changes_withdrawal_incident
                BEFORE INSERT ON changes.service_changes
                FOR EACH ROW EXECUTE FUNCTION changes.require_withdrawal_incident();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS service_changes_withdrawal_incident ON changes.service_changes;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS changes.require_withdrawal_incident();");

            migrationBuilder.DropForeignKey(
                name: "FK_service_changes_incidents_incident_id",
                schema: "changes",
                table: "service_changes");

            migrationBuilder.DropIndex(
                name: "IX_service_changes_incident_id",
                schema: "changes",
                table: "service_changes");

            migrationBuilder.DropColumn(
                name: "incident_id",
                schema: "changes",
                table: "service_changes");
        }
    }
}
