using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HiWallet.WithdrawalOrchestrator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWithdrawalSettlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_withdrawal_sagas_active",
                table: "withdrawal_sagas");

            migrationBuilder.AddColumn<decimal>(
                name: "bank_fee",
                table: "withdrawal_sagas",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bank_reference",
                table: "withdrawal_sagas",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "settlement_transaction_id",
                table: "withdrawal_sagas",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_withdrawal_sagas_active",
                table: "withdrawal_sagas",
                column: "updated_at",
                filter: "state IN ('initiated', 'debited', 'bank_transfer_pending', 'compensating', 'settling')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_withdrawal_sagas_active",
                table: "withdrawal_sagas");

            migrationBuilder.DropColumn(
                name: "bank_fee",
                table: "withdrawal_sagas");

            migrationBuilder.DropColumn(
                name: "bank_reference",
                table: "withdrawal_sagas");

            migrationBuilder.DropColumn(
                name: "settlement_transaction_id",
                table: "withdrawal_sagas");

            migrationBuilder.CreateIndex(
                name: "ix_withdrawal_sagas_active",
                table: "withdrawal_sagas",
                column: "updated_at",
                filter: "state IN ('initiated', 'debited', 'bank_transfer_pending', 'compensating')");
        }
    }
}
