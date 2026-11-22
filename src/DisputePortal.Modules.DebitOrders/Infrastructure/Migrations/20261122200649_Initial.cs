using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DisputePortal.Modules.DebitOrders.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "debit_orders");

            migrationBuilder.CreateTable(
                name: "mandates",
                schema: "debit_orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mandate_reference = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: false),
                    contract_reference = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    creditor_name = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    creditor_short_name = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    instalment_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    maximum_collection_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    frequency = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    collection_day = table.Column<int>(type: "integer", nullable: false),
                    first_collection_date = table.Column<DateOnly>(type: "date", nullable: false),
                    date_adjustment_allowed = table.Column<bool>(type: "boolean", nullable: false),
                    tracking_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    authentication_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    authenticated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mandates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "debit_orders",
                schema: "debit_orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mandate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mandate_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    mandate_reference = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: false),
                    transaction_id = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: false),
                    end_to_end_id = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    action_date = table.Column<DateOnly>(type: "date", nullable: false),
                    sequence_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    creditor_name = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    creditor_short_name = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    statement_text = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    debtor_account_masked = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status_reason_code = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    is_disputable_under_mandate = table.Column<bool>(type: "boolean", nullable: false),
                    disputability_reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    last_dispute_date = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_debit_orders", x => x.id);
                    table.ForeignKey(
                        name: "fk_debit_orders_mandates_mandate_id",
                        column: x => x.mandate_id,
                        principalSchema: "debit_orders",
                        principalTable: "mandates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_debit_orders_customer_id_action_date",
                schema: "debit_orders",
                table: "debit_orders",
                columns: new[] { "customer_id", "action_date" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_debit_orders_mandate_id",
                schema: "debit_orders",
                table: "debit_orders",
                column: "mandate_id");

            migrationBuilder.CreateIndex(
                name: "ix_debit_orders_transaction_id",
                schema: "debit_orders",
                table: "debit_orders",
                column: "transaction_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mandates_customer_id_type",
                schema: "debit_orders",
                table: "mandates",
                columns: new[] { "customer_id", "type" });

            migrationBuilder.CreateIndex(
                name: "ix_mandates_mandate_reference",
                schema: "debit_orders",
                table: "mandates",
                column: "mandate_reference",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "debit_orders",
                schema: "debit_orders");

            migrationBuilder.DropTable(
                name: "mandates",
                schema: "debit_orders");
        }
    }
}
