using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchAndSummaryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RequestLogs_CreatedAt",
                table: "RequestLogs");

            migrationBuilder.DropIndex(
                name: "IX_Issues_Status",
                table: "Issues");

            migrationBuilder.CreateIndex(
                name: "IX_RequestLogs_CreatedAt",
                table: "RequestLogs",
                column: "CreatedAt")
                .Annotation("Npgsql:IndexInclude", new[] { "Method", "StatusCode", "IpAddress", "Path" });

            migrationBuilder.CreateIndex(
                name: "IX_Issues_ResolvedAt",
                table: "Issues",
                column: "ResolvedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Issues_Status",
                table: "Issues",
                column: "Status")
                .Annotation("Npgsql:IndexInclude", new[] { "ResponseDueAt", "ResolveDueAt", "FirstResponseAt", "ReporterUserId", "AssigneeId" });

            migrationBuilder.CreateIndex(
                name: "IX_Issues_Title",
                table: "Issues",
                column: "Title")
                .Annotation("Npgsql:IndexInclude", new[] { "ReporterName", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RequestLogs_CreatedAt",
                table: "RequestLogs");

            migrationBuilder.DropIndex(
                name: "IX_Issues_ResolvedAt",
                table: "Issues");

            migrationBuilder.DropIndex(
                name: "IX_Issues_Status",
                table: "Issues");

            migrationBuilder.DropIndex(
                name: "IX_Issues_Title",
                table: "Issues");

            migrationBuilder.CreateIndex(
                name: "IX_RequestLogs_CreatedAt",
                table: "RequestLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Issues_Status",
                table: "Issues",
                column: "Status");
        }
    }
}
