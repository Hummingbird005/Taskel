using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskel.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUsersAndTaskOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Tasks created before authentication existed have no owner and cannot satisfy
            // the new foreign key, so they are removed.
            migrationBuilder.Sql("DELETE FROM tasks;");

            migrationBuilder.DropIndex(
                name: "IX_tasks_CreatedAt",
                table: "tasks");

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "tasks",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tasks_UserId_CreatedAt",
                table: "tasks",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_users_Email",
                table: "users",
                column: "Email",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_tasks_users_UserId",
                table: "tasks",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_tasks_users_UserId",
                table: "tasks");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropIndex(
                name: "IX_tasks_UserId_CreatedAt",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "tasks");

            migrationBuilder.CreateIndex(
                name: "IX_tasks_CreatedAt",
                table: "tasks",
                column: "CreatedAt");
        }
    }
}
