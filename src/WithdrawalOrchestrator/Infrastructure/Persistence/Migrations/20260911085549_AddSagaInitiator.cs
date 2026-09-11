using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HiWallet.WithdrawalOrchestrator.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Çekimi kim başlattı (decisions.md madde 34).
    ///
    /// Üretilen hali elle düzeltildi — wallet tarafındaki aynı gerekçeyle: EF'in
    /// koyduğu <c>defaultValue: ""</c> kolon varsayılanı olarak KALICI olurdu ve
    /// başlatanı yazmayı unutan bir INSERT sessizce geçerdi.
    /// </summary>
    public partial class AddSagaInitiator : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Mevcut saga'ların hepsi müşteri isteğiyle açıldı — o dönemde backoffice
            // yoktu ve çekim başlatmanın tek yolu POST /v1/withdrawals'tı. Yani
            // buradaki backfill ledger'daki `legacy:pre-migration`'ın aksine bir
            // tahmin değil, bilinen bir gerçek: başlatan hesabın kendisi.
            migrationBuilder.AddColumn<string>(
                name: "initiated_by_type",
                table: "withdrawal_sagas",
                type: "text",
                nullable: false,
                defaultValue: "customer");

            migrationBuilder.AddColumn<string>(
                name: "initiated_by_id",
                table: "withdrawal_sagas",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                "UPDATE withdrawal_sagas SET initiated_by_id = account_id::text WHERE initiated_by_id = '';");

            // Backfill bitti; varsayılanlar düşüyor.
            migrationBuilder.AlterColumn<string>(
                name: "initiated_by_type",
                table: "withdrawal_sagas",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: false,
                oldDefaultValue: "customer");

            migrationBuilder.AlterColumn<string>(
                name: "initiated_by_id",
                table: "withdrawal_sagas",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: false,
                oldDefaultValue: "");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "initiated_by_id",
                table: "withdrawal_sagas");

            migrationBuilder.DropColumn(
                name: "initiated_by_type",
                table: "withdrawal_sagas");
        }
    }
}
