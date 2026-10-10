using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HiWallet.WithdrawalOrchestrator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDepositReturnSagas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "deposit_return_sagas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    suspended_deposit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by = table.Column<string>(type: "text", nullable: false),
                    idempotency_key = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", nullable: true),
                    currency = table.Column<string>(type: "char(3)", nullable: true),
                    provider = table.Column<string>(type: "text", nullable: true),
                    deposit_bank_reference = table.Column<string>(type: "text", nullable: true),
                    debit_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    bank_command_id = table.Column<Guid>(type: "uuid", nullable: true),
                    bank_reference = table.Column<string>(type: "text", nullable: true),
                    bank_fee = table.Column<decimal>(type: "numeric(19,4)", nullable: true),
                    settlement_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    restore_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    failure_reason = table.Column<string>(type: "text", nullable: true),
                    failure_rule = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_deposit_return_sagas", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_deposit_return_sagas_active",
                table: "deposit_return_sagas",
                column: "updated_at",
                filter: "state IN ('initiated', 'bank_transfer_pending', 'settling', 'restoring')");

            migrationBuilder.CreateIndex(
                name: "ux_deposit_return_sagas_idempotency",
                table: "deposit_return_sagas",
                columns: new[] { "suspended_deposit_id", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "deposit_return_sagas");
        }
    }
}
