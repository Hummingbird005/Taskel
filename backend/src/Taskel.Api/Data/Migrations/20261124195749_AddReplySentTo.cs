using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskel.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReplySentTo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SentTo",
                table: "task_messages",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SentTo",
                table: "task_messages");
        }
    }
}
