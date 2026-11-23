using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskel.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPriorityLevel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PriorityLevel",
                table: "tasks",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PriorityLevel",
                table: "tasks");
        }
    }
}
