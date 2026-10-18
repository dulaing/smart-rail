using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartRail.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "order_links",
                schema: "catering",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    journey_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pickup_stop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_request_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    collection_point_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    request_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    request_payload = table.Column<string>(type: "jsonb", nullable: true),
                    placement_state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    rejection_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    provider_order_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    provider_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    status_sequence = table.Column<int>(type: "integer", nullable: false),
                    cancellation_reason = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    pickup_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    items = table.Column<string>(type: "jsonb", nullable: true),
                    scheduled_arrival = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    estimated_arrival = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    eta_evidence_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_links", x => x.id);
                    table.CheckConstraint("ck_order_links_sequence", "status_sequence >= 0 AND attempt_count >= 0");
                    table.CheckConstraint("ck_order_links_state", "placement_state IN ('PENDING', 'CONFIRMED', 'REJECTED')");
                    table.ForeignKey(
                        name: "FK_order_links_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "identity",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_links_journeys_journey_id",
                        column: x => x.journey_id,
                        principalSchema: "network",
                        principalTable: "journeys",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_links_pattern_stops_pickup_stop_id",
                        column: x => x.pickup_stop_id,
                        principalSchema: "network",
                        principalTable: "pattern_stops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_order_links_account_id_client_request_key",
                schema: "catering",
                table: "order_links",
                columns: new[] { "account_id", "client_request_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_links_journey_id",
                schema: "catering",
                table: "order_links",
                column: "journey_id");

            migrationBuilder.CreateIndex(
                name: "IX_order_links_pickup_stop_id",
                schema: "catering",
                table: "order_links",
                column: "pickup_stop_id");

            migrationBuilder.CreateIndex(
                name: "IX_order_links_placement_state_next_attempt_at",
                schema: "catering",
                table: "order_links",
                columns: new[] { "placement_state", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "IX_order_links_provider_order_ref",
                schema: "catering",
                table: "order_links",
                column: "provider_order_ref",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_links",
                schema: "catering");
        }
    }
}
