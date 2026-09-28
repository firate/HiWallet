using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HiWallet.Onboarding.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "consents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject = table.Column<string>(type: "text", nullable: false),
                    document = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<string>(type: "text", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consents", x => x.id);
                    table.CheckConstraint("ck_consents_document", "document IN ('terms','privacy_notice')");
                });

            migrationBuilder.CreateTable(
                name: "customers",
                columns: table => new
                {
                    subject = table.Column<string>(type: "text", nullable: false),
                    phone = table.Column<string>(type: "text", nullable: true),
                    phone_verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    first_name = table.Column<string>(type: "text", nullable: true),
                    last_name = table.Column<string>(type: "text", nullable: true),
                    national_id = table.Column<string>(type: "char(11)", nullable: true),
                    birth_date = table.Column<DateOnly>(type: "date", nullable: true),
                    identity_verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    basic_verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customers", x => x.subject);
                });

            migrationBuilder.CreateTable(
                name: "phone_verifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject = table.Column<string>(type: "text", nullable: false),
                    phone = table.Column<string>(type: "text", nullable: false),
                    code_hash = table.Column<string>(type: "text", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    failed_attempts = table.Column<int>(type: "integer", nullable: false),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_phone_verifications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "registrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    code_hash = table.Column<string>(type: "text", nullable: false),
                    code_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    failed_attempts = table.Column<int>(type: "integer", nullable: false),
                    email_verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    subject = table.Column<string>(type: "text", nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_registrations", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_consents_subject_document_version",
                table: "consents",
                columns: new[] { "subject", "document", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_customers_national_id",
                table: "customers",
                column: "national_id",
                unique: true,
                filter: "national_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_phone_verifications_subject",
                table: "phone_verifications",
                column: "subject");

            migrationBuilder.CreateIndex(
                name: "ix_registrations_email",
                table: "registrations",
                column: "email");

            migrationBuilder.CreateIndex(
                name: "ix_registrations_subject",
                table: "registrations",
                column: "subject");

            // Uygulama rolünün yetkileri. Rol yoksa atlanıyor: integration testler ve
            // yerel kurulum onboarding_app olmadan da koşabilmeli. Şema adı SABİT DEĞİL:
            // testler koşu başına ayrı bir schema'da migrate ediyor.
            migrationBuilder.Sql(
                """
                REVOKE UPDATE, DELETE ON consents FROM PUBLIC;

                DO $$
                DECLARE
                    schema text;
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'onboarding_app') THEN
                        RAISE NOTICE 'onboarding_app rolü yok, yetkilendirme atlanıyor.';
                        RETURN;
                    END IF;

                    schema := quote_ident(current_schema());

                    EXECUTE format('GRANT USAGE ON SCHEMA %s TO onboarding_app', schema);
                    EXECUTE format(
                        'GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA %s TO onboarding_app', schema);

                    -- Sonraki migration'ların üreteceği tablolar için.
                    EXECUTE format(
                        'ALTER DEFAULT PRIVILEGES IN SCHEMA %s
                         GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO onboarding_app', schema);

                    -- Onay değişmiyor ve silinmiyor: onayın kanıtı. Yukarıdaki toplu GRANT
                    -- geri alınıyor; sahip OLMAYAN bir role işlediği için bağlayıcı.
                    EXECUTE 'REVOKE UPDATE, DELETE ON consents FROM onboarding_app';
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consents");

            migrationBuilder.DropTable(
                name: "customers");

            migrationBuilder.DropTable(
                name: "phone_verifications");

            migrationBuilder.DropTable(
                name: "registrations");
        }
    }
}
