using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HiWallet.WalletService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessedEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // wallet_app'e ayrıca GRANT verilmiyor: GrantAppPrivileges migration'ı
            // ALTER DEFAULT PRIVILEGES kurduğu için wallet_owner'ın bundan SONRA
            // yarattığı tablolar yetkiyi kendiliğinden alıyor. AppRolePrivilegeTests
            // bunu doğruluyor — varsayım olarak bırakılmıyor.
            migrationBuilder.CreateTable(
                name: "processed_events",
                columns: table => new
                {
                    provider = table.Column<string>(type: "text", nullable: false),
                    event_id = table.Column<string>(type: "text", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ledger_transaction_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processed_events", x => new { x.provider, x.event_id });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "processed_events");
        }
    }
}
