using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelPrinter.Migrations
{
    /// <inheritdoc />
    public partial class AddIsRawDataToPrintJob : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsRawData",
                table: "PrintJobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsRawData",
                table: "PrintJobs");
        }
    }
}
