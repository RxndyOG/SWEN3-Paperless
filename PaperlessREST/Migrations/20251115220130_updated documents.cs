using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaperlessREST.Migrations
{
    /// <inheritdoc />
    public partial class updateddocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SummarizedContent",
                table: "Documents",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SummarizedContent",
                table: "Documents");
        }
    }
}
