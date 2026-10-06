using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HiWallet.WalletService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCardTopupHolds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "card_topup_holds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    wallet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    currency = table.Column<string>(type: "char(3)", nullable: false),
                    provider = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_card_topup_holds", x => x.id);
                    table.CheckConstraint("ck_card_topup_holds_amount", "amount > 0");
                    table.ForeignKey(
                        name: "fk_card_topup_holds_account",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_card_topup_holds_wallet",
                        column: x => x.wallet_id,
                        principalTable: "ledger_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "card_topup_hold_closures",
                columns: table => new
                {
                    hold_id = table.Column<Guid>(type: "uuid", nullable: false),
                    outcome = table.Column<string>(type: "text", nullable: false),
                    ledger_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_card_topup_hold_closures", x => x.hold_id);
                    table.CheckConstraint("ck_card_topup_hold_closures_outcome", "outcome IN ('paid','failed')");
                    table.CheckConstraint("ck_card_topup_hold_closures_transaction", "(outcome = 'paid') = (ledger_transaction_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_card_topup_hold_closures_hold",
                        column: x => x.hold_id,
                        principalTable: "card_topup_holds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_card_topup_holds_account",
                table: "card_topup_holds",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_card_topup_holds_wallet",
                table: "card_topup_holds",
                column: "wallet_id");

            // Pay ve kapanışı değişmiyor: ödeme sonradan eklenen kapanışla bitiyor. InitialSchema'daki
            // ALTER DEFAULT PRIVILEGES yeni tablolara UPDATE/DELETE de verdi; promo tabloları gibi geri
            // alınıyor. Model'den çıkarılamadığı için AppRolePrivilegeTests koruyor.
            migrationBuilder.Sql(
                """
                REVOKE UPDATE, DELETE ON card_topup_holds, card_topup_hold_closures FROM PUBLIC;

                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'wallet_app') THEN
                        REVOKE UPDATE, DELETE
                            ON card_topup_holds, card_topup_hold_closures
                            FROM wallet_app;
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "card_topup_hold_closures");

            migrationBuilder.DropTable(
                name: "card_topup_holds");
        }
    }
}
