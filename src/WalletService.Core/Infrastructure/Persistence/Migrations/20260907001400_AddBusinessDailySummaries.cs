using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HiWallet.WalletService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessDailySummaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // GRANT yok, gerekmiyor: GrantAppPrivileges migration'ı ALTER DEFAULT
            // PRIVILEGES kurdu, bu tablo da wallet_owner tarafından yaratıldığı için
            // wallet_app'e otomatik yetkileniyor (decisions.md madde 24).
            //
            // ledger_entries'teki REVOKE UPDATE, DELETE buraya UYGULANMIYOR ve
            // uygulanmamalı: bu tablo ledger değil, türetilmiş rapor — job onu her
            // turda üzerine yazıyor.
            migrationBuilder.CreateTable(
                name: "business_daily_summaries",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    currency = table.Column<string>(type: "char(3)", fixedLength: true, nullable: false),
                    transaction_count = table.Column<int>(type: "integer", nullable: false),
                    volume = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    commission = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    calculated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_business_daily_summaries", x => new { x.account_id, x.day, x.currency });
                });

            migrationBuilder.CreateIndex(
                name: "ix_business_daily_summaries_day",
                table: "business_daily_summaries",
                column: "day");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "business_daily_summaries");
        }
    }
}
