using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HiWallet.WalletService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountNumberAndDefaultWallets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Numara önce boş açılıyor, var olan hesaplar dolduruluyor, sonra zorunlu oluyor.
            migrationBuilder.AddColumn<string>(
                name: "number",
                table: "accounts",
                type: "text",
                nullable: true);

            // Var olan hesaplara numara: uygulamanın AccountNumber.New()'uyla aynı biçim
            // (dokuz rastgele hane, ilk hane sıfır değil, Luhn kontrol hanesi). Fonksiyon
            // oturuma özel (pg_temp); migration bitince kalmıyor. Çakışan numara yeniden
            // üretiliyor.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION pg_temp.new_account_number() RETURNS text LANGUAGE plpgsql AS $$
                DECLARE
                    body text := (100000000 + floor(random() * 900000000))::bigint::text;
                    total int := 0;
                    digit int;
                BEGIN
                    FOR i IN 0..8 LOOP
                        digit := substr(body, 9 - i, 1)::int;
                        IF i % 2 = 0 THEN
                            digit := digit * 2;
                            IF digit > 9 THEN digit := digit - 9; END IF;
                        END IF;
                        total := total + digit;
                    END LOOP;

                    RETURN body || ((10 - total % 10) % 10)::text;
                END
                $$;

                DO $$
                DECLARE
                    account record;
                    candidate text;
                BEGIN
                    FOR account IN SELECT id FROM accounts WHERE number IS NULL LOOP
                        LOOP
                            candidate := pg_temp.new_account_number();
                            EXIT WHEN NOT EXISTS (SELECT 1 FROM accounts WHERE number = candidate);
                        END LOOP;

                        UPDATE accounts SET number = candidate WHERE id = account.id;
                    END LOOP;
                END
                $$;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "number",
                table: "accounts",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "default_wallets",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "char(3)", nullable: false),
                    wallet_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_default_wallets", x => new { x.account_id, x.currency });
                    table.ForeignKey(
                        name: "fk_default_wallets_account",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_default_wallets_wallet",
                        columns: x => new { x.wallet_id, x.currency },
                        principalTable: "ledger_accounts",
                        principalColumns: new[] { "id", "currency" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_accounts_number",
                table: "accounts",
                column: "number",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_accounts_number",
                table: "accounts",
                sql: "number ~ '^[1-9][0-9]{9}$'");

            migrationBuilder.CreateIndex(
                name: "ix_default_wallets_wallet",
                table: "default_wallets",
                columns: new[] { "wallet_id", "currency" });

            // Var olan cüzdanlar: her hesabın her para biriminde en eski cüzdanı varsayılan.
            migrationBuilder.Sql(
                """
                INSERT INTO default_wallets (account_id, currency, wallet_id)
                SELECT DISTINCT ON (account_id, currency) account_id, currency, id
                FROM ledger_accounts
                WHERE type = 'user_wallet'
                ORDER BY account_id, currency, created_at, id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "default_wallets");


            migrationBuilder.DropIndex(
                name: "ux_accounts_number",
                table: "accounts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_accounts_number",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "number",
                table: "accounts");
        }
    }
}
