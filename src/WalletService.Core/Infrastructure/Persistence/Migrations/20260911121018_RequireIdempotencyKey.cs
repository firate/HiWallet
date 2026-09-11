using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HiWallet.WalletService.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Idempotency key artık ZORUNLU (decisions.md madde 4).
    ///
    /// Üretilen hali elle düzeltildi. EF <c>defaultValue: ""</c> ile backfill ediyordu
    /// ve bu iki şekilde bozuktu: (a) aynı hesapta anahtarsız iki işlem varsa
    /// <c>(ledger_account_id, "")</c> çakışır ve unique index HİÇ kurulamaz,
    /// (b) kurulsa bile o hesabın bir sonraki anahtarsız kaydı aynı boş anahtarla
    /// çarpışırdı.
    ///
    /// Backfill değeri işlemin KENDİ kimliği: yapı gereği benzersiz, bu yüzden index'i
    /// kurabiliyor, ve <c>legacy:</c> öneki bunun istemciden gelmediğini açıkça
    /// söylüyor — uydurulmuş bir istemci anahtarı gibi görünmüyor.
    /// </summary>
    public partial class RequireIdempotencyKey : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Partial index düşüyor: NULL'lar zaten kapsamı dışındaydı, backfill'den
            // sonra filtresiz haliyle yeniden kurulacak.
            migrationBuilder.DropIndex(
                name: "ux_ledger_tx_idem",
                table: "ledger_transactions");

            migrationBuilder.Sql(
                """
                UPDATE ledger_transactions
                   SET idempotency_key = 'legacy:' || id::text
                 WHERE idempotency_key IS NULL;
                """);

            // defaultValue YOK: backfill bitti, her satırın değeri var. Varsayılan
            // verilseydi kolon üzerinde KALICI olur ve anahtarsız bir INSERT sessizce
            // geçerdi — maddenin engellemek istediği şeyin ta kendisi.
            migrationBuilder.AlterColumn<string>(
                name: "idempotency_key",
                table: "ledger_transactions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            // Filtresiz: anahtar artık her satırda var. Filtre kalsaydı NULL yazabilen
            // bir yol açıldığında index onu sessizce kapsam dışı bırakırdı.
            migrationBuilder.CreateIndex(
                name: "ux_ledger_tx_idem",
                table: "ledger_transactions",
                columns: new[] { "ledger_account_id", "idempotency_key" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_ledger_tx_idem",
                table: "ledger_transactions");

            migrationBuilder.AlterColumn<string>(
                name: "idempotency_key",
                table: "ledger_transactions",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            // Backfill edilen değerler NULL'a DÖNDÜRÜLMÜYOR: geri alma veri silmemeli
            // ve 'legacy:' önekli satırlar zaten ayırt edilebilir durumda.
            migrationBuilder.CreateIndex(
                name: "ux_ledger_tx_idem",
                table: "ledger_transactions",
                columns: new[] { "ledger_account_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");
        }
    }
}
