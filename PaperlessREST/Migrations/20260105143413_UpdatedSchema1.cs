using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaperlessREST.Migrations
{
    /// <inheritdoc />
    public partial class UpdatedSchema1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Documents_CurrentVersionId",
                table: "Documents");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_CurrentVersionId",
                table: "Documents",
                column: "CurrentVersionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Documents_CurrentVersionId",
                table: "Documents");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_CurrentVersionId",
                table: "Documents",
                column: "CurrentVersionId",
                unique: true);
        }
    }
}
