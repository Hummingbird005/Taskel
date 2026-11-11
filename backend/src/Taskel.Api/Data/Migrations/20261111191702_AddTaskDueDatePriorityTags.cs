using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskel.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskDueDatePriorityTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "DueDate",
                table: "tasks",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "tasks",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<List<string>>(
                name: "Tags",
                table: "tasks",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DueDate",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "Tags",
                table: "tasks");
        }
    }
}
