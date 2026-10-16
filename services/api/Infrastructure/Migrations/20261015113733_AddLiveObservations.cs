using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartRail.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLiveObservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "live");

            migrationBuilder.CreateTable(
                name: "observations",
                schema: "live",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    journey_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_event_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    from_stop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_stop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    progress_permille = table.Column<int>(type: "integer", nullable: false),
                    reported_delay_seconds = table.Column<int>(type: "integer", nullable: false),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_observations", x => x.id);
                    table.CheckConstraint("ck_observations_delay", "reported_delay_seconds >= 0");
                    table.CheckConstraint("ck_observations_progress", "progress_permille BETWEEN 0 AND 1000");
                    table.ForeignKey(
                        name: "FK_observations_journeys_journey_id",
                        column: x => x.journey_id,
                        principalSchema: "network",
                        principalTable: "journeys",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_observations_pattern_stops_from_stop_id",
                        column: x => x.from_stop_id,
                        principalSchema: "network",
                        principalTable: "pattern_stops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_observations_pattern_stops_to_stop_id",
                        column: x => x.to_stop_id,
                        principalSchema: "network",
                        principalTable: "pattern_stops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_observations_from_stop_id",
                schema: "live",
                table: "observations",
                column: "from_stop_id");

            migrationBuilder.CreateIndex(
                name: "IX_observations_journey_id_observed_at",
                schema: "live",
                table: "observations",
                columns: new[] { "journey_id", "observed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_observations_source_event_id",
                schema: "live",
                table: "observations",
                column: "source_event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_observations_to_stop_id",
                schema: "live",
                table: "observations",
                column: "to_stop_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "observations",
                schema: "live");
        }
    }
}
