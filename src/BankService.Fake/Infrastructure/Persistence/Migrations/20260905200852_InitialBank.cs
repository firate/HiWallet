using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HiWallet.BankService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialBank : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bank_transfers",
                columns: table => new
                {
                    command_id = table.Column<Guid>(type: "uuid", nullable: false),
                    saga_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    currency = table.Column<string>(type: "char(3)", nullable: false),
                    destination_iban = table.Column<string>(type: "text", nullable: false),
                    outcome = table.Column<string>(type: "text", nullable: false),
                    bank_reference = table.Column<string>(type: "text", nullable: true),
                    failure_reason = table.Column<string>(type: "text", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reply_routing_key = table.Column<string>(type: "text", nullable: false),
                    reply_payload = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_transfers", x => x.command_id);
                });

            migrationBuilder.CreateTable(
                name: "transfer_scenarios",
                columns: table => new
                {
                    saga_id = table.Column<Guid>(type: "uuid", nullable: false),
                    outcome = table.Column<string>(type: "text", nullable: false),
                    remaining_transient_failures = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    delay_milliseconds = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transfer_scenarios", x => x.saga_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bank_transfers_saga",
                table: "bank_transfers",
                column: "saga_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bank_transfers");

            migrationBuilder.DropTable(
                name: "transfer_scenarios");
        }
    }
}
