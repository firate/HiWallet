using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace HiWallet.WalletService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedSystemAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "ledger_accounts",
                columns: new[] { "id", "account_id", "created_at", "currency", "name", "provider", "type" },
                values: new object[,]
                {
                    { new Guid("a0000000-0000-4000-8000-000000000001"), null, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "TRY", null, null, "revenue" },
                    { new Guid("a0000000-0000-4000-8000-000000000002"), null, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "TRY", null, "stripe-fake", "clearing" },
                    { new Guid("a0000000-0000-4000-8000-000000000003"), null, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "TRY", null, "bank-fake", "clearing" },
                    { new Guid("a0000000-0000-4000-8000-000000000004"), null, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "TRY", null, "bank-fake", "nostro" },
                    { new Guid("a0000000-0000-4000-8000-000000000005"), null, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "TRY", null, "stripe-fake", "provider_expense" },
                    { new Guid("a0000000-0000-4000-8000-000000000006"), null, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "TRY", null, "bank-fake", "provider_expense" }
                });

            migrationBuilder.InsertData(
                table: "wallet_balances",
                columns: new[] { "ledger_account_id", "currency", "updated_at" },
                values: new object[,]
                {
                    { new Guid("a0000000-0000-4000-8000-000000000001"), "TRY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("a0000000-0000-4000-8000-000000000002"), "TRY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("a0000000-0000-4000-8000-000000000003"), "TRY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("a0000000-0000-4000-8000-000000000004"), "TRY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("a0000000-0000-4000-8000-000000000005"), "TRY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("a0000000-0000-4000-8000-000000000006"), "TRY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "wallet_balances",
                keyColumn: "ledger_account_id",
                keyValue: new Guid("a0000000-0000-4000-8000-000000000001"));

            migrationBuilder.DeleteData(
                table: "wallet_balances",
                keyColumn: "ledger_account_id",
                keyValue: new Guid("a0000000-0000-4000-8000-000000000002"));

            migrationBuilder.DeleteData(
                table: "wallet_balances",
                keyColumn: "ledger_account_id",
                keyValue: new Guid("a0000000-0000-4000-8000-000000000003"));

            migrationBuilder.DeleteData(
                table: "wallet_balances",
                keyColumn: "ledger_account_id",
                keyValue: new Guid("a0000000-0000-4000-8000-000000000004"));

            migrationBuilder.DeleteData(
                table: "wallet_balances",
                keyColumn: "ledger_account_id",
                keyValue: new Guid("a0000000-0000-4000-8000-000000000005"));

            migrationBuilder.DeleteData(
                table: "wallet_balances",
                keyColumn: "ledger_account_id",
                keyValue: new Guid("a0000000-0000-4000-8000-000000000006"));

            migrationBuilder.DeleteData(
                table: "ledger_accounts",
                keyColumn: "id",
                keyValue: new Guid("a0000000-0000-4000-8000-000000000001"));

            migrationBuilder.DeleteData(
                table: "ledger_accounts",
                keyColumn: "id",
                keyValue: new Guid("a0000000-0000-4000-8000-000000000002"));

            migrationBuilder.DeleteData(
                table: "ledger_accounts",
                keyColumn: "id",
                keyValue: new Guid("a0000000-0000-4000-8000-000000000003"));

            migrationBuilder.DeleteData(
                table: "ledger_accounts",
                keyColumn: "id",
                keyValue: new Guid("a0000000-0000-4000-8000-000000000004"));

            migrationBuilder.DeleteData(
                table: "ledger_accounts",
                keyColumn: "id",
                keyValue: new Guid("a0000000-0000-4000-8000-000000000005"));

            migrationBuilder.DeleteData(
                table: "ledger_accounts",
                keyColumn: "id",
                keyValue: new Guid("a0000000-0000-4000-8000-000000000006"));
        }
    }
}
