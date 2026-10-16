using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartRail.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCateringOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "catering");

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "catering",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_change_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    delivery_state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.id);
                    table.CheckConstraint("ck_outbox_messages_attempts", "attempt_count >= 0");
                    table.CheckConstraint("ck_outbox_messages_state", "delivery_state IN ('PENDING', 'DELIVERED', 'FAILED')");
                    table.ForeignKey(
                        name: "FK_outbox_messages_service_changes_service_change_id",
                        column: x => x.service_change_id,
                        principalSchema: "changes",
                        principalTable: "service_changes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_delivery_state_next_attempt_at",
                schema: "catering",
                table: "outbox_messages",
                columns: new[] { "delivery_state", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_event_id",
                schema: "catering",
                table: "outbox_messages",
                column: "event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_service_change_id",
                schema: "catering",
                table: "outbox_messages",
                column: "service_change_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "catering");
        }
    }
}
