using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HiWallet.WalletService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPromoCampaignStaff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_promo_campaigns_period",
                table: "promo_campaigns");

            migrationBuilder.AddColumn<string>(
                name: "created_by",
                table: "promo_campaigns",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ended_by",
                table: "promo_campaigns",
                type: "text",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_promo_campaigns_period",
                table: "promo_campaigns",
                sql: "ends_at IS NULL OR ends_at >= starts_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_promo_campaigns_period",
                table: "promo_campaigns");

            migrationBuilder.DropColumn(
                name: "created_by",
                table: "promo_campaigns");

            migrationBuilder.DropColumn(
                name: "ended_by",
                table: "promo_campaigns");

            migrationBuilder.AddCheckConstraint(
                name: "ck_promo_campaigns_period",
                table: "promo_campaigns",
                sql: "ends_at IS NULL OR ends_at > starts_at");
        }
    }
}
