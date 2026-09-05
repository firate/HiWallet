using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace HiWallet.WalletService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameLedgerBalances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EF rename ALGILAYAMIYOR: model'de tablo adı değişince DropTable + CreateTable
            // üretti ve "veri kaybı olabilir" diye uyardı. Gerçek bir kurulumda bu tüm
            // bakiyeleri silerdi. Elle RenameTable'a çevrildi — EF'in bilinen sınırı,
            // structure.md'nin "migration elle düzenlenmez" kuralının istisnası
            // (trigger/REVOKE ile aynı gerekçe: EF'in karşılığı yok).
            migrationBuilder.RenameTable(
                name: "wallet_balances",
                newName: "ledger_balances");

            migrationBuilder.RenameIndex(
                name: "ix_wallet_balances_ledger_account_currency",
                newName: "ix_ledger_balances_ledger_account_currency",
                table: "ledger_balances");

            // Constraint yeniden adlandırmanın EF karşılığı yok; DropPrimaryKey +
            // AddPrimaryKey index'i baştan kurardı (büyük tabloda tam yeniden yazım).
            // ALTER ... RENAME CONSTRAINT anlık ve veriye dokunmuyor.
            migrationBuilder.Sql("""
                ALTER TABLE ledger_balances RENAME CONSTRAINT pk_wallet_balances TO pk_ledger_balances;
                ALTER TABLE ledger_balances RENAME CONSTRAINT fk_wallet_balances_ledger_account
                                            TO fk_ledger_balances_ledger_account;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE ledger_balances RENAME CONSTRAINT fk_ledger_balances_ledger_account
                                            TO fk_wallet_balances_ledger_account;
                ALTER TABLE ledger_balances RENAME CONSTRAINT pk_ledger_balances TO pk_wallet_balances;
                """);

            migrationBuilder.RenameIndex(
                name: "ix_ledger_balances_ledger_account_currency",
                newName: "ix_wallet_balances_ledger_account_currency",
                table: "ledger_balances");

            migrationBuilder.RenameTable(
                name: "ledger_balances",
                newName: "wallet_balances");
        }
    }
}
