using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HiWallet.Bank.Fake.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialBankFake : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "transfer_scenarios",
                columns: table => new
                {
                    client_reference = table.Column<string>(type: "text", nullable: false),
                    outcome = table.Column<string>(type: "text", nullable: false),
                    remaining_transient_failures = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    delay_milliseconds = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transfer_scenarios", x => x.client_reference);
                });

            migrationBuilder.CreateTable(
                name: "transfers",
                columns: table => new
                {
                    bank_reference = table.Column<string>(type: "text", nullable: false),
                    client_reference = table.Column<string>(type: "text", nullable: false),
                    idempotency_key = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    currency = table.Column<string>(type: "char(3)", nullable: false),
                    destination_iban = table.Column<string>(type: "text", nullable: false),
                    outcome = table.Column<string>(type: "text", nullable: false),
                    resolve_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fee = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    callback_sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    callback_attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_callback_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transfers", x => x.bank_reference);
                });

            migrationBuilder.CreateIndex(
                name: "ix_transfers_pending_callback",
                table: "transfers",
                column: "resolve_at",
                filter: "callback_sent_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_transfers_idempotency",
                table: "transfers",
                column: "idempotency_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "transfer_scenarios");

            migrationBuilder.DropTable(
                name: "transfers");
        }
    }
}
