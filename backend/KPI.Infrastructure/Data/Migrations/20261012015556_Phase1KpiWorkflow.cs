using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase1KpiWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ClosedAt",
                table: "Kpis",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClosedById",
                table: "Kpis",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectReason",
                table: "Kpis",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedAt",
                table: "Kpis",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SubmittedById",
                table: "Kpis",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Kpis",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClosedAt",
                table: "Kpis");

            migrationBuilder.DropColumn(
                name: "ClosedById",
                table: "Kpis");

            migrationBuilder.DropColumn(
                name: "RejectReason",
                table: "Kpis");

            migrationBuilder.DropColumn(
                name: "SubmittedAt",
                table: "Kpis");

            migrationBuilder.DropColumn(
                name: "SubmittedById",
                table: "Kpis");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Kpis");
        }
    }
}
