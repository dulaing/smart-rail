using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartRail.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialNetworkSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "network");

            migrationBuilder.CreateTable(
                name: "service_patterns",
                schema: "network",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    train_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    source_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    source_checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    operating_days_mask = table.Column<int>(type: "integer", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_patterns", x => x.id);
                    table.CheckConstraint("ck_service_patterns_effective_dates", "effective_to IS NULL OR effective_to >= effective_from");
                    table.CheckConstraint("ck_service_patterns_operating_days", "operating_days_mask BETWEEN 1 AND 127");
                    table.CheckConstraint("ck_service_patterns_published_source", "source_kind <> 'PUBLISHED' OR (source_url IS NOT NULL AND source_checked_at IS NOT NULL)");
                    table.CheckConstraint("ck_service_patterns_source_kind", "source_kind IN ('PUBLISHED', 'FICTIONAL_DEMO')");
                });

            migrationBuilder.CreateTable(
                name: "stations",
                schema: "network",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    name_en = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name_si = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name_ta = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "journeys",
                schema: "network",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pattern_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_date = table.Column<DateOnly>(type: "date", nullable: false),
                    demo_seed_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_journeys", x => x.id);
                    table.ForeignKey(
                        name: "FK_journeys_service_patterns_pattern_id",
                        column: x => x.pattern_id,
                        principalSchema: "network",
                        principalTable: "service_patterns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pattern_stops",
                schema: "network",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pattern_id = table.Column<Guid>(type: "uuid", nullable: false),
                    station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stop_sequence = table.Column<int>(type: "integer", nullable: false),
                    arrival_local = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    departure_local = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    day_offset = table.Column<int>(type: "integer", nullable: false),
                    planned_platform = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pattern_stops", x => x.id);
                    table.CheckConstraint("ck_pattern_stops_day_offset", "day_offset >= 0");
                    table.CheckConstraint("ck_pattern_stops_has_a_time", "arrival_local IS NOT NULL OR departure_local IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_pattern_stops_service_patterns_pattern_id",
                        column: x => x.pattern_id,
                        principalSchema: "network",
                        principalTable: "service_patterns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pattern_stops_stations_station_id",
                        column: x => x.station_id,
                        principalSchema: "network",
                        principalTable: "stations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_journeys_demo_seed_key",
                schema: "network",
                table: "journeys",
                column: "demo_seed_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_journeys_pattern_id_service_date",
                schema: "network",
                table: "journeys",
                columns: new[] { "pattern_id", "service_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pattern_stops_pattern_id_stop_sequence",
                schema: "network",
                table: "pattern_stops",
                columns: new[] { "pattern_id", "stop_sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pattern_stops_station_id",
                schema: "network",
                table: "pattern_stops",
                column: "station_id");

            migrationBuilder.CreateIndex(
                name: "IX_stations_code",
                schema: "network",
                table: "stations",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "journeys",
                schema: "network");

            migrationBuilder.DropTable(
                name: "pattern_stops",
                schema: "network");

            migrationBuilder.DropTable(
                name: "service_patterns",
                schema: "network");

            migrationBuilder.DropTable(
                name: "stations",
                schema: "network");
        }
    }
}
