using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartRail.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDriverReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "driver_reports",
                schema: "changes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    journey_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submitted_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    acknowledged_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_driver_reports", x => x.id);
                    table.CheckConstraint("ck_driver_reports_acknowledgement", "(acknowledged_by_id IS NULL) = (acknowledged_at IS NULL)");
                    table.CheckConstraint("ck_driver_reports_reason", "reason_code IN ('TRAIN_FAULT', 'SIGNAL_DELAY', 'OBSTRUCTION', 'CANNOT_CONTINUE')");
                    table.ForeignKey(
                        name: "FK_driver_reports_accounts_acknowledged_by_id",
                        column: x => x.acknowledged_by_id,
                        principalSchema: "identity",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_driver_reports_accounts_submitted_by_id",
                        column: x => x.submitted_by_id,
                        principalSchema: "identity",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_driver_reports_journeys_journey_id",
                        column: x => x.journey_id,
                        principalSchema: "network",
                        principalTable: "journeys",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_driver_reports_acknowledged_by_id",
                schema: "changes",
                table: "driver_reports",
                column: "acknowledged_by_id");

            migrationBuilder.CreateIndex(
                name: "IX_driver_reports_journey_id_submitted_by_id_received_at_id",
                schema: "changes",
                table: "driver_reports",
                columns: new[] { "journey_id", "submitted_by_id", "received_at", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_driver_reports_submitted_by_id",
                schema: "changes",
                table: "driver_reports",
                column: "submitted_by_id");

            migrationBuilder.CreateIndex(
                name: "ux_driver_reports_client_report_id",
                schema: "changes",
                table: "driver_reports",
                column: "client_report_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "driver_reports",
                schema: "changes");
        }
    }
}
