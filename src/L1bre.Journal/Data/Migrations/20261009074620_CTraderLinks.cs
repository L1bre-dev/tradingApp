using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace L1bre.Journal.Data.Migrations
{
    /// <inheritdoc />
    public partial class CTraderLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CTraderLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    CtidUserId = table.Column<long>(type: "bigint", nullable: false),
                    AccessTokenEncrypted = table.Column<string>(type: "text", nullable: false),
                    RefreshTokenEncrypted = table.Column<string>(type: "text", nullable: false),
                    AccessTokenExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LinkedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TokenRefreshedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AccountsCheckedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LastErrorAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CTraderLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CTraderLinks_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TradingAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CTraderLinkId = table.Column<Guid>(type: "uuid", nullable: false),
                    CtidTraderAccountId = table.Column<long>(type: "bigint", nullable: false),
                    TraderLogin = table.Column<long>(type: "bigint", nullable: true),
                    BrokerName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsLive = table.Column<bool>(type: "boolean", nullable: false),
                    IsSelected = table.Column<bool>(type: "boolean", nullable: false),
                    IsAvailable = table.Column<bool>(type: "boolean", nullable: false),
                    AddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradingAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TradingAccounts_CTraderLinks_CTraderLinkId",
                        column: x => x.CTraderLinkId,
                        principalTable: "CTraderLinks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CTraderLinks_UserId_CtidUserId",
                table: "CTraderLinks",
                columns: new[] { "UserId", "CtidUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TradingAccounts_CTraderLinkId_CtidTraderAccountId",
                table: "TradingAccounts",
                columns: new[] { "CTraderLinkId", "CtidTraderAccountId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TradingAccounts");

            migrationBuilder.DropTable(
                name: "CTraderLinks");
        }
    }
}
