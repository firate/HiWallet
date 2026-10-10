using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HiWallet.Onboarding.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPhoneChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "purpose",
                table: "phone_verifications",
                type: "text",
                nullable: false,
                defaultValue: "basic");

            migrationBuilder.CreateTable(
                name: "phone_changes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject = table.Column<string>(type: "text", nullable: false),
                    old_phone = table.Column<string>(type: "text", nullable: true),
                    new_phone = table.Column<string>(type: "text", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_phone_changes", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_phone_changes_subject",
                table: "phone_changes",
                column: "subject");

            // Değişiklik kaydı değişmiyor ve silinmiyor: eski numara müşteri bilgisinin
            // geçmişi. İlk migration'ın varsayılan yetkisi tabloya UPDATE ve DELETE de
            // veriyor; onboarding_app'ten geri alınıyor (consents ile aynı).
            migrationBuilder.Sql(
                """
                REVOKE UPDATE, DELETE ON phone_changes FROM PUBLIC;

                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'onboarding_app') THEN
                        RAISE NOTICE 'onboarding_app rolü yok, yetkilendirme atlanıyor.';
                        RETURN;
                    END IF;

                    EXECUTE 'REVOKE UPDATE, DELETE ON phone_changes FROM onboarding_app';
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "phone_changes");

            migrationBuilder.DropColumn(
                name: "purpose",
                table: "phone_verifications");
        }
    }
}
