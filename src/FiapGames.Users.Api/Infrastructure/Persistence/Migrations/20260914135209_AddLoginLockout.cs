using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FiapGames.Users.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLoginLockout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FailedLoginAttempts",
                schema: "users",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockedUntilUtc",
                schema: "users",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FailedLoginAttempts",
                schema: "users",
                table: "users");

            migrationBuilder.DropColumn(
                name: "LockedUntilUtc",
                schema: "users",
                table: "users");
        }
    }
}
