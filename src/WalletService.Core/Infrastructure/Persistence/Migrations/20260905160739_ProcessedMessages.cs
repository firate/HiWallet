using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HiWallet.WalletService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProcessedMessages : Migration
    {
        // GRANT yok, gerekmiyor: GrantAppPrivileges migration'ı ALTER DEFAULT
        // PRIVILEGES kuruyor, yani wallet_owner'ın bundan SONRA açtığı tablolar
        // wallet_app'e otomatik yetkileniyor (decisions.md madde 24). Elle GRANT
        // eklenseydi iki yerde yetki tanımı olurdu.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "processed_messages",
                columns: table => new
                {
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_type = table.Column<string>(type: "text", nullable: false),
                    saga_id = table.Column<Guid>(type: "uuid", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ledger_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reply_routing_key = table.Column<string>(type: "text", nullable: false),
                    reply_payload = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processed_messages", x => x.message_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_processed_messages_saga",
                table: "processed_messages",
                column: "saga_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "processed_messages");
        }
    }
}
