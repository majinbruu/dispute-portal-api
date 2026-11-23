using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DisputePortal.Modules.Disputes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDisputeComment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "comment",
                schema: "disputes",
                table: "disputes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "comment",
                schema: "disputes",
                table: "disputes",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }
    }
}
