using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskel.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAnswerFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AnswerMessageId",
                table: "task_attachments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WithAnswer",
                table: "task_attachments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_task_attachments_AnswerMessageId",
                table: "task_attachments",
                column: "AnswerMessageId");

            migrationBuilder.AddForeignKey(
                name: "FK_task_attachments_task_messages_AnswerMessageId",
                table: "task_attachments",
                column: "AnswerMessageId",
                principalTable: "task_messages",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_task_attachments_task_messages_AnswerMessageId",
                table: "task_attachments");

            migrationBuilder.DropIndex(
                name: "IX_task_attachments_AnswerMessageId",
                table: "task_attachments");

            migrationBuilder.DropColumn(
                name: "AnswerMessageId",
                table: "task_attachments");

            migrationBuilder.DropColumn(
                name: "WithAnswer",
                table: "task_attachments");
        }
    }
}
