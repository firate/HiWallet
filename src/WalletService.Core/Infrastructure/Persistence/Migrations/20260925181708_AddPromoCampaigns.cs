using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HiWallet.WalletService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPromoCampaigns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "campaign_id",
                table: "promo_grants",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "promo_campaign_evaluations",
                columns: table => new
                {
                    ledger_transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evaluated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_promo_campaign_evaluations", x => x.ledger_transaction_id);
                    table.ForeignKey(
                        name: "fk_promo_campaign_evaluations_transaction",
                        column: x => x.ledger_transaction_id,
                        principalTable: "ledger_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "promo_campaigns",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    rule = table.Column<string>(type: "text", nullable: false),
                    threshold_amount = table.Column<decimal>(type: "numeric(19,4)", nullable: true),
                    reward_type = table.Column<string>(type: "text", nullable: false),
                    reward_amount = table.Column<decimal>(type: "numeric(19,4)", nullable: true),
                    reward_rate = table.Column<decimal>(type: "numeric(9,6)", nullable: true),
                    reward_max = table.Column<decimal>(type: "numeric(19,4)", nullable: true),
                    currency = table.Column<string>(type: "char(3)", nullable: false),
                    grant_scope = table.Column<string>(type: "text", nullable: false),
                    grant_valid_for = table.Column<TimeSpan>(type: "interval", nullable: true),
                    budget = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    daily_cap_per_account = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    total_cap_per_account = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_promo_campaigns", x => x.id);
                    table.CheckConstraint("ck_promo_campaigns_fixed", "(reward_type = 'fixed') = (reward_amount IS NOT NULL) AND (reward_amount IS NULL OR reward_amount > 0)");
                    table.CheckConstraint("ck_promo_campaigns_grant_scope", "grant_scope IN ('all_businesses','selected_businesses')");
                    table.CheckConstraint("ck_promo_campaigns_grant_valid_for", "grant_valid_for IS NULL OR grant_valid_for > interval '0'");
                    table.CheckConstraint("ck_promo_campaigns_limits", "budget > 0 AND daily_cap_per_account > 0 AND total_cap_per_account > 0");
                    table.CheckConstraint("ck_promo_campaigns_name", "btrim(name) <> ''");
                    table.CheckConstraint("ck_promo_campaigns_percentage", "(reward_type = 'percentage') = (reward_rate IS NOT NULL) AND (reward_type = 'percentage') = (reward_max IS NOT NULL) AND (reward_rate IS NULL OR (reward_rate > 0 AND reward_rate <= 1)) AND (reward_max IS NULL OR reward_max > 0)");
                    table.CheckConstraint("ck_promo_campaigns_percentage_rule", "rule = 'payment_to_merchant' OR reward_type = 'fixed'");
                    table.CheckConstraint("ck_promo_campaigns_period", "ends_at IS NULL OR ends_at > starts_at");
                    table.CheckConstraint("ck_promo_campaigns_reward_type", "reward_type IN ('fixed','percentage')");
                    table.CheckConstraint("ck_promo_campaigns_rule", "rule IN ('payment_to_merchant','daily_payment_total')");
                    table.CheckConstraint("ck_promo_campaigns_threshold", "(rule = 'daily_payment_total') = (threshold_amount IS NOT NULL) AND (threshold_amount IS NULL OR threshold_amount > 0)");
                });

            migrationBuilder.CreateTable(
                name: "promo_campaign_merchants",
                columns: table => new
                {
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_promo_campaign_merchants", x => new { x.campaign_id, x.role, x.account_id });
                    table.CheckConstraint("ck_promo_campaign_merchants_role", "role IN ('trigger','scope')");
                    table.ForeignKey(
                        name: "fk_promo_campaign_merchants_account",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_promo_campaign_merchants_campaign",
                        column: x => x.campaign_id,
                        principalTable: "promo_campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_promo_grants_campaign",
                table: "promo_grants",
                column: "campaign_id",
                filter: "campaign_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_promo_grants_campaign",
                table: "promo_grants",
                sql: "campaign_id IS NULL OR funder = 'platform'");

            migrationBuilder.CreateIndex(
                name: "ix_promo_campaign_merchants_account",
                table: "promo_campaign_merchants",
                column: "account_id");

            migrationBuilder.AddForeignKey(
                name: "fk_promo_grants_campaign",
                table: "promo_grants",
                column: "campaign_id",
                principalTable: "promo_campaigns",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_promo_grants_campaign",
                table: "promo_grants");

            migrationBuilder.DropTable(
                name: "promo_campaign_evaluations");

            migrationBuilder.DropTable(
                name: "promo_campaign_merchants");

            migrationBuilder.DropTable(
                name: "promo_campaigns");

            migrationBuilder.DropIndex(
                name: "ix_promo_grants_campaign",
                table: "promo_grants");

            migrationBuilder.DropCheckConstraint(
                name: "ck_promo_grants_campaign",
                table: "promo_grants");

            migrationBuilder.DropColumn(
                name: "campaign_id",
                table: "promo_grants");
        }
    }
}
