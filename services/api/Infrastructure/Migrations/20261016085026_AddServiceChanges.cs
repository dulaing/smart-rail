using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartRail.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "changes");

            migrationBuilder.CreateTable(
                name: "journey_change_versions",
                schema: "changes",
                columns: table => new
                {
                    journey_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_journey_change_versions", x => x.journey_id);
                    table.CheckConstraint("ck_journey_change_versions_version", "version >= 0");
                    table.ForeignKey(
                        name: "FK_journey_change_versions_journeys_journey_id",
                        column: x => x.journey_id,
                        principalSchema: "network",
                        principalTable: "journeys",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "service_changes",
                schema: "changes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    journey_id = table.Column<Guid>(type: "uuid", nullable: false),
                    journey_version = table.Column<int>(type: "integer", nullable: false),
                    action = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    delay_minutes = table.Column<int>(type: "integer", nullable: true),
                    new_platform = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    target_stop_id = table.Column<Guid>(type: "uuid", nullable: true),
                    final_stop_id = table.Column<Guid>(type: "uuid", nullable: true),
                    supersedes_change_id = table.Column<Guid>(type: "uuid", nullable: true),
                    published_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_changes", x => x.id);
                    table.CheckConstraint("ck_service_changes_action", "action IN ('PUBLISH', 'WITHDRAW')");
                    table.CheckConstraint("ck_service_changes_fields", "(action = 'PUBLISH' AND supersedes_change_id IS NULL AND (\n    (kind = 'DELAY' AND delay_minutes >= 0 AND new_platform IS NULL AND target_stop_id IS NULL AND final_stop_id IS NULL)\n OR (kind = 'CANCEL' AND delay_minutes IS NULL AND new_platform IS NULL AND target_stop_id IS NULL AND final_stop_id IS NULL)\n OR (kind = 'PLATFORM' AND delay_minutes IS NULL AND btrim(new_platform) <> '' AND target_stop_id IS NOT NULL AND final_stop_id IS NULL)\n OR (kind = 'EARLY_END' AND delay_minutes IS NULL AND new_platform IS NULL AND target_stop_id IS NULL AND final_stop_id IS NOT NULL)))\nOR (action = 'WITHDRAW' AND supersedes_change_id IS NOT NULL\n    AND delay_minutes IS NULL AND new_platform IS NULL AND target_stop_id IS NULL AND final_stop_id IS NULL)");
                    table.CheckConstraint("ck_service_changes_kind", "kind IN ('DELAY', 'CANCEL', 'PLATFORM', 'EARLY_END')");
                    table.CheckConstraint("ck_service_changes_version", "journey_version >= 1");
                    table.ForeignKey(
                        name: "FK_service_changes_accounts_published_by_id",
                        column: x => x.published_by_id,
                        principalSchema: "identity",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_service_changes_journeys_journey_id",
                        column: x => x.journey_id,
                        principalSchema: "network",
                        principalTable: "journeys",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_service_changes_pattern_stops_final_stop_id",
                        column: x => x.final_stop_id,
                        principalSchema: "network",
                        principalTable: "pattern_stops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_service_changes_pattern_stops_target_stop_id",
                        column: x => x.target_stop_id,
                        principalSchema: "network",
                        principalTable: "pattern_stops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_service_changes_service_changes_supersedes_change_id",
                        column: x => x.supersedes_change_id,
                        principalSchema: "changes",
                        principalTable: "service_changes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_service_changes_final_stop_id",
                schema: "changes",
                table: "service_changes",
                column: "final_stop_id");

            migrationBuilder.CreateIndex(
                name: "IX_service_changes_journey_id_journey_version",
                schema: "changes",
                table: "service_changes",
                columns: new[] { "journey_id", "journey_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_service_changes_published_by_id",
                schema: "changes",
                table: "service_changes",
                column: "published_by_id");

            migrationBuilder.CreateIndex(
                name: "IX_service_changes_supersedes_change_id",
                schema: "changes",
                table: "service_changes",
                column: "supersedes_change_id");

            migrationBuilder.CreateIndex(
                name: "IX_service_changes_target_stop_id",
                schema: "changes",
                table: "service_changes",
                column: "target_stop_id");

            // Every journey that already exists starts at version 0. The seeder does the same
            // for journeys added later and never resets a row that has moved on.
            migrationBuilder.Sql("""
                INSERT INTO changes.journey_change_versions (journey_id, version, updated_at)
                SELECT id, 0, now() FROM network.journeys
                ON CONFLICT (journey_id) DO NOTHING;
                """);

            // History is append-only: a row is never edited or deleted.
            migrationBuilder.Sql("""
                CREATE FUNCTION changes.reject_service_change_edits() RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION 'changes.service_changes is append-only';
                END;
                $$ LANGUAGE plpgsql;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER service_changes_append_only
                BEFORE UPDATE OR DELETE ON changes.service_changes
                FOR EACH ROW EXECUTE FUNCTION changes.reject_service_change_edits();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "journey_change_versions",
                schema: "changes");

            migrationBuilder.DropTable(
                name: "service_changes",
                schema: "changes");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS changes.reject_service_change_edits();");
        }
    }
}
