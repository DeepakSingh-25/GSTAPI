using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gst.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OtpUnconsumedUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_tb_otp_codes_UserId_Purpose_Unconsumed",
                schema: "dbo",
                table: "tb_otp_codes",
                columns: new[] { "UserId", "Purpose" },
                unique: true,
                filter: "[ConsumedOn] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_tb_otp_codes_UserId_Purpose_Unconsumed",
                schema: "dbo",
                table: "tb_otp_codes");
        }
    }
}
