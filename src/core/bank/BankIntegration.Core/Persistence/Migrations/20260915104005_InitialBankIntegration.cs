using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HiWallet.BankIntegration.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialBankIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bank_callbacks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "text", nullable: false),
                    event_id = table.Column<string>(type: "text", nullable: false),
                    raw_payload = table.Column<string>(type: "text", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    process_attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_callbacks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bank_transfers",
                columns: table => new
                {
                    command_id = table.Column<Guid>(type: "uuid", nullable: false),
                    saga_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    currency = table.Column<string>(type: "char(3)", nullable: false),
                    destination_iban = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    bank_reference = table.Column<string>(type: "text", nullable: true),
                    fee = table.Column<decimal>(type: "numeric(19,4)", nullable: true),
                    failure_reason = table.Column<string>(type: "text", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_via = table.Column<string>(type: "text", nullable: true),
                    reply_routing_key = table.Column<string>(type: "text", nullable: true),
                    reply_payload = table.Column<string>(type: "text", nullable: true),
                    reply_published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    publish_attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_transfers", x => x.command_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bank_callbacks_unprocessed",
                table: "bank_callbacks",
                column: "received_at",
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_bank_callbacks_event",
                table: "bank_callbacks",
                columns: new[] { "provider", "event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bank_transfers_pending",
                table: "bank_transfers",
                column: "started_at",
                filter: "resolved_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_bank_transfers_reference",
                table: "bank_transfers",
                column: "bank_reference",
                filter: "bank_reference IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_bank_transfers_unpublished",
                table: "bank_transfers",
                column: "resolved_at",
                filter: "resolved_at IS NOT NULL AND reply_published_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bank_callbacks");

            migrationBuilder.DropTable(
                name: "bank_transfers");
        }
    }
}
