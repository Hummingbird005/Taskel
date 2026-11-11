using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskel.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskEmailSourceAndCompletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAt",
                table: "tasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompletionResponse",
                table: "tasks",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceEmailAddress",
                table: "tasks",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceEmailMessageId",
                table: "tasks",
                type: "character varying(998)",
                maxLength: 998,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceEmailSubject",
                table: "tasks",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "CompletionResponse",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "SourceEmailAddress",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "SourceEmailMessageId",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "SourceEmailSubject",
                table: "tasks");
        }
    }
}
