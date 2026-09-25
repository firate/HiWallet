using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace HiWallet.WalletService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformPromoAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_ledger_accounts_type",
                table: "ledger_accounts");

            migrationBuilder.AddColumn<bool>(
                name: "accepts_promo",
                table: "accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.InsertData(
                table: "ledger_accounts",
                columns: new[] { "id", "account_id", "created_at", "currency", "name", "provider", "type" },
                values: new object[,]
                {
                    { new Guid("a0000000-0000-4000-8000-000000000007"), null, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "TRY", null, null, "promo_expense" },
                    { new Guid("a0000000-0000-4000-8000-000000000008"), null, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "TRY", null, null, "promo_breakage" }
                });

            migrationBuilder.InsertData(
                table: "ledger_balances",
                columns: new[] { "fund_type", "ledger_account_id", "currency", "updated_at" },
                values: new object[,]
                {
                    { "card", new Guid("a0000000-0000-4000-8000-000000000007"), "TRY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "cash", new Guid("a0000000-0000-4000-8000-000000000007"), "TRY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "promo", new Guid("a0000000-0000-4000-8000-000000000007"), "TRY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "card", new Guid("a0000000-0000-4000-8000-000000000008"), "TRY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "cash", new Guid("a0000000-0000-4000-8000-000000000008"), "TRY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "promo", new Guid("a0000000-0000-4000-8000-000000000008"), "TRY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) }
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_ledger_accounts_type",
                table: "ledger_accounts",
                sql: "type IN ('user_wallet','clearing','revenue','nostro','provider_expense','promo_expense','promo_breakage')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_ledger_accounts_type",
                table: "ledger_accounts");

            migrationBuilder.DeleteData(
                table: "ledger_balances",
                keyColumns: new[] { "fund_type", "ledger_account_id" },
                keyValues: new object[] { "card", new Guid("a0000000-0000-4000-8000-000000000007") });

            migrationBuilder.DeleteData(
                table: "ledger_balances",
                keyColumns: new[] { "fund_type", "ledger_account_id" },
                keyValues: new object[] { "cash", new Guid("a0000000-0000-4000-8000-000000000007") });

            migrationBuilder.DeleteData(
                table: "ledger_balances",
                keyColumns: new[] { "fund_type", "ledger_account_id" },
                keyValues: new object[] { "promo", new Guid("a0000000-0000-4000-8000-000000000007") });

            migrationBuilder.DeleteData(
                table: "ledger_balances",
                keyColumns: new[] { "fund_type", "ledger_account_id" },
                keyValues: new object[] { "card", new Guid("a0000000-0000-4000-8000-000000000008") });

            migrationBuilder.DeleteData(
                table: "ledger_balances",
                keyColumns: new[] { "fund_type", "ledger_account_id" },
                keyValues: new object[] { "cash", new Guid("a0000000-0000-4000-8000-000000000008") });

            migrationBuilder.DeleteData(
                table: "ledger_balances",
                keyColumns: new[] { "fund_type", "ledger_account_id" },
                keyValues: new object[] { "promo", new Guid("a0000000-0000-4000-8000-000000000008") });

            migrationBuilder.DeleteData(
                table: "ledger_accounts",
                keyColumn: "id",
                keyValue: new Guid("a0000000-0000-4000-8000-000000000007"));

            migrationBuilder.DeleteData(
                table: "ledger_accounts",
                keyColumn: "id",
                keyValue: new Guid("a0000000-0000-4000-8000-000000000008"));

            migrationBuilder.DropColumn(
                name: "accepts_promo",
                table: "accounts");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ledger_accounts_type",
                table: "ledger_accounts",
                sql: "type IN ('user_wallet','clearing','revenue','nostro','provider_expense')");
        }
    }
}
