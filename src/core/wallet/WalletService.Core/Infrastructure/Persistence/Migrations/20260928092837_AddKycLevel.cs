using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HiWallet.WalletService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKycLevel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "holder",
                table: "accounts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "kyc_level",
                table: "accounts",
                type: "text",
                nullable: true);

            // Kayıt akışından önce açılmış bireysel hesaplar doğrulamadan geçmedi; en alt
            // seviyeden başlıyorlar. Sahipleri boş kalıyor: eski akışta bir kimliğin birden
            // fazla bireysel hesabı olabiliyordu ve hangisinin o kimliğin hesabı olduğu
            // belli değil. Kullanıcıları account_members'ta duruyor.
            migrationBuilder.Sql("UPDATE accounts SET kyc_level = 'unknown' WHERE type = 'person';");

            migrationBuilder.CreateIndex(
                name: "ux_accounts_person_holder",
                table: "accounts",
                column: "holder",
                unique: true,
                filter: "holder IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_accounts_holder",
                table: "accounts",
                sql: "type = 'person' OR holder IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_accounts_kyc_level",
                table: "accounts",
                sql: "(type = 'person' AND kyc_level IN ('unknown','unverified','verified','contracted')) OR (type = 'business' AND kyc_level IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_accounts_person_holder",
                table: "accounts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_accounts_holder",
                table: "accounts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_accounts_kyc_level",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "holder",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "kyc_level",
                table: "accounts");
        }
    }
}
