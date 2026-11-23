using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DisputePortal.Modules.DebitOrders.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameCollectionStatuses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The status is stored as text. Existing rows are moved to the pacs.002 wording.
            migrationBuilder.Sql("UPDATE debit_orders.debit_orders SET status = 'Accepted' WHERE status = 'Successful';");
            migrationBuilder.Sql("UPDATE debit_orders.debit_orders SET status = 'Rejected' WHERE status = 'Failed';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE debit_orders.debit_orders SET status = 'Successful' WHERE status = 'Accepted';");
            migrationBuilder.Sql("UPDATE debit_orders.debit_orders SET status = 'Failed' WHERE status = 'Rejected';");
        }
    }
}
