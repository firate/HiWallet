using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HiWallet.BankIntegration.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDepositReturnTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "destination_iban",
                table: "bank_transfers",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<Guid>(
                name: "returns_deposit_id",
                table: "bank_transfers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_bank_transfers_returns_deposit",
                table: "bank_transfers",
                column: "returns_deposit_id",
                filter: "returns_deposit_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_bank_transfers_returns_deposit",
                table: "bank_transfers",
                column: "returns_deposit_id",
                principalTable: "bank_deposits",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_bank_transfers_returns_deposit",
                table: "bank_transfers");

            migrationBuilder.DropIndex(
                name: "ix_bank_transfers_returns_deposit",
                table: "bank_transfers");

            migrationBuilder.DropColumn(
                name: "returns_deposit_id",
                table: "bank_transfers");

            migrationBuilder.AlterColumn<string>(
                name: "destination_iban",
                table: "bank_transfers",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }
    }
}
