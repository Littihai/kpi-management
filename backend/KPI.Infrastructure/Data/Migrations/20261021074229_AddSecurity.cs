using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FailedLoginCount",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockedUntil",
                table: "Users",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PasswordChangedAt",
                table: "Users",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AuthLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    At = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Event = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    Reason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Detail = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PasswordHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PasswordHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PasswordHistory_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuthLogs_At",
                table: "AuthLogs",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_AuthLogs_Event_At",
                table: "AuthLogs",
                columns: new[] { "Event", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_AuthLogs_UserId_At",
                table: "AuthLogs",
                columns: new[] { "UserId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_PasswordHistory_UserId_CreatedAt",
                table: "PasswordHistory",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuthLogs");

            migrationBuilder.DropTable(
                name: "PasswordHistory");

            migrationBuilder.DropColumn(
                name: "FailedLoginCount",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LockedUntil",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PasswordChangedAt",
                table: "Users");
        }
    }
}
