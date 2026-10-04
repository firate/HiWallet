using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HiWallet.BankIntegration.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBankDeposits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bank_deposits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "text", nullable: false),
                    bank_reference = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    currency = table.Column<string>(type: "char(3)", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    sender_name = table.Column<string>(type: "text", nullable: true),
                    sender_iban = table.Column<string>(type: "text", nullable: true),
                    sender_national_id = table.Column<string>(type: "text", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    discovered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    discovered_via = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    publish_attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_deposits", x => x.id);
                    table.CheckConstraint("ck_bank_deposits_via", "discovered_via IN ('callback','reconciliation')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_bank_deposits_unpublished",
                table: "bank_deposits",
                column: "discovered_at",
                filter: "published_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_bank_deposits_reference",
                table: "bank_deposits",
                columns: new[] { "provider", "bank_reference" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bank_deposits");
        }
    }
}
