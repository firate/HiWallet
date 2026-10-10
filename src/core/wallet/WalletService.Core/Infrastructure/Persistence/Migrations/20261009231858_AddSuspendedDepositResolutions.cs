using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HiWallet.WalletService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSuspendedDepositResolutions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "suspended_deposit_resolutions",
                columns: table => new
                {
                    suspended_deposit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    ledger_transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resolved_by = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_suspended_deposit_resolutions", x => x.suspended_deposit_id);
                    table.CheckConstraint("ck_suspended_deposit_resolutions_kind", "kind IN ('moved')");
                    table.ForeignKey(
                        name: "fk_suspended_deposit_resolutions_account",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_suspended_deposit_resolutions_deposit",
                        column: x => x.suspended_deposit_id,
                        principalTable: "suspended_deposits",
                        principalColumn: "ledger_transaction_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_suspended_deposit_resolutions_account",
                table: "suspended_deposit_resolutions",
                column: "account_id");

            // Karar değişmiyor ve silinmiyor: havale bir kez aktarılıyor, kim aktardığı kalıcı.
            // InitialSchema'daki ALTER DEFAULT PRIVILEGES yeni tablolara UPDATE/DELETE de verdi;
            // promo tabloları gibi geri alınıyor. AppRolePrivilegeTests koruyor.
            migrationBuilder.Sql(
                """
                REVOKE UPDATE, DELETE ON suspended_deposit_resolutions FROM PUBLIC;

                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'wallet_app') THEN
                        REVOKE UPDATE, DELETE ON suspended_deposit_resolutions FROM wallet_app;
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "suspended_deposit_resolutions");
        }
    }
}
