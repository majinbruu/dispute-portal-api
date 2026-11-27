using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DisputePortal.Modules.Disputes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewWithdrawalAndComments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_disputes_transaction_id",
                schema: "disputes",
                table: "disputes");

            migrationBuilder.AddColumn<Guid>(
                name: "actor_subject_id",
                schema: "disputes",
                table: "dispute_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "actor_type",
                schema: "disputes",
                table: "dispute_events",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "System");

            // Events recorded before actors existed: the customer raised the dispute, and the worker did everything after that.
            migrationBuilder.Sql(
                """
                UPDATE disputes.dispute_events AS e
                SET actor_type = 'Customer', actor_subject_id = d.customer_id
                FROM disputes.disputes AS d
                WHERE d.id = e.dispute_id AND e.status = 'Submitted';
                """);

            migrationBuilder.CreateTable(
                name: "dispute_comments",
                schema: "disputes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dispute_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    author_subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dispute_comments", x => x.id);
                    table.ForeignKey(
                        name: "fk_dispute_comments_disputes_dispute_id",
                        column: x => x.dispute_id,
                        principalSchema: "disputes",
                        principalTable: "disputes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_disputes_transaction_id",
                schema: "disputes",
                table: "disputes",
                column: "transaction_id",
                unique: true,
                filter: "status <> 'Withdrawn'");

            migrationBuilder.CreateIndex(
                name: "ix_dispute_comments_dispute_id_created_at",
                schema: "disputes",
                table: "dispute_comments",
                columns: new[] { "dispute_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dispute_comments",
                schema: "disputes");

            migrationBuilder.DropIndex(
                name: "ix_disputes_transaction_id",
                schema: "disputes",
                table: "disputes");

            migrationBuilder.DropColumn(
                name: "actor_subject_id",
                schema: "disputes",
                table: "dispute_events");

            migrationBuilder.DropColumn(
                name: "actor_type",
                schema: "disputes",
                table: "dispute_events");

            migrationBuilder.CreateIndex(
                name: "ix_disputes_transaction_id",
                schema: "disputes",
                table: "disputes",
                column: "transaction_id",
                unique: true);
        }
    }
}
