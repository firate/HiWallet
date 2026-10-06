using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HiWallet.CardTopup.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "card_topup_outbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    card_topup_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    publish_attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_card_topup_outbox", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "card_topups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject = table.Column<string>(type: "text", nullable: false),
                    idempotency_key = table.Column<string>(type: "text", nullable: false),
                    wallet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    currency = table.Column<string>(type: "char(3)", nullable: false),
                    provider = table.Column<string>(type: "text", nullable: false),
                    return_url = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    payment_id = table.Column<string>(type: "text", nullable: true),
                    payment_url = table.Column<string>(type: "text", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    failure_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_card_topups", x => x.id);
                    table.CheckConstraint("ck_card_topups_amount", "amount > 0");
                    table.CheckConstraint("ck_card_topups_state", "state IN ('created','pending','paid','failed','rejected')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_card_topup_outbox_card_topup",
                table: "card_topup_outbox",
                column: "card_topup_id");

            migrationBuilder.CreateIndex(
                name: "ix_card_topup_outbox_unpublished",
                table: "card_topup_outbox",
                column: "created_at",
                filter: "published_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_card_topups_open",
                table: "card_topups",
                column: "expires_at",
                filter: "state IN ('created','pending')");

            migrationBuilder.CreateIndex(
                name: "ux_card_topups_idempotency",
                table: "card_topups",
                columns: new[] { "subject", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "card_topup_outbox");

            migrationBuilder.DropTable(
                name: "card_topups");
        }
    }
}
