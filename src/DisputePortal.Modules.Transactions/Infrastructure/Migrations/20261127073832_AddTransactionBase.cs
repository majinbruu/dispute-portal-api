using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DisputePortal.Modules.Transactions.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionBase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The columns every transaction shares move from debit_orders to a new base table. The base table is
            // created and filled first, so no debit order is ever without its shared columns.
            migrationBuilder.CreateTable(
                name: "transactions",
                schema: "transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    account_masked = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: false),
                    counterparty_name = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    is_disputable = table.Column<bool>(type: "boolean", nullable: false),
                    disputability_reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    last_dispute_date = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transactions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_transactions_customer_id_type_effective_date",
                schema: "transactions",
                table: "transactions",
                columns: new[] { "customer_id", "type", "effective_date" },
                descending: new[] { false, false, true });

            // A rejected collection used to be refused at read time. It is now part of the stored decision.
            migrationBuilder.Sql(
                """
                INSERT INTO transactions.transactions (
                    id, customer_id, type, account_masked, counterparty_name, amount, currency, effective_date,
                    is_disputable, disputability_reason, last_dispute_date, created_at)
                SELECT
                    id, customer_id, 'DebitOrder', debtor_account_masked, creditor_name, amount, currency, action_date,
                    status = 'Accepted' AND is_disputable_under_mandate,
                    CASE WHEN status = 'Accepted' THEN disputability_reason ELSE 'CollectionRejected' END,
                    last_dispute_date, created_at
                FROM transactions.debit_orders;
                """);

            migrationBuilder.DropIndex(
                name: "ix_debit_orders_customer_id_action_date",
                schema: "transactions",
                table: "debit_orders");

            migrationBuilder.DropColumn(
                name: "action_date",
                schema: "transactions",
                table: "debit_orders");

            migrationBuilder.DropColumn(
                name: "amount",
                schema: "transactions",
                table: "debit_orders");

            migrationBuilder.DropColumn(
                name: "created_at",
                schema: "transactions",
                table: "debit_orders");

            migrationBuilder.DropColumn(
                name: "creditor_name",
                schema: "transactions",
                table: "debit_orders");

            migrationBuilder.DropColumn(
                name: "currency",
                schema: "transactions",
                table: "debit_orders");

            migrationBuilder.DropColumn(
                name: "customer_id",
                schema: "transactions",
                table: "debit_orders");

            migrationBuilder.DropColumn(
                name: "debtor_account_masked",
                schema: "transactions",
                table: "debit_orders");

            migrationBuilder.DropColumn(
                name: "disputability_reason",
                schema: "transactions",
                table: "debit_orders");

            migrationBuilder.DropColumn(
                name: "is_disputable_under_mandate",
                schema: "transactions",
                table: "debit_orders");

            migrationBuilder.DropColumn(
                name: "last_dispute_date",
                schema: "transactions",
                table: "debit_orders");

            migrationBuilder.DropPrimaryKey(
                name: "pk_debit_orders",
                schema: "transactions",
                table: "debit_orders");

            migrationBuilder.AddPrimaryKey(
                name: "PK_debit_orders",
                schema: "transactions",
                table: "debit_orders",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_debit_orders_transactions_id",
                schema: "transactions",
                table: "debit_orders",
                column: "id",
                principalSchema: "transactions",
                principalTable: "transactions",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_debit_orders_transactions_id",
                schema: "transactions",
                table: "debit_orders");

            migrationBuilder.DropPrimaryKey(
                name: "PK_debit_orders",
                schema: "transactions",
                table: "debit_orders");

            migrationBuilder.AddColumn<DateOnly>(
                name: "action_date",
                schema: "transactions",
                table: "debit_orders",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<decimal>(
                name: "amount",
                schema: "transactions",
                table: "debit_orders",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "created_at",
                schema: "transactions",
                table: "debit_orders",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "creditor_name",
                schema: "transactions",
                table: "debit_orders",
                type: "character varying(140)",
                maxLength: 140,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "currency",
                schema: "transactions",
                table: "debit_orders",
                type: "character(3)",
                fixedLength: true,
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "customer_id",
                schema: "transactions",
                table: "debit_orders",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "debtor_account_masked",
                schema: "transactions",
                table: "debit_orders",
                type: "character varying(34)",
                maxLength: 34,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "disputability_reason",
                schema: "transactions",
                table: "debit_orders",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "is_disputable_under_mandate",
                schema: "transactions",
                table: "debit_orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "last_dispute_date",
                schema: "transactions",
                table: "debit_orders",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            // The old model had no CollectionRejected reason: a rejected collection kept its mandate decision,
            // which is no longer stored. It is rebuilt from the mandate type, which is close but not exact for
            // a rejected DebiCheck collection that was outside its mandate terms.
            migrationBuilder.Sql(
                """
                UPDATE transactions.debit_orders AS d
                SET customer_id = t.customer_id,
                    debtor_account_masked = t.account_masked,
                    creditor_name = t.counterparty_name,
                    amount = t.amount,
                    currency = t.currency,
                    action_date = t.effective_date,
                    is_disputable_under_mandate = CASE
                        WHEN t.disputability_reason = 'CollectionRejected' THEN d.mandate_type = 'Registered'
                        ELSE t.is_disputable END,
                    disputability_reason = CASE
                        WHEN t.disputability_reason <> 'CollectionRejected' THEN t.disputability_reason
                        WHEN d.mandate_type = 'Registered' THEN 'RegisteredMandate'
                        ELSE 'WithinMandateTerms' END,
                    last_dispute_date = t.last_dispute_date,
                    created_at = t.created_at
                FROM transactions.transactions AS t
                WHERE t.id = d.id;
                """);

            migrationBuilder.DropTable(
                name: "transactions",
                schema: "transactions");

            migrationBuilder.AddPrimaryKey(
                name: "pk_debit_orders",
                schema: "transactions",
                table: "debit_orders",
                column: "id");

            migrationBuilder.CreateIndex(
                name: "ix_debit_orders_customer_id_action_date",
                schema: "transactions",
                table: "debit_orders",
                columns: new[] { "customer_id", "action_date" },
                descending: new[] { false, true });
        }
    }
}
