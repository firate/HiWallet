using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HiWallet.WalletService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "provider_invoices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "text", nullable: false),
                    invoice_ref = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    expected_amount = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    currency = table.Column<string>(type: "char(3)", fixedLength: true, nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    fee_count = table.Column<int>(type: "integer", nullable: false),
                    ledger_tx_id = table.Column<Guid>(type: "uuid", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provider_invoices", x => x.id);
                    table.CheckConstraint("ck_provider_invoices_status", "status IN ('applied','pending_review')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_provider_invoices_pending",
                table: "provider_invoices",
                column: "received_at",
                filter: "status = 'pending_review'");

            migrationBuilder.CreateIndex(
                name: "ux_provider_invoices_ref",
                table: "provider_invoices",
                columns: new[] { "provider", "invoice_ref" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "provider_invoices");
        }
    }
}
