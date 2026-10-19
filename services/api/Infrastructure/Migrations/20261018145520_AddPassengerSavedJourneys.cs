using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartRail.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPassengerSavedJourneys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "passenger");

            migrationBuilder.CreateTable(
                name: "followed_journeys",
                schema: "passenger",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    journey_id = table.Column<Guid>(type: "uuid", nullable: false),
                    followed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_followed_journeys", x => x.id);
                    table.ForeignKey(
                        name: "FK_followed_journeys_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "identity",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_followed_journeys_journeys_journey_id",
                        column: x => x.journey_id,
                        principalSchema: "network",
                        principalTable: "journeys",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "saved_routes",
                schema: "passenger",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origin_station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destination_station_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_saved_routes", x => x.id);
                    table.ForeignKey(
                        name: "FK_saved_routes_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "identity",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_saved_routes_stations_destination_station_id",
                        column: x => x.destination_station_id,
                        principalSchema: "network",
                        principalTable: "stations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_saved_routes_stations_origin_station_id",
                        column: x => x.origin_station_id,
                        principalSchema: "network",
                        principalTable: "stations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_followed_journeys_account_id_journey_id",
                schema: "passenger",
                table: "followed_journeys",
                columns: new[] { "account_id", "journey_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_followed_journeys_journey_id",
                schema: "passenger",
                table: "followed_journeys",
                column: "journey_id");

            migrationBuilder.CreateIndex(
                name: "IX_saved_routes_account_id_origin_station_id_destination_stati~",
                schema: "passenger",
                table: "saved_routes",
                columns: new[] { "account_id", "origin_station_id", "destination_station_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_saved_routes_destination_station_id",
                schema: "passenger",
                table: "saved_routes",
                column: "destination_station_id");

            migrationBuilder.CreateIndex(
                name: "IX_saved_routes_origin_station_id",
                schema: "passenger",
                table: "saved_routes",
                column: "origin_station_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "followed_journeys",
                schema: "passenger");

            migrationBuilder.DropTable(
                name: "saved_routes",
                schema: "passenger");
        }
    }
}
