using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace HiWallet.WalletService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFundType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ledger_entries_ledger_account",
                table: "ledger_entries");

            migrationBuilder.DropPrimaryKey(
                name: "pk_ledger_balances",
                table: "ledger_balances");

            migrationBuilder.AddColumn<string>(
                name: "fund_type",
                table: "ledger_entries",
                type: "text",
                nullable: false,
                // Mevcut satirlarin tamami cash: bu kolon eklenene kadar para
                // ayrismiyordu ve hepsi cekilebilir muamelesi goruyordu. Kolon
                // varsayilani hemen asagida kaldiriliyor.
                defaultValue: "cash");

            migrationBuilder.AddColumn<string>(
                name: "fund_type",
                table: "ledger_balances",
                type: "text",
                nullable: false,
                defaultValue: "cash");

            // Backfill bitti. Varsayilan kaldiriliyor: fund_type'i yazmayi unutan bir
            // yazma yolu sessizce cash'e dusmemeli, patlamali (decisions.md madde 36).
            migrationBuilder.Sql("ALTER TABLE ledger_entries ALTER COLUMN fund_type DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE ledger_balances ALTER COLUMN fund_type DROP DEFAULT;");

            migrationBuilder.AddPrimaryKey(
                name: "pk_ledger_balances",
                table: "ledger_balances",
                columns: new[] { "ledger_account_id", "fund_type" });

            migrationBuilder.InsertData(
                table: "ledger_balances",
                columns: new[] { "fund_type", "ledger_account_id", "currency", "updated_at" },
                values: new object[,]
                {
                    { "card", new Guid("a0000000-0000-4000-8000-000000000001"), "TRY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "promo", new Guid("a0000000-0000-4000-8000-000000000001"), "TRY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "card", new Guid("a0000000-0000-4000-8000-000000000002"), "TRY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "promo", new Guid("a0000000-0000-4000-8000-000000000002"), "TRY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "card", new Guid("a0000000-0000-4000-8000-000000000003"), "TRY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "promo", new Guid("a0000000-0000-4000-8000-000000000003"), "TRY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "card", new Guid("a0000000-0000-4000-8000-000000000004"), "TRY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "promo", new Guid("a0000000-0000-4000-8000-000000000004"), "TRY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "card", new Guid("a0000000-0000-4000-8000-000000000005"), "TRY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "promo", new Guid("a0000000-0000-4000-8000-000000000005"), "TRY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "card", new Guid("a0000000-0000-4000-8000-000000000006"), "TRY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "promo", new Guid("a0000000-0000-4000-8000-000000000006"), "TRY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) }
                });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_ledger_account",
                table: "ledger_entries",
                columns: new[] { "ledger_account_id", "fund_type", "id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_ledger_entries_fund_type",
                table: "ledger_entries",
                sql: "fund_type IN ('cash','card','promo')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ledger_balances_fund_type",
                table: "ledger_balances",
                sql: "fund_type IN ('cash','card','promo')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ledger_entries_ledger_account",
                table: "ledger_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ledger_entries_fund_type",
                table: "ledger_entries");

            migrationBuilder.DropPrimaryKey(
                name: "pk_ledger_balances",
                table: "ledger_balances");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ledger_balances_fund_type",
                table: "ledger_balances");

            migrationBuilder.DeleteData(
                table: "ledger_balances",
                keyColumns: new[] { "fund_type", "ledger_account_id" },
                keyColumnTypes: new[] { "text", "uuid" },
                keyValues: new object[] { "card", new Guid("a0000000-0000-4000-8000-000000000001") });

            migrationBuilder.DeleteData(
                table: "ledger_balances",
                keyColumns: new[] { "fund_type", "ledger_account_id" },
                keyColumnTypes: new[] { "text", "uuid" },
                keyValues: new object[] { "cash", new Guid("a0000000-0000-4000-8000-000000000001") });

            migrationBuilder.DeleteData(
                table: "ledger_balances",
                keyColumns: new[] { "fund_type", "ledger_account_id" },
                keyColumnTypes: new[] { "text", "uuid" },
                keyValues: new object[] { "promo", new Guid("a0000000-0000-4000-8000-000000000001") });

            migrationBuilder.DeleteData(
                table: "ledger_balances",
                keyColumns: new[] { "fund_type", "ledger_account_id" },
                keyColumnTypes: new[] { "text", "uuid" },
                keyValues: new object[] { "card", new Guid("a0000000-0000-4000-8000-000000000002") });

            migrationBuilder.DeleteData(
                table: "ledger_balances",
                keyColumns: new[] { "fund_type", "ledger_account_id" },
                keyColumnTypes: new[] { "text", "uuid" },
                keyValues: new object[] { "cash", new Guid("a0000000-0000-4000-8000-000000000002") });

            migrationBuilder.DeleteData(
                table: "ledger_balances",
                keyColumns: new[] { "fund_type", "ledger_account_id" },
                keyColumnTypes: new[] { "text", "uuid" },
                keyValues: new object[] { "promo", new Guid("a0000000-0000-4000-8000-000000000002") });

            migrationBuilder.DeleteData(
                table: "ledger_balances",
                keyColumns: new[] { "fund_type", "ledger_account_id" },
                keyColumnTypes: new[] { "text", "uuid" },
                keyValues: new object[] { "card", new Guid("a0000000-0000-4000-8000-000000000003") });

            migrationBuilder.DeleteData(
                table: "ledger_balances",
                keyColumns: new[] { "fund_type", "ledger_account_id" },
                keyColumnTypes: new[] { "text", "uuid" },
                keyValues: new object[] { "cash", new Guid("a0000000-0000-4000-8000-000000000003") });

            migrationBuilder.DeleteData(
                table: "ledger_balances",
                keyColumns: new[] { "fund_type", "ledger_account_id" },
                keyColumnTypes: new[] { "text", "uuid" },
                keyValues: new object[] { "promo", new Guid("a0000000-0000-4000-8000-000000000003") });

            migrationBuilder.DeleteData(
                table: "ledger_balances",
                keyColumns: new[] { "fund_type", "ledger_account_id" },
                keyColumnTypes: new[] { "text", "uuid" },
                keyValues: new object[] { "card", new Guid("a0000000-0000-4000-8000-000000000004") });

            migrationBuilder.DeleteData(
                table: "ledger_balances",
                keyColumns: new[] { "fund_type", "ledger_account_id" },
                keyColumnTypes: new[] { "text", "uuid" },
                keyValues: new object[] { "cash", new Guid("a0000000-0000-4000-8000-000000000004") });

            migrationBuilder.DeleteData(
                table: "ledger_balances",
                keyColumns: new[] { "fund_type", "ledger_account_id" },
                keyColumnTypes: new[] { "text", "uuid" },
                keyValues: new object[] { "promo", new Guid("a0000000-0000-4000-8000-000000000004") });

            migrationBuilder.DeleteData(
                table: "ledger_balances",
                keyColumns: new[] { "fund_type", "ledger_account_id" },
                keyColumnTypes: new[] { "text", "uuid" },
                keyValues: new object[] { "card", new Guid("a0000000-0000-4000-8000-000000000005") });

            migrationBuilder.DeleteData(
                table: "ledger_balances",
                keyColumns: new[] { "fund_type", "ledger_account_id" },
                keyColumnTypes: new[] { "text", "uuid" },
                keyValues: new object[] { "cash", new Guid("a0000000-0000-4000-8000-000000000005") });

            migrationBuilder.DeleteData(
                table: "ledger_balances",
                keyColumns: new[] { "fund_type", "ledger_account_id" },
                keyColumnTypes: new[] { "text", "uuid" },
                keyValues: new object[] { "promo", new Guid("a0000000-0000-4000-8000-000000000005") });

            migrationBuilder.DeleteData(
                table: "ledger_balances",
                keyColumns: new[] { "fund_type", "ledger_account_id" },
                keyColumnTypes: new[] { "text", "uuid" },
                keyValues: new object[] { "card", new Guid("a0000000-0000-4000-8000-000000000006") });

            migrationBuilder.DeleteData(
                table: "ledger_balances",
                keyColumns: new[] { "fund_type", "ledger_account_id" },
                keyColumnTypes: new[] { "text", "uuid" },
                keyValues: new object[] { "cash", new Guid("a0000000-0000-4000-8000-000000000006") });

            migrationBuilder.DeleteData(
                table: "ledger_balances",
                keyColumns: new[] { "fund_type", "ledger_account_id" },
                keyColumnTypes: new[] { "text", "uuid" },
                keyValues: new object[] { "promo", new Guid("a0000000-0000-4000-8000-000000000006") });

            migrationBuilder.DropColumn(
                name: "fund_type",
                table: "ledger_entries");

            migrationBuilder.DropColumn(
                name: "fund_type",
                table: "ledger_balances");

            migrationBuilder.AddPrimaryKey(
                name: "pk_ledger_balances",
                table: "ledger_balances",
                column: "ledger_account_id");

            migrationBuilder.InsertData(
                table: "ledger_balances",
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

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_ledger_account",
                table: "ledger_entries",
                columns: new[] { "ledger_account_id", "id" });
        }
    }
}
