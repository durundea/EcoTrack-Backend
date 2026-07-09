using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EcoTrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRecyclingTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductConversions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecyclingBatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SyncedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SyncRunId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SyncedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductConversions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RecyclingBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SegregationBatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    PickupTaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceCategory = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SourceWeightKg = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Stage = table.Column<string>(type: "text", nullable: false),
                    OutputProduct = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    OutputQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    InventoryUpdated = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecyclingBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RecyclingBatchStageHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Stage = table.Column<string>(type: "text", nullable: false),
                    AtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecyclingBatchId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecyclingBatchStageHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecyclingBatchStageHistory_RecyclingBatches_RecyclingBatchId",
                        column: x => x.RecyclingBatchId,
                        principalTable: "RecyclingBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductConversions_RecyclingBatchId",
                table: "ProductConversions",
                column: "RecyclingBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductConversions_SyncedAtUtc",
                table: "ProductConversions",
                column: "SyncedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RecyclingBatches_PickupTaskId",
                table: "RecyclingBatches",
                column: "PickupTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_RecyclingBatches_SegregationBatchId",
                table: "RecyclingBatches",
                column: "SegregationBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_RecyclingBatches_Stage",
                table: "RecyclingBatches",
                column: "Stage");

            migrationBuilder.CreateIndex(
                name: "IX_RecyclingBatchStageHistory_RecyclingBatchId",
                table: "RecyclingBatchStageHistory",
                column: "RecyclingBatchId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductConversions");

            migrationBuilder.DropTable(
                name: "RecyclingBatchStageHistory");

            migrationBuilder.DropTable(
                name: "RecyclingBatches");
        }
    }
}
