using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HiWallet.WalletService.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// İşlemin aktörü (decisions.md madde 34).
    ///
    /// Üretilen hali elle düzeltildi. EF her iki kolona da <c>defaultValue: ""</c>
    /// koymuştu; ikisi de kabul edilemezdi:
    ///
    /// (a) Boş <c>actor_type</c> okunamıyor — <c>ValueConverters</c> bilinmeyen değerde
    ///     patlıyor, yani mevcut satırlar okunamaz hale gelirdi.
    /// (b) Kolon varsayılanı KALICI olurdu ve aktör belirtmeyi unutan bir INSERT
    ///     sessizce geçerdi. Bu maddenin varlık sebebi tam olarak bunu engellemek.
    /// </summary>
    public partial class AddLedgerTransactionActor : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Mevcut satırların aktörü BİLİNMİYOR ve uydurulmuyor. 'customer' yazmak
            // "müşteri yaptı" demek olurdu — doğrulanamaz bir iddia ve kalıcı bir
            // kayda yazılamaz. 'system' dürüst karşılık: o dönemde backoffice yoktu,
            // yani insan eliyle yazılmadıkları kesin. actor_id ise hangi satırların
            // bu boşlukta kaldığını ayırt edilebilir kılıyor.
            migrationBuilder.AddColumn<string>(
                name: "actor_type",
                table: "ledger_transactions",
                type: "text",
                nullable: false,
                defaultValue: "system");

            migrationBuilder.AddColumn<string>(
                name: "actor_id",
                table: "ledger_transactions",
                type: "text",
                nullable: false,
                defaultValue: "legacy:pre-migration");

            // Backfill bitti; varsayılanlar düşüyor. Bundan sonra aktör YAZILMAK
            // ZORUNDA — veritabanı da bunu dayatıyor, yalnızca C# tarafı değil.
            migrationBuilder.AlterColumn<string>(
                name: "actor_type",
                table: "ledger_transactions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: false,
                oldDefaultValue: "system");

            migrationBuilder.AlterColumn<string>(
                name: "actor_id",
                table: "ledger_transactions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: false,
                oldDefaultValue: "legacy:pre-migration");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "actor_id",
                table: "ledger_transactions");

            migrationBuilder.DropColumn(
                name: "actor_type",
                table: "ledger_transactions");
        }
    }
}
