using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DisputePortal.Modules.Disputes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IndexIdempotencyKeyCreatedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_idempotency_keys_created_at",
                schema: "disputes",
                table: "idempotency_keys",
                column: "created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_idempotency_keys_created_at",
                schema: "disputes",
                table: "idempotency_keys");
        }
    }
}
