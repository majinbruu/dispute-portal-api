using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DisputePortal.Modules.Transactions.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MoveToTransactionsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "transactions");

            migrationBuilder.RenameTable(
                name: "mandates",
                schema: "debit_orders",
                newName: "mandates",
                newSchema: "transactions");

            migrationBuilder.RenameTable(
                name: "debit_orders",
                schema: "debit_orders",
                newName: "debit_orders",
                newSchema: "transactions");

            // The old schema is empty by now: its migration history table is moved before migrations run
            // (see TransactionsModule). PostgreSQL refuses to drop a schema that still holds anything.
            migrationBuilder.DropSchema(
                name: "debit_orders");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "debit_orders");

            migrationBuilder.RenameTable(
                name: "mandates",
                schema: "transactions",
                newName: "mandates",
                newSchema: "debit_orders");

            migrationBuilder.RenameTable(
                name: "debit_orders",
                schema: "transactions",
                newName: "debit_orders",
                newSchema: "debit_orders");
        }
    }
}
