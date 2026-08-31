using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FiapGames.Users.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRbacAndGoogleOidc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "PasswordHash",
                schema: "users",
                table: "users",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "GoogleSubjectId",
                schema: "users",
                table: "users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_GoogleSubjectId",
                schema: "users",
                table: "users",
                column: "GoogleSubjectId",
                unique: true,
                filter: "\"GoogleSubjectId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_users_GoogleSubjectId",
                schema: "users",
                table: "users");

            migrationBuilder.DropColumn(
                name: "GoogleSubjectId",
                schema: "users",
                table: "users");

            migrationBuilder.AlterColumn<string>(
                name: "PasswordHash",
                schema: "users",
                table: "users",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }
    }
}
