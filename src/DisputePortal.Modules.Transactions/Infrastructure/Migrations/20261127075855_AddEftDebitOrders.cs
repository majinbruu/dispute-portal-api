using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DisputePortal.Modules.Transactions.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEftDebitOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "mandate_reference",
                schema: "transactions",
                table: "debit_orders",
                type: "character varying(35)",
                maxLength: 35,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(35)",
                oldMaxLength: 35);

            migrationBuilder.AlterColumn<Guid>(
                name: "mandate_id",
                schema: "transactions",
                table: "debit_orders",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "contract_reference",
                schema: "transactions",
                table: "debit_orders",
                type: "character varying(35)",
                maxLength: 35,
                nullable: false,
                defaultValue: "");

            // Every debit order recorded so far was collected under a mandate, which is where its contract
            // reference comes from. The empty default was only there to add the column to a table with rows.
            migrationBuilder.Sql(
                """
                UPDATE transactions.debit_orders AS d
                SET contract_reference = m.contract_reference
                FROM transactions.mandates AS m
                WHERE m.id = d.mandate_id;
                """);

            migrationBuilder.Sql("ALTER TABLE transactions.debit_orders ALTER COLUMN contract_reference DROP DEFAULT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The previous model requires a mandate on every debit order, so EFT debit orders do not survive
            // going back. Deleting the shared row removes the debit order row with it.
            migrationBuilder.Sql(
                """
                DELETE FROM transactions.transactions
                WHERE id IN (SELECT id FROM transactions.debit_orders WHERE mandate_type = 'Eft');
                """);

            migrationBuilder.DropColumn(
                name: "contract_reference",
                schema: "transactions",
                table: "debit_orders");

            migrationBuilder.AlterColumn<string>(
                name: "mandate_reference",
                schema: "transactions",
                table: "debit_orders",
                type: "character varying(35)",
                maxLength: 35,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(35)",
                oldMaxLength: 35,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "mandate_id",
                schema: "transactions",
                table: "debit_orders",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
