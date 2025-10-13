using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MongoDB.Bson;

namespace Infrastructure.Migrations
{
    public partial class AddNotifications : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<ObjectId>(nullable: false),
                    RecipientId = table.Column<string>(nullable: false),
                    SenderId = table.Column<string>(nullable: true),
                    Type = table.Column<int>(nullable: false),
                    Content = table.Column<string>(nullable: false),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    IsRead = table.Column<bool>(nullable: false),
                    RelatedEntityId = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Notifications");
        }
    }
}
