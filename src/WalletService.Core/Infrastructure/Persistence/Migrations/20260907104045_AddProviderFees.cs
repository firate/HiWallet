using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HiWallet.WalletService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderFees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "provider_fees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "text", nullable: false),
                    settlement_model = table.Column<string>(type: "text", nullable: false),
                    fee_type = table.Column<string>(type: "text", nullable: false),
                    expected_amount = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    actual_amount = table.Column<decimal>(type: "numeric(19,4)", nullable: true),
                    currency = table.Column<string>(type: "char(3)", fixedLength: true, nullable: false),
                    provider_ref = table.Column<string>(type: "text", nullable: true),
                    invoice_ref = table.Column<string>(type: "text", nullable: true),
                    ledger_tx_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provider_fees", x => x.id);
                    table.CheckConstraint("ck_provider_fees_settlement_model", "settlement_model IN ('net','invoiced')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_provider_fees_invoice",
                table: "provider_fees",
                column: "invoice_ref");

            migrationBuilder.CreateIndex(
                name: "ix_provider_fees_tx",
                table: "provider_fees",
                column: "transaction_id");

            migrationBuilder.CreateIndex(
                name: "ix_provider_fees_unbilled",
                table: "provider_fees",
                columns: new[] { "provider", "occurred_at" },
                filter: "invoice_ref IS NULL");

            // FK'lar ELLE: EF navigation açılmadığı için modelden üretilmiyorlar
            // (trigger ve REVOKE'larla aynı durum). Navigation bilerek yok —
            // ilişkiyi gezinilebilir yapmak, ledger okuyan sorguların ücret
            // tablosunu yanlışlıkla yüklemesine kapı açardı.
            //
            // processed_events ve processed_messages'ın AKSİNE burada FK var: o
            // tablolar ledger'a hiç yazılmadan da satır üretiyor (reddedilen komut),
            // ücret satırı ise her zaman gerçekleşmiş bir işlemin yanında doğuyor.
            // Hedefi olmayan bir ücret satırı bozulmadır.
            migrationBuilder.Sql(
                """
                ALTER TABLE provider_fees
                    ADD CONSTRAINT fk_provider_fees_transaction
                        FOREIGN KEY (transaction_id) REFERENCES ledger_transactions(id),
                    ADD CONSTRAINT fk_provider_fees_ledger_tx
                        FOREIGN KEY (ledger_tx_id) REFERENCES ledger_transactions(id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "provider_fees");
        }
    }
}
