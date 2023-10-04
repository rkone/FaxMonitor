using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FaxMonitor.Migrations
{
    /// <inheritdoc />
    public partial class AddServerJobIdIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Job_ServerJobId",
                table: "Job",
                column: "ServerJobId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Job_ServerJobId",
                table: "Job");
        }
    }
}
