using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HiWallet.WithdrawalOrchestrator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialWithdrawalSagas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "withdrawal_outbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    saga_id = table.Column<Guid>(type: "uuid", nullable: false),
                    routing_key = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    publish_attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_withdrawal_outbox", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "withdrawal_sagas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    wallet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    currency = table.Column<string>(type: "char(3)", nullable: false),
                    destination_iban = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    total_debited = table.Column<decimal>(type: "numeric(19,4)", nullable: true),
                    debit_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    refund_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    bank_command_id = table.Column<Guid>(type: "uuid", nullable: true),
                    failure_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_withdrawal_sagas", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_withdrawal_outbox_saga",
                table: "withdrawal_outbox",
                column: "saga_id");

            migrationBuilder.CreateIndex(
                name: "ix_withdrawal_outbox_unpublished",
                table: "withdrawal_outbox",
                column: "created_at",
                filter: "published_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_withdrawal_sagas_active",
                table: "withdrawal_sagas",
                column: "updated_at",
                filter: "state IN ('initiated', 'debited', 'bank_transfer_pending', 'compensating')");

            migrationBuilder.CreateIndex(
                name: "ix_withdrawal_sagas_wallet",
                table: "withdrawal_sagas",
                columns: new[] { "wallet_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_withdrawal_sagas_idempotency",
                table: "withdrawal_sagas",
                columns: new[] { "account_id", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "withdrawal_outbox");

            migrationBuilder.DropTable(
                name: "withdrawal_sagas");
        }
    }
}
