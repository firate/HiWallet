using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HiWallet.CardTopup.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWalletIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_card_topups_wallet",
                table: "card_topups",
                columns: new[] { "wallet_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_card_topups_wallet",
                table: "card_topups");
        }
    }
}
