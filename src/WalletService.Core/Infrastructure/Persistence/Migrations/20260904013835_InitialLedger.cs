using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HiWallet.WalletService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounts", x => x.id);
                    table.CheckConstraint("ck_accounts_type", "type IN ('person','business')");
                });

            migrationBuilder.CreateTable(
                name: "ledger_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "text", nullable: true),
                    provider = table.Column<string>(type: "text", nullable: true),
                    currency = table.Column<string>(type: "char(3)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    provider_key = table.Column<string>(type: "text", nullable: true, computedColumnSql: "COALESCE(provider, '')", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_accounts", x => x.id);
                    table.UniqueConstraint("uq_ledger_accounts_id_currency", x => new { x.id, x.currency });
                    table.CheckConstraint("ck_ledger_accounts_account", "(type = 'user_wallet') = (account_id IS NOT NULL)");
                    table.CheckConstraint("ck_ledger_accounts_name", "(type = 'user_wallet') = (name IS NOT NULL)");
                    table.CheckConstraint("ck_ledger_accounts_name_blank", "name IS NULL OR btrim(name) <> ''");
                    table.CheckConstraint("ck_ledger_accounts_provider", "(type IN ('clearing','nostro','provider_expense')) = (provider IS NOT NULL)");
                    table.CheckConstraint("ck_ledger_accounts_provider_blank", "provider IS NULL OR btrim(provider) <> ''");
                    table.CheckConstraint("ck_ledger_accounts_type", "type IN ('user_wallet','clearing','revenue','nostro','provider_expense')");
                    table.ForeignKey(
                        name: "fk_ledger_accounts_account",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ledger_transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    ledger_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<string>(type: "text", nullable: true),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_transactions", x => x.id);
                    table.ForeignKey(
                        name: "fk_ledger_transactions_ledger_account",
                        column: x => x.ledger_account_id,
                        principalTable: "ledger_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "wallet_balances",
                columns: table => new
                {
                    ledger_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    balance = table.Column<decimal>(type: "numeric(19,4)", nullable: false, defaultValue: 0m),
                    currency = table.Column<string>(type: "char(3)", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wallet_balances", x => x.ledger_account_id);
                    table.ForeignKey(
                        name: "fk_wallet_balances_ledger_account",
                        columns: x => new { x.ledger_account_id, x.currency },
                        principalTable: "ledger_accounts",
                        principalColumns: new[] { "id", "currency" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ledger_entries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    currency = table.Column<string>(type: "char(3)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_entries", x => x.id);
                    table.CheckConstraint("ck_ledger_entries_amount", "amount <> 0");
                    table.ForeignKey(
                        name: "fk_ledger_entries_ledger_account",
                        columns: x => new { x.ledger_account_id, x.currency },
                        principalTable: "ledger_accounts",
                        principalColumns: new[] { "id", "currency" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ledger_entries_transaction",
                        column: x => x.transaction_id,
                        principalTable: "ledger_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_accounts_account",
                table: "ledger_accounts",
                column: "account_id",
                filter: "account_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_ledger_accounts_system",
                table: "ledger_accounts",
                columns: new[] { "type", "provider_key", "currency" },
                unique: true,
                filter: "account_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_ledger_account",
                table: "ledger_entries",
                columns: new[] { "ledger_account_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_ledger_account_currency",
                table: "ledger_entries",
                columns: new[] { "ledger_account_id", "currency" });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_tx",
                table: "ledger_entries",
                column: "transaction_id");

            migrationBuilder.CreateIndex(
                name: "ux_ledger_tx_idem",
                table: "ledger_transactions",
                columns: new[] { "ledger_account_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_balances_ledger_account_currency",
                table: "wallet_balances",
                columns: new[] { "ledger_account_id", "currency" });

            // ---------------------------------------------------------------------
            // Buradan aşağısı EF modeliyle İFADE EDİLEMEYEN kısım. Fluent API'nin
            // trigger ve yetki karşılığı yok, o yüzden ham SQL. Ayrı bir .sql dosyası
            // TUTULMUYOR — migration'ın içinde duruyorlar ki şema versiyonlamasından
            // kopmasınlar (structure.md "Migration komutları").
            // ---------------------------------------------------------------------

            // Zero-sum invariant. Toplam para birimi BAŞINA sıfır olmalı: tek SUM yetmez,
            // +100 TRY ile -100 USD toplamı sıfır çıkar ve dengesiz bir işlem dengeli
            // sayılırdı (decisions.md madde 17).
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION assert_ledger_balanced() RETURNS trigger AS $$
                DECLARE
                    bad_currency char(3);
                    bad_total    numeric(19,4);
                BEGIN
                    SELECT currency, SUM(amount)
                      INTO bad_currency, bad_total
                      FROM ledger_entries
                     WHERE transaction_id = NEW.transaction_id
                     GROUP BY currency
                    HAVING SUM(amount) <> 0
                     LIMIT 1;

                    IF FOUND THEN
                        RAISE EXCEPTION 'Ledger transaction % is unbalanced in %: %',
                            NEW.transaction_id, bad_currency, bad_total;
                    END IF;

                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;
                """);

            // DEFERRABLE INITIALLY DEFERRED şart: satırlar tek tek insert edilirken ara
            // durumda toplam sıfır değil, kontrol commit anında çalışmalı.
            // FOR EACH ROW da şart — Postgres CONSTRAINT TRIGGER'ı statement seviyesinde
            // deferred yapamıyor.
            migrationBuilder.Sql("""
                CREATE CONSTRAINT TRIGGER trg_ledger_balanced
                    AFTER INSERT ON ledger_entries
                    DEFERRABLE INITIALLY DEFERRED
                    FOR EACH ROW EXECUTE FUNCTION assert_ledger_balanced();
                """);

            // Append-only zorlaması. Bu REVOKE yalnızca tablo sahibi OLMAYAN bir role
            // işler — sahiplik yetkisi örtüktür ve revoke edilemez. Migration wallet_owner
            // ile koşuyor, uygulama wallet_app ile bağlanıyor; ayrım olmasaydı bu satır
            // tamamen süs olurdu (decisions.md madde 5).
            // IF EXISTS: rol yoksa (örn. tek kullanıcılı local kurulum) migration patlamasın.
            migrationBuilder.Sql("""
                REVOKE UPDATE, DELETE ON ledger_entries FROM PUBLIC;

                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'wallet_app') THEN
                        REVOKE UPDATE, DELETE ON ledger_entries FROM wallet_app;
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_ledger_balanced ON ledger_entries;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS assert_ledger_balanced();");

            migrationBuilder.DropTable(
                name: "ledger_entries");

            migrationBuilder.DropTable(
                name: "wallet_balances");

            migrationBuilder.DropTable(
                name: "ledger_transactions");

            migrationBuilder.DropTable(
                name: "ledger_accounts");

            migrationBuilder.DropTable(
                name: "accounts");
        }
    }
}
