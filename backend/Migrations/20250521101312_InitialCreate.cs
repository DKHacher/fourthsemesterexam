using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "storeddata",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(999)", maxLength: 999, nullable: false),
                    deviceid = table.Column<string>(type: "character varying(999)", maxLength: 999, nullable: true),
                    linktopicture = table.Column<string>(type: "character varying(999)", maxLength: 999, nullable: true),
                    date = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("storeddata_pkey", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "storeddata");
        }
    }
}
