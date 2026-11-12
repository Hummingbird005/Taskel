using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskel.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAssignmentAndDirectoryAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DirectoryId",
                table: "users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "users",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssigneeId",
                table: "tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_DirectoryId",
                table: "users",
                column: "DirectoryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tasks_AssigneeId",
                table: "tasks",
                column: "AssigneeId");

            migrationBuilder.AddForeignKey(
                name: "FK_tasks_users_AssigneeId",
                table: "tasks",
                column: "AssigneeId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_tasks_users_AssigneeId",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "IX_users_DirectoryId",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_tasks_AssigneeId",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "DirectoryId",
                table: "users");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "users");

            migrationBuilder.DropColumn(
                name: "AssigneeId",
                table: "tasks");
        }
    }
}
