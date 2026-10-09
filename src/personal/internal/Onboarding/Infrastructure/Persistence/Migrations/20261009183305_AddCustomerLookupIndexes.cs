using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HiWallet.Onboarding.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerLookupIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_registrations_account_id",
                table: "registrations",
                column: "account_id",
                filter: "account_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_customers_phone",
                table: "customers",
                column: "phone",
                filter: "phone IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_registrations_account_id",
                table: "registrations");

            migrationBuilder.DropIndex(
                name: "ix_customers_phone",
                table: "customers");
        }
    }
}
