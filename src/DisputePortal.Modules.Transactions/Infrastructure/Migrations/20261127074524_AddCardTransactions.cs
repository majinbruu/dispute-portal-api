using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DisputePortal.Modules.Transactions.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCardTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "card_transactions",
                schema: "transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    card_number_masked = table.Column<string>(type: "character varying(19)", maxLength: 19, nullable: false),
                    scheme = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    merchant_city = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    merchant_country = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    merchant_category_code = table.Column<string>(type: "character(4)", fixedLength: true, maxLength: 4, nullable: false),
                    transaction_date = table.Column<DateOnly>(type: "date", nullable: false),
                    posting_date = table.Column<DateOnly>(type: "date", nullable: true),
                    authorisation_code = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    acquirer_reference_number = table.Column<string>(type: "character varying(23)", maxLength: 23, nullable: false),
                    entry_mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    verification = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    refunded_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_card_transactions", x => x.id);
                    table.ForeignKey(
                        name: "fk_card_transactions_transactions_id",
                        column: x => x.id,
                        principalSchema: "transactions",
                        principalTable: "transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_card_transactions_acquirer_reference_number",
                schema: "transactions",
                table: "card_transactions",
                column: "acquirer_reference_number",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The shared half of each card transaction lives in the base table and has to go too.
            migrationBuilder.Sql("DELETE FROM transactions.transactions WHERE type = 'Card';");

            migrationBuilder.DropTable(
                name: "card_transactions",
                schema: "transactions");
        }
    }
}
