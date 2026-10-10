using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HiWallet.WalletService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDepositReturnSteps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_suspended_deposit_resolutions",
                table: "suspended_deposit_resolutions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_suspended_deposit_resolutions_kind",
                table: "suspended_deposit_resolutions");

            migrationBuilder.AlterColumn<Guid>(
                name: "account_id",
                table: "suspended_deposit_resolutions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            // Var olan satırların her biri havalenin tek ve ilk adımı (aktarım): sıra 1. Varsayılan
            // hemen kaldırılıyor; sırası verilmemiş bir adım sessizce 1 olmasın.
            migrationBuilder.AddColumn<int>(
                name: "seq",
                table: "suspended_deposit_resolutions",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.Sql("ALTER TABLE suspended_deposit_resolutions ALTER COLUMN seq DROP DEFAULT;");

            migrationBuilder.AddPrimaryKey(
                name: "pk_suspended_deposit_resolutions",
                table: "suspended_deposit_resolutions",
                columns: new[] { "suspended_deposit_id", "seq" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_suspended_deposit_resolutions_account",
                table: "suspended_deposit_resolutions",
                sql: "(kind = 'moved') = (account_id IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_suspended_deposit_resolutions_kind",
                table: "suspended_deposit_resolutions",
                sql: "kind IN ('moved','return_started','returned','return_failed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_suspended_deposit_resolutions_seq",
                table: "suspended_deposit_resolutions",
                sql: "seq > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_suspended_deposit_resolutions",
                table: "suspended_deposit_resolutions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_suspended_deposit_resolutions_account",
                table: "suspended_deposit_resolutions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_suspended_deposit_resolutions_kind",
                table: "suspended_deposit_resolutions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_suspended_deposit_resolutions_seq",
                table: "suspended_deposit_resolutions");

            migrationBuilder.DropColumn(
                name: "seq",
                table: "suspended_deposit_resolutions");

            migrationBuilder.AlterColumn<Guid>(
                name: "account_id",
                table: "suspended_deposit_resolutions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "pk_suspended_deposit_resolutions",
                table: "suspended_deposit_resolutions",
                column: "suspended_deposit_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_suspended_deposit_resolutions_kind",
                table: "suspended_deposit_resolutions",
                sql: "kind IN ('moved')");
        }
    }
}
