using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FaxMonitor.Migrations
{
    /// <inheritdoc />
    public partial class RecipientInfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RecipientName",
                table: "Job",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecipientNumber",
                table: "Job",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RecipientName",
                table: "Job");

            migrationBuilder.DropColumn(
                name: "RecipientNumber",
                table: "Job");
        }
    }
}
