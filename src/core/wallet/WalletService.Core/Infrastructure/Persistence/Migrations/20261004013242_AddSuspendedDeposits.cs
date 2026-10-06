using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace HiWallet.WalletService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSuspendedDeposits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_ledger_accounts_provider",
                table: "ledger_accounts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ledger_accounts_type",
                table: "ledger_accounts");

            migrationBuilder.CreateTable(
                name: "suspended_deposits",
                columns: table => new
                {
                    ledger_transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "text", nullable: false),
                    bank_reference = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    currency = table.Column<string>(type: "char(3)", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_suspended_deposits", x => x.ledger_transaction_id);
                    table.CheckConstraint("ck_suspended_deposits_amount", "amount > 0");
                    table.CheckConstraint("ck_suspended_deposits_reason", "reason IN ('no_account_number','ambiguous_account_number','unknown_account','business_account','no_wallet_in_currency','unknown_sender','sender_not_holder','limit_exceeded')");
                    table.ForeignKey(
                        name: "fk_suspended_deposits_account",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_suspended_deposits_transaction",
                        column: x => x.ledger_transaction_id,
                        principalTable: "ledger_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "ledger_accounts",
                columns: new[] { "id", "account_id", "created_at", "currency", "name", "provider", "type" },
                values: new object[] { new Guid("a0000000-0000-4000-8000-000000000009"), null, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "TRY", null, "bank-fake", "suspense" });

            migrationBuilder.InsertData(
                table: "ledger_balances",
                columns: new[] { "fund_type", "ledger_account_id", "currency", "updated_at" },
                values: new object[,]
                {
                    { "card", new Guid("a0000000-0000-4000-8000-000000000009"), "TRY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "cash", new Guid("a0000000-0000-4000-8000-000000000009"), "TRY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "promo", new Guid("a0000000-0000-4000-8000-000000000009"), "TRY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) }
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_ledger_accounts_provider",
                table: "ledger_accounts",
                sql: "(type IN ('clearing','nostro','provider_expense','suspense')) = (provider IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ledger_accounts_type",
                table: "ledger_accounts",
                sql: "type IN ('user_wallet','clearing','revenue','nostro','provider_expense','promo_expense','promo_breakage','suspense')");

            migrationBuilder.CreateIndex(
                name: "ix_suspended_deposits_account",
                table: "suspended_deposits",
                column: "account_id",
                filter: "account_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_suspended_deposits_created",
                table: "suspended_deposits",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ux_suspended_deposits_reference",
                table: "suspended_deposits",
                columns: new[] { "provider", "bank_reference" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "suspended_deposits");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ledger_accounts_provider",
                table: "ledger_accounts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ledger_accounts_type",
                table: "ledger_accounts");

            migrationBuilder.DeleteData(
                table: "ledger_balances",
                keyColumns: new[] { "fund_type", "ledger_account_id" },
                keyValues: new object[] { "card", new Guid("a0000000-0000-4000-8000-000000000009") });

            migrationBuilder.DeleteData(
                table: "ledger_balances",
                keyColumns: new[] { "fund_type", "ledger_account_id" },
                keyValues: new object[] { "cash", new Guid("a0000000-0000-4000-8000-000000000009") });

            migrationBuilder.DeleteData(
                table: "ledger_balances",
                keyColumns: new[] { "fund_type", "ledger_account_id" },
                keyValues: new object[] { "promo", new Guid("a0000000-0000-4000-8000-000000000009") });

            migrationBuilder.DeleteData(
                table: "ledger_accounts",
                keyColumn: "id",
                keyValue: new Guid("a0000000-0000-4000-8000-000000000009"));

            migrationBuilder.AddCheckConstraint(
                name: "ck_ledger_accounts_provider",
                table: "ledger_accounts",
                sql: "(type IN ('clearing','nostro','provider_expense')) = (provider IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ledger_accounts_type",
                table: "ledger_accounts",
                sql: "type IN ('user_wallet','clearing','revenue','nostro','provider_expense','promo_expense','promo_breakage')");
        }
    }
}
