using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace L1bre.Journal.Data.Migrations
{
    /// <inheritdoc />
    public partial class TradeSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DepositCurrency",
                table: "TradingAccounts",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "HistoryStart",
                table: "TradingAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastSyncAt",
                table: "TradingAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastSyncError",
                table: "TradingAccounts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastSyncErrorAt",
                table: "TradingAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MoneyDigits",
                table: "TradingAccounts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SyncRequestedAt",
                table: "TradingAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SyncedUntil",
                table: "TradingAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TradeDeals",
                columns: table => new
                {
                    TradingAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    DealId = table.Column<long>(type: "bigint", nullable: false),
                    PositionId = table.Column<long>(type: "bigint", nullable: false),
                    OrderId = table.Column<long>(type: "bigint", nullable: false),
                    SymbolId = table.Column<long>(type: "bigint", nullable: false),
                    Side = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    VolumeCents = table.Column<long>(type: "bigint", nullable: false),
                    ExecutedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    Commission = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    IsClosing = table.Column<bool>(type: "boolean", nullable: false),
                    ClosedVolumeCents = table.Column<long>(type: "bigint", nullable: false),
                    GrossProfit = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    Swap = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    CloseCommission = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    ConversionFee = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    RawJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeDeals", x => new { x.TradingAccountId, x.DealId });
                    table.ForeignKey(
                        name: "FK_TradeDeals_TradingAccounts_TradingAccountId",
                        column: x => x.TradingAccountId,
                        principalTable: "TradingAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Trades",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TradingAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    PositionId = table.Column<long>(type: "bigint", nullable: false),
                    SymbolId = table.Column<long>(type: "bigint", nullable: false),
                    SymbolName = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Digits = table.Column<int>(type: "integer", nullable: false),
                    PipSize = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    Direction = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    VolumeCents = table.Column<long>(type: "bigint", nullable: false),
                    LotSizeCents = table.Column<long>(type: "bigint", nullable: true),
                    OpeningOrderId = table.Column<long>(type: "bigint", nullable: false),
                    OrderType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Comment = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Origin = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Technique = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Timeframe = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    Status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    OpenedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EntryPrice = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    StopLoss = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: true),
                    TakeProfit = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: true),
                    ClosedVolumeCents = table.Column<long>(type: "bigint", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExitPrice = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: true),
                    GrossProfit = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    Swap = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    Commission = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    ConversionFee = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    NetProfit = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    Pips = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: true),
                    CandlesStatus = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    CandlesAttempts = table.Column<int>(type: "integer", nullable: false),
                    CandlesError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ImportedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trades", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Trades_TradingAccounts_TradingAccountId",
                        column: x => x.TradingAccountId,
                        principalTable: "TradingAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TradeCandles",
                columns: table => new
                {
                    TradeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Timeframe = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    OpenTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Open = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    High = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    Low = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    Close = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    Volume = table.Column<long>(type: "bigint", nullable: false),
                    IsCut = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeCandles", x => new { x.TradeId, x.Timeframe, x.OpenTime });
                    table.ForeignKey(
                        name: "FK_TradeCandles_Trades_TradeId",
                        column: x => x.TradeId,
                        principalTable: "Trades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TradeDeals_TradingAccountId_PositionId",
                table: "TradeDeals",
                columns: new[] { "TradingAccountId", "PositionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Trades_TradingAccountId_OpenedAt",
                table: "Trades",
                columns: new[] { "TradingAccountId", "OpenedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Trades_TradingAccountId_PositionId",
                table: "Trades",
                columns: new[] { "TradingAccountId", "PositionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TradeCandles");

            migrationBuilder.DropTable(
                name: "TradeDeals");

            migrationBuilder.DropTable(
                name: "Trades");

            migrationBuilder.DropColumn(
                name: "DepositCurrency",
                table: "TradingAccounts");

            migrationBuilder.DropColumn(
                name: "HistoryStart",
                table: "TradingAccounts");

            migrationBuilder.DropColumn(
                name: "LastSyncAt",
                table: "TradingAccounts");

            migrationBuilder.DropColumn(
                name: "LastSyncError",
                table: "TradingAccounts");

            migrationBuilder.DropColumn(
                name: "LastSyncErrorAt",
                table: "TradingAccounts");

            migrationBuilder.DropColumn(
                name: "MoneyDigits",
                table: "TradingAccounts");

            migrationBuilder.DropColumn(
                name: "SyncRequestedAt",
                table: "TradingAccounts");

            migrationBuilder.DropColumn(
                name: "SyncedUntil",
                table: "TradingAccounts");
        }
    }
}
