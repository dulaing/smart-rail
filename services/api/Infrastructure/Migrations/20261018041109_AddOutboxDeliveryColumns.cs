using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartRail.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxDeliveryColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "delivered_at",
                schema: "catering",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_error",
                schema: "catering",
                table: "outbox_messages",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "payload_version",
                schema: "catering",
                table: "outbox_messages",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddCheckConstraint(
                name: "ck_outbox_messages_payload_version",
                schema: "catering",
                table: "outbox_messages",
                sql: "payload_version IN (1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_outbox_messages_payload_version",
                schema: "catering",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "delivered_at",
                schema: "catering",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "last_error",
                schema: "catering",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "payload_version",
                schema: "catering",
                table: "outbox_messages");
        }
    }
}
