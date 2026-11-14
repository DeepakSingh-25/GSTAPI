using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gst.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuthRegister : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_tb_users_Email",
                schema: "gst",
                table: "tb_users");

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                schema: "gst",
                table: "tb_users",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EmailVerified",
                schema: "gst",
                table: "tb_users",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "FailedLoginCount",
                schema: "gst",
                table: "tb_users",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "FirstName",
                schema: "gst",
                table: "tb_users",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "JobRole",
                schema: "gst",
                table: "tb_users",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastName",
                schema: "gst",
                table: "tb_users",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "LockoutEnd",
                schema: "gst",
                table: "tb_users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MobileNo",
                schema: "gst",
                table: "tb_users",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OnboardingCompleted",
                schema: "gst",
                table: "tb_users",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PasswordChangedOn",
                schema: "gst",
                table: "tb_users",
                type: "datetime2",
                nullable: true);

            // Legacy rows fix: Admin -> Owner / Admin (EXEC se idempotent script me batch compile error nahi aata)
            migrationBuilder.Sql("EXEC(N'UPDATE [gst].[tb_users] SET [Role] = N''Owner / Admin'' WHERE [Role] = N''Admin'';')");

            // Legacy rows fix: FullName ko FirstName/LastName me todo, EmailVerified = 1
            migrationBuilder.Sql("EXEC(N'UPDATE [gst].[tb_users] SET [FirstName] = LEFT(CASE WHEN CHARINDEX(N'' '', [FullName]) > 0 THEN LEFT([FullName], CHARINDEX(N'' '', [FullName]) - 1) ELSE [FullName] END, 50), [LastName] = LEFT(CASE WHEN CHARINDEX(N'' '', [FullName]) > 0 THEN LTRIM(SUBSTRING([FullName], CHARINDEX(N'' '', [FullName]) + 1, 150)) ELSE N'''' END, 50), [EmailVerified] = 1 WHERE [FirstName] = N'''';')");

            migrationBuilder.CreateTable(
                name: "tb_companies",
                schema: "gst",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegalName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TradeName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Gstin = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    Pan = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    RegistrationType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    State = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    StateCode = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    BusinessSize = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Industry = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AddressLine1 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AddressLine2 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Pincode = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    Website = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BankName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AccountNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Ifsc = table.Column<string>(type: "nvarchar(11)", maxLength: 11, nullable: true),
                    Branch = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TermsAndConditions = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tb_companies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "tb_otp_codes",
                schema: "gst",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CodeHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ExpiresOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    VerifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConsumedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tb_otp_codes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tb_otp_codes_tb_users_UserId",
                        column: x => x.UserId,
                        principalSchema: "gst",
                        principalTable: "tb_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tb_users_CompanyId",
                schema: "gst",
                table: "tb_users",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_tb_users_Email",
                schema: "gst",
                table: "tb_users",
                column: "Email",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_tb_users_MobileNo",
                schema: "gst",
                table: "tb_users",
                column: "MobileNo",
                unique: true,
                filter: "[MobileNo] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_tb_companies_Gstin",
                schema: "gst",
                table: "tb_companies",
                column: "Gstin",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_tb_otp_codes_Email_Purpose_CreatedOn",
                schema: "gst",
                table: "tb_otp_codes",
                columns: new[] { "Email", "Purpose", "CreatedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_tb_otp_codes_UserId",
                schema: "gst",
                table: "tb_otp_codes",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_tb_users_tb_companies_CompanyId",
                schema: "gst",
                table: "tb_users",
                column: "CompanyId",
                principalSchema: "gst",
                principalTable: "tb_companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Role rename wapas (columns drop hone se pehle)
            migrationBuilder.Sql("EXEC(N'UPDATE [gst].[tb_users] SET [Role] = N''Admin'' WHERE [Role] = N''Owner / Admin'';')");

            migrationBuilder.DropForeignKey(
                name: "FK_tb_users_tb_companies_CompanyId",
                schema: "gst",
                table: "tb_users");

            migrationBuilder.DropTable(
                name: "tb_companies",
                schema: "gst");

            migrationBuilder.DropTable(
                name: "tb_otp_codes",
                schema: "gst");

            migrationBuilder.DropIndex(
                name: "IX_tb_users_CompanyId",
                schema: "gst",
                table: "tb_users");

            migrationBuilder.DropIndex(
                name: "IX_tb_users_Email",
                schema: "gst",
                table: "tb_users");

            migrationBuilder.DropIndex(
                name: "IX_tb_users_MobileNo",
                schema: "gst",
                table: "tb_users");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                schema: "gst",
                table: "tb_users");

            migrationBuilder.DropColumn(
                name: "EmailVerified",
                schema: "gst",
                table: "tb_users");

            migrationBuilder.DropColumn(
                name: "FailedLoginCount",
                schema: "gst",
                table: "tb_users");

            migrationBuilder.DropColumn(
                name: "FirstName",
                schema: "gst",
                table: "tb_users");

            migrationBuilder.DropColumn(
                name: "JobRole",
                schema: "gst",
                table: "tb_users");

            migrationBuilder.DropColumn(
                name: "LastName",
                schema: "gst",
                table: "tb_users");

            migrationBuilder.DropColumn(
                name: "LockoutEnd",
                schema: "gst",
                table: "tb_users");

            migrationBuilder.DropColumn(
                name: "MobileNo",
                schema: "gst",
                table: "tb_users");

            migrationBuilder.DropColumn(
                name: "OnboardingCompleted",
                schema: "gst",
                table: "tb_users");

            migrationBuilder.DropColumn(
                name: "PasswordChangedOn",
                schema: "gst",
                table: "tb_users");

            migrationBuilder.CreateIndex(
                name: "IX_tb_users_Email",
                schema: "gst",
                table: "tb_users",
                column: "Email",
                unique: true);
        }
    }
}
