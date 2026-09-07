using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HiWallet.TopupWebhook.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Inbox artık iki akış taşıyor: top-up ve settlement. <c>ledger_account_id</c>
    /// (cüzdan bazlı partition anahtarı) yerini genel bir <c>routing_key</c>'e
    /// bırakıyor, <c>kind</c> ise relay'in hangi exchange'e yayınlayacağını söylüyor.
    ///
    /// İkinci bir inbox tablosu ve ikinci bir relay açılmadı: relay'in işi (ele
    /// geçir, yayınla, işaretle) iki akışta birebir aynı (decisions.md madde 25).
    /// </summary>
    public partial class GeneralizeInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SIRA ÖNEMLİ. EF'in ürettiği hali ledger_account_id'yi backfill'den
            // ÖNCE düşürüyordu; o sırada yayınlanmamış satırlar partition anahtarını
            // kaybeder ve boş routing key ile yayınlanırdı. Önce ekle, doldur,
            // sonra düşür.
            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "topup_inbox",
                type: "text",
                nullable: false,
                defaultValue: "topup");

            migrationBuilder.AddColumn<string>(
                name: "routing_key",
                table: "topup_inbox",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                """
                UPDATE topup_inbox
                   SET routing_key = ledger_account_id::text
                 WHERE routing_key = '';
                """);

            migrationBuilder.DropColumn(
                name: "ledger_account_id",
                table: "topup_inbox");

            // Varsayılan yalnızca mevcut satırları doldurmak içindi. Kalıcı olsaydı
            // yeni bir akış eklenirken kind'ı yazmayı unutmak sessizce "topup"a
            // düşerdi ve mesaj yanlış exchange'e giderdi.
            migrationBuilder.AlterColumn<string>(
                name: "kind",
                table: "topup_inbox",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValue: "topup");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ledger_account_id",
                table: "topup_inbox",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Geri dönüşte de veri korunuyor. Settlement satırları uuid'ye
            // çevrilemiyor; onlar sıfır uuid ile kalıyor — geri dönüş zaten bu
            // akışın hiç var olmadığı bir sürüme gidiyor.
            migrationBuilder.Sql(
                """
                UPDATE topup_inbox
                   SET ledger_account_id = routing_key::uuid
                 WHERE kind = 'topup';
                """);

            migrationBuilder.DropColumn(name: "kind", table: "topup_inbox");
            migrationBuilder.DropColumn(name: "routing_key", table: "topup_inbox");
        }
    }
}
