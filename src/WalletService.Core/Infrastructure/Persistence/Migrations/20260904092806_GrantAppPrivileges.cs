using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HiWallet.WalletService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GrantAppPrivileges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Uygulama rolünün yetkileri. Migration wallet_owner ile koşuyor ve tabloların
            // sahibi o; sahip kendi tablolarında GRANT verebiliyor, superuser gerekmiyor.
            //
            // Neden migration'da: elle çalıştırılan bir kurulum adımı olarak bırakılırsa
            // her yeni ortamda unutuluyor ve uygulama "permission denied" ile ayağa
            // kalkmıyor. REVOKE zaten migration'da; GRANT'in ayrı yerde durması ikisinin
            // ayrışmasına yol açar.
            //
            // Rol yoksa (tek kullanıcılı local kurulum) migration patlamamalı.
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    schema text;
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'wallet_app') THEN
                        RAISE NOTICE 'wallet_app rolü yok, yetkilendirme atlanıyor.';
                        RETURN;
                    END IF;

                    -- Şema adı SABİT DEĞİL: integration testler koşu başına ayrı bir
                    -- schema'da migrate ediyor, 'public' yazılsaydı yetkiler yanlış
                    -- yere verilirdi ve testte fark edilmezdi.
                    schema := quote_ident(current_schema());

                    EXECUTE format('GRANT USAGE ON SCHEMA %s TO wallet_app', schema);

                    EXECUTE format(
                        'GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA %s TO wallet_app', schema);
                    EXECUTE format(
                        'GRANT USAGE ON ALL SEQUENCES IN SCHEMA %s TO wallet_app', schema);

                    -- Sonraki migration'ların üreteceği tablolar için.
                    EXECUTE format(
                        'ALTER DEFAULT PRIVILEGES IN SCHEMA %s
                         GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO wallet_app', schema);
                    EXECUTE format(
                        'ALTER DEFAULT PRIVILEGES IN SCHEMA %s
                         GRANT USAGE ON SEQUENCES TO wallet_app', schema);

                    -- Append-only: yukarıdaki toplu GRANT ledger_entries'e de UPDATE/DELETE
                    -- verdi, geri alınıyor. Sıra önemli — REVOKE en sonda olmalı.
                    -- Sahip OLMAYAN bir role işlediği için gerçekten bağlayıcı
                    -- (decisions.md madde 5).
                    EXECUTE 'REVOKE UPDATE, DELETE ON ledger_entries FROM wallet_app';
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    schema text;
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'wallet_app') THEN
                        RETURN;
                    END IF;

                    schema := quote_ident(current_schema());

                    EXECUTE format('ALTER DEFAULT PRIVILEGES IN SCHEMA %s
                                    REVOKE SELECT, INSERT, UPDATE, DELETE ON TABLES FROM wallet_app', schema);
                    EXECUTE format('ALTER DEFAULT PRIVILEGES IN SCHEMA %s
                                    REVOKE USAGE ON SEQUENCES FROM wallet_app', schema);
                    EXECUTE format('REVOKE ALL ON ALL TABLES IN SCHEMA %s FROM wallet_app', schema);
                    EXECUTE format('REVOKE ALL ON ALL SEQUENCES IN SCHEMA %s FROM wallet_app', schema);
                    EXECUTE format('REVOKE USAGE ON SCHEMA %s FROM wallet_app', schema);
                END
                $$;
                """);
        }
    }
}
