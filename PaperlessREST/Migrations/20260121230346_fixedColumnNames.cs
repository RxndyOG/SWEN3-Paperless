using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaperlessREST.Migrations
{
    /// <inheritdoc />
    public partial class fixedColumnNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AccessStatistics_Documents_DocumentId",
                table: "AccessStatistics");

            migrationBuilder.RenameColumn(
                name: "DocumentId",
                table: "AccessStatistics",
                newName: "documentId");

            migrationBuilder.RenameColumn(
                name: "AccessDate",
                table: "AccessStatistics",
                newName: "accessDate");

            migrationBuilder.RenameColumn(
                name: "AccessCount",
                table: "AccessStatistics",
                newName: "accessCount");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "AccessStatistics",
                newName: "id");

            migrationBuilder.RenameIndex(
                name: "IX_AccessStatistics_DocumentId_AccessDate",
                table: "AccessStatistics",
                newName: "IX_AccessStatistics_documentId_accessDate");

            migrationBuilder.AddForeignKey(
                name: "FK_AccessStatistics_Documents_documentId",
                table: "AccessStatistics",
                column: "documentId",
                principalTable: "Documents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AccessStatistics_Documents_documentId",
                table: "AccessStatistics");

            migrationBuilder.RenameColumn(
                name: "documentId",
                table: "AccessStatistics",
                newName: "DocumentId");

            migrationBuilder.RenameColumn(
                name: "accessDate",
                table: "AccessStatistics",
                newName: "AccessDate");

            migrationBuilder.RenameColumn(
                name: "accessCount",
                table: "AccessStatistics",
                newName: "AccessCount");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "AccessStatistics",
                newName: "Id");

            migrationBuilder.RenameIndex(
                name: "IX_AccessStatistics_documentId_accessDate",
                table: "AccessStatistics",
                newName: "IX_AccessStatistics_DocumentId_AccessDate");

            migrationBuilder.AddForeignKey(
                name: "FK_AccessStatistics_Documents_DocumentId",
                table: "AccessStatistics",
                column: "DocumentId",
                principalTable: "Documents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
