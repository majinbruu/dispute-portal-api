using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DisputePortal.Modules.Disputes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GeneraliseDisputesToTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "return_payload",
                schema: "disputes",
                table: "disputes",
                newName: "scheme_payload");

            migrationBuilder.RenameColumn(
                name: "original_transaction_id",
                schema: "disputes",
                table: "disputes",
                newName: "scheme_reference");

            migrationBuilder.RenameColumn(
                name: "debtor_account_masked",
                schema: "disputes",
                table: "disputes",
                newName: "account_masked");

            migrationBuilder.RenameColumn(
                name: "debit_order_id",
                schema: "disputes",
                table: "disputes",
                newName: "transaction_id");

            migrationBuilder.RenameColumn(
                name: "creditor_name",
                schema: "disputes",
                table: "disputes",
                newName: "counterparty_name");

            migrationBuilder.RenameColumn(
                name: "action_date",
                schema: "disputes",
                table: "disputes",
                newName: "transaction_date");

            migrationBuilder.RenameIndex(
                name: "ix_disputes_debit_order_id",
                schema: "disputes",
                table: "disputes",
                newName: "ix_disputes_transaction_id");

            migrationBuilder.AlterColumn<string>(
                name: "original_end_to_end_id",
                schema: "disputes",
                table: "disputes",
                type: "character varying(35)",
                maxLength: 35,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(35)",
                oldMaxLength: 35);

            migrationBuilder.AlterColumn<string>(
                name: "mandate_type",
                schema: "disputes",
                table: "disputes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "mandate_reference",
                schema: "disputes",
                table: "disputes",
                type: "character varying(35)",
                maxLength: 35,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(35)",
                oldMaxLength: 35);

            migrationBuilder.AddColumn<string>(
                name: "card_scheme",
                schema: "disputes",
                table: "disputes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "posting_date",
                schema: "disputes",
                table: "disputes",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reason_id",
                schema: "disputes",
                table: "disputes",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "transaction_type",
                schema: "disputes",
                table: "disputes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            // Every dispute raised so far is a debit order dispute. Its reason was stored as the ISO code only,
            // and each of the five codes maps to exactly one reason id.
            migrationBuilder.Sql(
                """
                UPDATE disputes.disputes
                SET transaction_type = 'DebitOrder',
                    reason_id = CASE reason_code
                        WHEN 'MD01' THEN 'not_authorised'
                        WHEN 'AM09' THEN 'incorrect_amount'
                        WHEN 'DT01' THEN 'wrong_date'
                        WHEN 'MD06' THEN 'cancelled'
                        WHEN 'AM05' THEN 'duplicate'
                    END;
                """);

            // The empty defaults were only there to add the columns to a table that already has rows.
            migrationBuilder.Sql(
                """
                ALTER TABLE disputes.disputes
                    ALTER COLUMN reason_id DROP DEFAULT,
                    ALTER COLUMN transaction_type DROP DEFAULT;
                """);

            // Outbox messages are stored as JSON in the shape of the DisputeStatusChanged contract, which changed
            // with this migration. Messages still waiting to be delivered must be readable by the new code.
            migrationBuilder.Sql(
                """
                UPDATE disputes.outbox_messages
                SET payload = (payload - 'CreditorName')
                    || jsonb_build_object('CounterpartyName', payload -> 'CreditorName', 'TransactionType', 'DebitOrder')
                WHERE type = 'DisputeStatusChanged' AND jsonb_exists(payload, 'CreditorName');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The previous model can only hold debit order disputes, so card disputes do not survive going back.
            migrationBuilder.Sql("DELETE FROM disputes.disputes WHERE transaction_type = 'Card';");

            migrationBuilder.Sql(
                """
                UPDATE disputes.outbox_messages
                SET payload = (payload - 'CounterpartyName' - 'TransactionType')
                    || jsonb_build_object('CreditorName', payload -> 'CounterpartyName')
                WHERE type = 'DisputeStatusChanged' AND jsonb_exists(payload, 'CounterpartyName');
                """);

            migrationBuilder.DropColumn(
                name: "card_scheme",
                schema: "disputes",
                table: "disputes");

            migrationBuilder.DropColumn(
                name: "posting_date",
                schema: "disputes",
                table: "disputes");

            migrationBuilder.DropColumn(
                name: "reason_id",
                schema: "disputes",
                table: "disputes");

            migrationBuilder.DropColumn(
                name: "transaction_type",
                schema: "disputes",
                table: "disputes");

            migrationBuilder.RenameColumn(
                name: "transaction_id",
                schema: "disputes",
                table: "disputes",
                newName: "debit_order_id");

            migrationBuilder.RenameColumn(
                name: "transaction_date",
                schema: "disputes",
                table: "disputes",
                newName: "action_date");

            migrationBuilder.RenameColumn(
                name: "scheme_reference",
                schema: "disputes",
                table: "disputes",
                newName: "original_transaction_id");

            migrationBuilder.RenameColumn(
                name: "scheme_payload",
                schema: "disputes",
                table: "disputes",
                newName: "return_payload");

            migrationBuilder.RenameColumn(
                name: "counterparty_name",
                schema: "disputes",
                table: "disputes",
                newName: "creditor_name");

            migrationBuilder.RenameColumn(
                name: "account_masked",
                schema: "disputes",
                table: "disputes",
                newName: "debtor_account_masked");

            migrationBuilder.RenameIndex(
                name: "ix_disputes_transaction_id",
                schema: "disputes",
                table: "disputes",
                newName: "ix_disputes_debit_order_id");

            migrationBuilder.AlterColumn<string>(
                name: "original_end_to_end_id",
                schema: "disputes",
                table: "disputes",
                type: "character varying(35)",
                maxLength: 35,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(35)",
                oldMaxLength: 35,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "mandate_type",
                schema: "disputes",
                table: "disputes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "mandate_reference",
                schema: "disputes",
                table: "disputes",
                type: "character varying(35)",
                maxLength: 35,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(35)",
                oldMaxLength: 35,
                oldNullable: true);
        }
    }
}
