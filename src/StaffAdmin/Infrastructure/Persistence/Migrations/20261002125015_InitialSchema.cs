using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HiWallet.StaffAdmin.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "staff_audit_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_subject = table.Column<string>(type: "text", nullable: false),
                    actor_name = table.Column<string>(type: "text", nullable: true),
                    action = table.Column<string>(type: "text", nullable: false),
                    target_type = table.Column<string>(type: "text", nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_label = table.Column<string>(type: "text", nullable: false),
                    details = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staff_audit_events", x => x.id);
                    table.CheckConstraint("ck_staff_audit_events_action", "action IN ('role_created','role_updated','role_deleted','staff_invited','staff_roles_changed','staff_disabled','staff_enabled','invitation_sent')");
                    table.CheckConstraint("ck_staff_audit_events_target_type", "target_type IN ('role','staff')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_staff_audit_events_target_id",
                table: "staff_audit_events",
                column: "target_id");

            // Uygulama rolünün yetkileri. Rol yoksa atlanıyor: integration testler ve
            // yerel kurulum staff_admin_app olmadan da koşabilmeli. Şema adı SABİT DEĞİL:
            // testler koşu başına ayrı bir schema'da migrate ediyor.
            migrationBuilder.Sql(
                """
                REVOKE UPDATE, DELETE ON staff_audit_events FROM PUBLIC;

                DO $$
                DECLARE
                    schema text;
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'staff_admin_app') THEN
                        RAISE NOTICE 'staff_admin_app rolü yok, yetkilendirme atlanıyor.';
                        RETURN;
                    END IF;

                    schema := quote_ident(current_schema());

                    EXECUTE format('GRANT USAGE ON SCHEMA %s TO staff_admin_app', schema);
                    EXECUTE format(
                        'GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA %s TO staff_admin_app', schema);

                    -- Sonraki migration'ların üreteceği tablolar için.
                    EXECUTE format(
                        'ALTER DEFAULT PRIVILEGES IN SCHEMA %s
                         GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO staff_admin_app', schema);

                    -- Kayıt değişmiyor ve silinmiyor: kim kime ne verdi sonradan yazılamasın.
                    -- Yukarıdaki toplu GRANT geri alınıyor; sahip OLMAYAN bir role işlediği için
                    -- bağlayıcı.
                    EXECUTE 'REVOKE UPDATE, DELETE ON staff_audit_events FROM staff_admin_app';
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "staff_audit_events");
        }
    }
}
