using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gst.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MoveToDbo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.RenameTable(
                name: "tb_users",
                schema: "gst",
                newName: "tb_users",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "tb_otp_codes",
                schema: "gst",
                newName: "tb_otp_codes",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "tb_companies",
                schema: "gst",
                newName: "tb_companies",
                newSchema: "dbo");

            // gst schema khali ho gaya to hata do
            migrationBuilder.Sql(
                "IF SCHEMA_ID(N'gst') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.objects WHERE schema_id = SCHEMA_ID(N'gst')) EXEC(N'DROP SCHEMA [gst]');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "gst");

            migrationBuilder.RenameTable(
                name: "tb_users",
                schema: "dbo",
                newName: "tb_users",
                newSchema: "gst");

            migrationBuilder.RenameTable(
                name: "tb_otp_codes",
                schema: "dbo",
                newName: "tb_otp_codes",
                newSchema: "gst");

            migrationBuilder.RenameTable(
                name: "tb_companies",
                schema: "dbo",
                newName: "tb_companies",
                newSchema: "gst");
        }
    }
}
