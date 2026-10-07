using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace HiWallet.WalletService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
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

            migrationBuilder.CreateTable(
                name: "provider_fees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "text", nullable: false),
                    settlement_model = table.Column<string>(type: "text", nullable: false),
                    fee_type = table.Column<string>(type: "text", nullable: false),
                    expected_amount = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    actual_amount = table.Column<decimal>(type: "numeric(19,4)", nullable: true),
                    currency = table.Column<string>(type: "char(3)", fixedLength: true, nullable: false),
                    provider_ref = table.Column<string>(type: "text", nullable: true),
                    invoice_ref = table.Column<string>(type: "text", nullable: true),
                    ledger_tx_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provider_fees", x => x.id);
                    table.CheckConstraint("ck_provider_fees_settlement_model", "settlement_model IN ('net','invoiced')");
                });

            migrationBuilder.CreateTable(
                name: "provider_invoices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "text", nullable: false),
                    invoice_ref = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    expected_amount = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    currency = table.Column<string>(type: "char(3)", fixedLength: true, nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    fee_count = table.Column<int>(type: "integer", nullable: false),
                    ledger_tx_id = table.Column<Guid>(type: "uuid", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provider_invoices", x => x.id);
                    table.CheckConstraint("ck_provider_invoices_status", "status IN ('applied','pending_review')");
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
                name: "ledger_balances",
                columns: table => new
                {
                    ledger_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fund_type = table.Column<string>(type: "text", nullable: false),
                    balance = table.Column<decimal>(type: "numeric(19,4)", nullable: false, defaultValue: 0m),
                    currency = table.Column<string>(type: "char(3)", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_balances", x => new { x.ledger_account_id, x.fund_type });
                    table.CheckConstraint("ck_ledger_balances_fund_type", "fund_type IN ('cash','card','promo')");
                    table.ForeignKey(
                        name: "fk_ledger_balances_ledger_account",
                        columns: x => new { x.ledger_account_id, x.currency },
                        principalTable: "ledger_accounts",
                        principalColumns: new[] { "id", "currency" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ledger_transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    ledger_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_type = table.Column<string>(type: "text", nullable: false),
                    actor_id = table.Column<string>(type: "text", nullable: false),
                    idempotency_key = table.Column<string>(type: "text", nullable: false),
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
                name: "ledger_entries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    currency = table.Column<string>(type: "char(3)", nullable: false),
                    fund_type = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_entries", x => x.id);
                    table.CheckConstraint("ck_ledger_entries_amount", "amount <> 0");
                    table.CheckConstraint("ck_ledger_entries_fund_type", "fund_type IN ('cash','card','promo')");
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

            migrationBuilder.InsertData(
                table: "ledger_accounts",
                columns: new[] { "id", "account_id", "created_at", "currency", "name", "provider", "type" },
                values: new object[,]
                {
                    { new Guid("a0000000-0000-4000-8000-000000000001"), null, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "TRY", null, null, "revenue" },
                    { new Guid("a0000000-0000-4000-8000-000000000002"), null, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "TRY", null, "stripe-fake", "clearing" },
                    { new Guid("a0000000-0000-4000-8000-000000000003"), null, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "TRY", null, "bank-fake", "clearing" },
                    { new Guid("a0000000-0000-4000-8000-000000000004"), null, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "TRY", null, "bank-fake", "nostro" },
                    { new Guid("a0000000-0000-4000-8000-000000000005"), null, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "TRY", null, "stripe-fake", "provider_expense" },
                    { new Guid("a0000000-0000-4000-8000-000000000006"), null, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "TRY", null, "bank-fake", "provider_expense" }
                });

            migrationBuilder.InsertData(
                table: "ledger_balances",
                columns: new[] { "fund_type", "ledger_account_id", "currency", "updated_at" },
                values: new object[,]
                {
                    { "card", new Guid("a0000000-0000-4000-8000-000000000001"), "TRY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "cash", new Guid("a0000000-0000-4000-8000-000000000001"), "TRY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "promo", new Guid("a0000000-0000-4000-8000-000000000001"), "TRY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "card", new Guid("a0000000-0000-4000-8000-000000000002"), "TRY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "cash", new Guid("a0000000-0000-4000-8000-000000000002"), "TRY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "promo", new Guid("a0000000-0000-4000-8000-000000000002"), "TRY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "card", new Guid("a0000000-0000-4000-8000-000000000003"), "TRY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "cash", new Guid("a0000000-0000-4000-8000-000000000003"), "TRY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "promo", new Guid("a0000000-0000-4000-8000-000000000003"), "TRY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "card", new Guid("a0000000-0000-4000-8000-000000000004"), "TRY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "cash", new Guid("a0000000-0000-4000-8000-000000000004"), "TRY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "promo", new Guid("a0000000-0000-4000-8000-000000000004"), "TRY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "card", new Guid("a0000000-0000-4000-8000-000000000005"), "TRY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "cash", new Guid("a0000000-0000-4000-8000-000000000005"), "TRY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "promo", new Guid("a0000000-0000-4000-8000-000000000005"), "TRY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "card", new Guid("a0000000-0000-4000-8000-000000000006"), "TRY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "cash", new Guid("a0000000-0000-4000-8000-000000000006"), "TRY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "promo", new Guid("a0000000-0000-4000-8000-000000000006"), "TRY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) }
                });

            migrationBuilder.CreateIndex(
                name: "ix_business_daily_summaries_day",
                table: "business_daily_summaries",
                column: "day");

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
                name: "ix_ledger_balances_ledger_account_currency",
                table: "ledger_balances",
                columns: new[] { "ledger_account_id", "currency" });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_ledger_account",
                table: "ledger_entries",
                columns: new[] { "ledger_account_id", "fund_type", "id" });

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
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_processed_messages_saga",
                table: "processed_messages",
                column: "saga_id");

            migrationBuilder.CreateIndex(
                name: "ix_provider_fees_invoice",
                table: "provider_fees",
                column: "invoice_ref");

            migrationBuilder.CreateIndex(
                name: "ix_provider_fees_tx",
                table: "provider_fees",
                column: "transaction_id");

            migrationBuilder.CreateIndex(
                name: "ix_provider_fees_unbilled",
                table: "provider_fees",
                columns: new[] { "provider", "occurred_at" },
                filter: "invoice_ref IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_provider_invoices_pending",
                table: "provider_invoices",
                column: "received_at",
                filter: "status = 'pending_review'");

            migrationBuilder.CreateIndex(
                name: "ux_provider_invoices_ref",
                table: "provider_invoices",
                columns: new[] { "provider", "invoice_ref" },
                unique: true);
            // --- EF'in ÜRETMEDİĞİ kısım ------------------------------------------
            // Aşağıdakiler model'den çıkarılamıyor; migration'lar tek dosyaya
            // indirilirken elle taşındı. Kaybolmaları sessiz olurdu, o yüzden
            // testlerle korunuyorlar: trigger'ı SchemaTests, yetkileri
            // AppRolePrivilegeTests doğruluyor.

            // provider_fees'in ledger_transactions'a iki FK'si. EF modelinde
            // navigation yok — tablo ledger DEĞİL, yalnızca ücret beklentisini
            // tutuyor (decisions.md madde 10) ve ledger tarafına bağımlılık
            // yaratmaması için ilişki modellenmedi.
            migrationBuilder.Sql(
                """
                ALTER TABLE provider_fees
                    ADD CONSTRAINT fk_provider_fees_transaction
                        FOREIGN KEY (transaction_id) REFERENCES ledger_transactions(id),
                    ADD CONSTRAINT fk_provider_fees_ledger_tx
                        FOREIGN KEY (ledger_tx_id) REFERENCES ledger_transactions(id);
                """);

            // Zero-sum invariant'ı (decisions.md madde 5). Toplam para birimi
            // BAŞINA sıfır olmalı: tek SUM yetmez, +100 TRY ile -100 USD toplamı
            // sıfır çıkar ve dengesiz bir işlem dengeli sayılırdı.
            migrationBuilder.Sql(
                """
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

            // DEFERRABLE INITIALLY DEFERRED şart: satırlar tek tek insert edilirken
            // ara durumda toplam sıfır değil, kontrol commit anında çalışmalı.
            // FOR EACH ROW de şart — Postgres CONSTRAINT TRIGGER'ı statement
            // seviyesinde deferred yapamıyor.
            migrationBuilder.Sql(
                """
                CREATE CONSTRAINT TRIGGER trg_ledger_balanced
                    AFTER INSERT ON ledger_entries
                    DEFERRABLE INITIALLY DEFERRED
                    FOR EACH ROW EXECUTE FUNCTION assert_ledger_balanced();
                """);

            // Append-only zorlaması (CLAUDE.md "Ledger"): düzeltme ters kayıtla
            // yapılır, UPDATE ve DELETE yoktur.
            migrationBuilder.Sql(
                """
                REVOKE UPDATE, DELETE ON ledger_entries FROM PUBLIC;

                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'wallet_app') THEN
                        REVOKE UPDATE, DELETE ON ledger_entries FROM wallet_app;
                    END IF;
                END
                $$;
                """);

            // Uygulama rolünün yetkileri. Rol yoksa atlanıyor: integration testler
            // ve local kurulum wallet_app olmadan da koşabilmeli.
            migrationBuilder.Sql(
                """
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
            migrationBuilder.DropTable(
                name: "business_daily_summaries");

            migrationBuilder.DropTable(
                name: "ledger_balances");

            migrationBuilder.DropTable(
                name: "ledger_entries");

            migrationBuilder.DropTable(
                name: "processed_events");

            migrationBuilder.DropTable(
                name: "processed_messages");

            migrationBuilder.DropTable(
                name: "provider_fees");

            migrationBuilder.DropTable(
                name: "provider_invoices");

            migrationBuilder.DropTable(
                name: "ledger_transactions");

            migrationBuilder.DropTable(
                name: "ledger_accounts");

            migrationBuilder.DropTable(
                name: "accounts");
        }
    }
}
