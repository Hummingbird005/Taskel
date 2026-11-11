using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskel.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDashboardSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "DashboardWidgets",
                table: "users",
                type: "text[]",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShowDueAlert",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DashboardWidgets",
                table: "users");

            migrationBuilder.DropColumn(
                name: "ShowDueAlert",
                table: "users");
        }
    }
}
