using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HiWallet.WithdrawalOrchestrator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInitiatedBySubject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "initiated_by_subject",
                table: "withdrawal_sagas",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "initiated_by_subject",
                table: "withdrawal_sagas");
        }
    }
}
