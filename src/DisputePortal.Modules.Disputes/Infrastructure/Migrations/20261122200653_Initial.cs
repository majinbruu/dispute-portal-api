using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DisputePortal.Modules.Disputes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "disputes");

            migrationBuilder.CreateTable(
                name: "disputes",
                schema: "disputes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    debit_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_transaction_id = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: false),
                    original_end_to_end_id = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    action_date = table.Column<DateOnly>(type: "date", nullable: false),
                    mandate_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    mandate_reference = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: false),
                    creditor_name = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    debtor_account_masked = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: false),
                    reason_code = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_to_scheme_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    outcome_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    return_payload = table.Column<string>(type: "jsonb", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_disputes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "idempotency_keys",
                schema: "disputes",
                columns: table => new
                {
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    request_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    dispute_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_idempotency_keys", x => new { x.customer_id, x.key });
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "disputes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "dispute_events",
                schema: "disputes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dispute_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dispute_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_dispute_events_disputes_dispute_id",
                        column: x => x.dispute_id,
                        principalSchema: "disputes",
                        principalTable: "disputes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_dispute_events_dispute_id_occurred_at",
                schema: "disputes",
                table: "dispute_events",
                columns: new[] { "dispute_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_disputes_customer_id_submitted_at",
                schema: "disputes",
                table: "disputes",
                columns: new[] { "customer_id", "submitted_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_disputes_debit_order_id",
                schema: "disputes",
                table: "disputes",
                column: "debit_order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_disputes_reference",
                schema: "disputes",
                table: "disputes",
                column: "reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_disputes_status_submitted_at",
                schema: "disputes",
                table: "disputes",
                columns: new[] { "status", "submitted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_next_attempt_at_occurred_at",
                schema: "disputes",
                table: "outbox_messages",
                columns: new[] { "next_attempt_at", "occurred_at" },
                filter: "processed_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dispute_events",
                schema: "disputes");

            migrationBuilder.DropTable(
                name: "idempotency_keys",
                schema: "disputes");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "disputes");

            migrationBuilder.DropTable(
                name: "disputes",
                schema: "disputes");
        }
    }
}
