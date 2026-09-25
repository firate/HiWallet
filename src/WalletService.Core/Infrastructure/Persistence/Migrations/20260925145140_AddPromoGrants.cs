using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HiWallet.WalletService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPromoGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "promo_grants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    currency = table.Column<string>(type: "char(3)", nullable: false),
                    funder = table.Column<string>(type: "text", nullable: false),
                    funder_ledger_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scope = table.Column<string>(type: "text", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ledger_transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_promo_grants", x => x.id);
                    table.CheckConstraint("ck_promo_grants_amount", "amount > 0");
                    table.CheckConstraint("ck_promo_grants_expires_at", "expires_at IS NULL OR expires_at > created_at");
                    table.CheckConstraint("ck_promo_grants_funder", "funder IN ('platform','business')");
                    table.CheckConstraint("ck_promo_grants_funder_account", "(funder = 'business') = (funder_ledger_account_id IS NOT NULL)");
                    table.CheckConstraint("ck_promo_grants_scope", "scope IN ('all_businesses','selected_businesses')");
                    table.ForeignKey(
                        name: "fk_promo_grants_funder",
                        columns: x => new { x.funder_ledger_account_id, x.currency },
                        principalTable: "ledger_accounts",
                        principalColumns: new[] { "id", "currency" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_promo_grants_ledger_account",
                        columns: x => new { x.ledger_account_id, x.currency },
                        principalTable: "ledger_accounts",
                        principalColumns: new[] { "id", "currency" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_promo_grants_transaction",
                        column: x => x.ledger_transaction_id,
                        principalTable: "ledger_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "promo_consumptions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    grant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_promo_consumptions", x => x.id);
                    table.CheckConstraint("ck_promo_consumptions_amount", "amount > 0");
                    table.ForeignKey(
                        name: "fk_promo_consumptions_grant",
                        column: x => x.grant_id,
                        principalTable: "promo_grants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_promo_consumptions_transaction",
                        column: x => x.ledger_transaction_id,
                        principalTable: "ledger_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "promo_grant_merchants",
                columns: table => new
                {
                    grant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_promo_grant_merchants", x => new { x.grant_id, x.account_id });
                    table.ForeignKey(
                        name: "fk_promo_grant_merchants_account",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_promo_grant_merchants_grant",
                        column: x => x.grant_id,
                        principalTable: "promo_grants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_promo_consumptions_transaction",
                table: "promo_consumptions",
                column: "ledger_transaction_id");

            migrationBuilder.CreateIndex(
                name: "ux_promo_consumptions_grant_tx",
                table: "promo_consumptions",
                columns: new[] { "grant_id", "ledger_transaction_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_promo_grant_merchants_account",
                table: "promo_grant_merchants",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_promo_grants_expires_at",
                table: "promo_grants",
                column: "expires_at",
                filter: "expires_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_promo_grants_funder",
                table: "promo_grants",
                columns: new[] { "funder_ledger_account_id", "currency" });

            migrationBuilder.CreateIndex(
                name: "ix_promo_grants_ledger_account",
                table: "promo_grants",
                columns: new[] { "ledger_account_id", "currency" });

            migrationBuilder.CreateIndex(
                name: "ux_promo_grants_transaction",
                table: "promo_grants",
                column: "ledger_transaction_id",
                unique: true);

            // Parti değişmiyor, tüketim eklenerek yazılıyor (decisions.md madde 37).
            // InitialSchema'daki ALTER DEFAULT PRIVILEGES yeni tablolara da UPDATE/DELETE
            // verdi; ledger_entries ile aynı şekilde geri alınıyor. Model'den
            // çıkarılamadığı için AppRolePrivilegeTests koruyor.
            migrationBuilder.Sql(
                """
                REVOKE UPDATE, DELETE ON promo_grants, promo_grant_merchants, promo_consumptions FROM PUBLIC;

                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'wallet_app') THEN
                        REVOKE UPDATE, DELETE
                            ON promo_grants, promo_grant_merchants, promo_consumptions
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
                name: "promo_consumptions");

            migrationBuilder.DropTable(
                name: "promo_grant_merchants");

            migrationBuilder.DropTable(
                name: "promo_grants");
        }
    }
}
