using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HiWallet.WithdrawalOrchestrator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWithdrawalReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_withdrawal_sagas_active",
                table: "withdrawal_sagas");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reviewed_at",
                table: "withdrawal_sagas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reviewed_by",
                table: "withdrawal_sagas",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_withdrawal_sagas_active",
                table: "withdrawal_sagas",
                column: "updated_at",
                filter: "state IN ('initiated', 'debited', 'bank_transfer_pending', 'compensating', 'settling', 'under_review', 'cancelling')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_withdrawal_sagas_active",
                table: "withdrawal_sagas");

            migrationBuilder.DropColumn(
                name: "reviewed_at",
                table: "withdrawal_sagas");

            migrationBuilder.DropColumn(
                name: "reviewed_by",
                table: "withdrawal_sagas");

            migrationBuilder.CreateIndex(
                name: "ix_withdrawal_sagas_active",
                table: "withdrawal_sagas",
                column: "updated_at",
                filter: "state IN ('initiated', 'debited', 'bank_transfer_pending', 'compensating', 'settling')");
        }
    }
}
