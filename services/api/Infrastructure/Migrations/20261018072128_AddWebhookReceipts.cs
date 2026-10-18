using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartRail.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhookReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "webhook_receipts",
                schema: "catering",
                columns: table => new
                {
                    provider_event_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    order_link_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status_sequence = table.Column<int>(type: "integer", nullable: false),
                    content_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_receipts", x => x.provider_event_id);
                    table.ForeignKey(
                        name: "FK_webhook_receipts_order_links_order_link_id",
                        column: x => x.order_link_id,
                        principalSchema: "catering",
                        principalTable: "order_links",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_webhook_receipts_order_link_id",
                schema: "catering",
                table: "webhook_receipts",
                column: "order_link_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "webhook_receipts",
                schema: "catering");
        }
    }
}
