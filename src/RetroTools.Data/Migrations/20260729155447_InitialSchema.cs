using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetroTools.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "platforms",
                columns: table => new
                {
                    Code = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Manufacturer = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Year = table.Column<int>(type: "int", nullable: false),
                    ColorCount = table.Column<int>(type: "int", nullable: false),
                    HasHardwareSprites = table.Column<bool>(type: "bit(1)", nullable: false),
                    HasProgrammablePalette = table.Column<bool>(type: "bit(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platforms", x => x.Code);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    DisplayName = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Email = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AvatarUrl = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedUtc = table.Column<DateTime>(type: "datetime(3)", nullable: false),
                    LastLoginUtc = table.Column<DateTime>(type: "datetime(3)", nullable: true),
                    IsDisabled = table.Column<bool>(type: "bit(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "platform_modes",
                columns: table => new
                {
                    Code = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PlatformCode = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(96)", maxLength: 96, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ScreenWidth = table.Column<int>(type: "int", nullable: false),
                    ScreenHeight = table.Column<int>(type: "int", nullable: false),
                    BitsPerPixel = table.Column<int>(type: "int", nullable: false),
                    PaletteSlots = table.Column<int>(type: "int", nullable: false),
                    MaxColorsPerCell = table.Column<int>(type: "int", nullable: false),
                    ColorScope = table.Column<int>(type: "int", nullable: false),
                    CellWidth = table.Column<int>(type: "int", nullable: false),
                    CellHeight = table.Column<int>(type: "int", nullable: false),
                    PixelAspectWidth = table.Column<int>(type: "int", nullable: false),
                    PixelAspectHeight = table.Column<int>(type: "int", nullable: false),
                    WidthAlignment = table.Column<int>(type: "int", nullable: false),
                    HeightAlignment = table.Column<int>(type: "int", nullable: false),
                    FixedWidth = table.Column<int>(type: "int", nullable: true),
                    FixedHeight = table.Column<int>(type: "int", nullable: true),
                    IsHardwareSprite = table.Column<bool>(type: "bit(1)", nullable: false),
                    SupportsMask = table.Column<bool>(type: "bit(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platform_modes", x => x.Code);
                    table.ForeignKey(
                        name: "FK_platform_modes_platforms_PlatformCode",
                        column: x => x.PlatformCode,
                        principalTable: "platforms",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "user_logins",
                columns: table => new
                {
                    Provider = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ProviderKey = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    LinkedUtc = table.Column<DateTime>(type: "datetime(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_logins", x => new { x.Provider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_user_logins_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "projects",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    OwnerId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PlatformCode = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ModeCode = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PaletteProfileId = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Visibility = table.Column<int>(type: "int", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime(3)", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime(3)", nullable: false),
                    RowVersion = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_projects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_projects_platform_modes_ModeCode",
                        column: x => x.ModeCode,
                        principalTable: "platform_modes",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_projects_platforms_PlatformCode",
                        column: x => x.PlatformCode,
                        principalTable: "platforms",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_projects_users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "palettes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ProjectId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_palettes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_palettes_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "sprite_groups",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ProjectId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sprite_groups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sprite_groups_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "spritemaps",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ProjectId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Columns = table.Column<int>(type: "int", nullable: false),
                    Rows = table.Column<int>(type: "int", nullable: false),
                    CellWidthPx = table.Column<int>(type: "int", nullable: false),
                    CellHeightPx = table.Column<int>(type: "int", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime(3)", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime(3)", nullable: false),
                    RowVersion = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_spritemaps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_spritemaps_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "palette_entries",
                columns: table => new
                {
                    PaletteId = table.Column<long>(type: "bigint", nullable: false),
                    SlotIndex = table.Column<int>(type: "int", nullable: false),
                    HardwareColorIndex = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_palette_entries", x => new { x.PaletteId, x.SlotIndex });
                    table.ForeignKey(
                        name: "FK_palette_entries_palettes_PaletteId",
                        column: x => x.PaletteId,
                        principalTable: "palettes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "sprites",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ProjectId = table.Column<long>(type: "bigint", nullable: false),
                    GroupId = table.Column<long>(type: "bigint", nullable: true),
                    Name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    WidthPx = table.Column<int>(type: "int", nullable: false),
                    HeightPx = table.Column<int>(type: "int", nullable: false),
                    PaletteId = table.Column<long>(type: "bigint", nullable: true),
                    HasMask = table.Column<bool>(type: "bit(1)", nullable: false),
                    MetaJson = table.Column<string>(type: "json", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime(3)", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime(3)", nullable: false),
                    RowVersion = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sprites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sprites_palettes_PaletteId",
                        column: x => x.PaletteId,
                        principalTable: "palettes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_sprites_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_sprites_sprite_groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "sprite_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "sprite_frames",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SpriteId = table.Column<long>(type: "bigint", nullable: false),
                    FrameIndex = table.Column<int>(type: "int", nullable: false),
                    DurationMs = table.Column<int>(type: "int", nullable: false),
                    PixelData = table.Column<byte[]>(type: "MEDIUMBLOB", nullable: false),
                    AttributeData = table.Column<byte[]>(type: "MEDIUMBLOB", nullable: true),
                    MaskData = table.Column<byte[]>(type: "MEDIUMBLOB", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sprite_frames", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sprite_frames_sprites_SpriteId",
                        column: x => x.SpriteId,
                        principalTable: "sprites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "spritemap_cells",
                columns: table => new
                {
                    SpriteMapId = table.Column<long>(type: "bigint", nullable: false),
                    Column = table.Column<int>(type: "int", nullable: false),
                    Row = table.Column<int>(type: "int", nullable: false),
                    SpriteId = table.Column<long>(type: "bigint", nullable: true),
                    FrameIndex = table.Column<int>(type: "int", nullable: false),
                    Flags = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_spritemap_cells", x => new { x.SpriteMapId, x.Column, x.Row });
                    table.ForeignKey(
                        name: "FK_spritemap_cells_spritemaps_SpriteMapId",
                        column: x => x.SpriteMapId,
                        principalTable: "spritemaps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_spritemap_cells_sprites_SpriteId",
                        column: x => x.SpriteId,
                        principalTable: "sprites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_palettes_ProjectId",
                table: "palettes",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_platform_modes_PlatformCode",
                table: "platform_modes",
                column: "PlatformCode");

            migrationBuilder.CreateIndex(
                name: "IX_projects_ModeCode",
                table: "projects",
                column: "ModeCode");

            migrationBuilder.CreateIndex(
                name: "IX_projects_OwnerId",
                table: "projects",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_projects_OwnerId_Name",
                table: "projects",
                columns: new[] { "OwnerId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_projects_PlatformCode",
                table: "projects",
                column: "PlatformCode");

            migrationBuilder.CreateIndex(
                name: "IX_sprite_frames_SpriteId_FrameIndex",
                table: "sprite_frames",
                columns: new[] { "SpriteId", "FrameIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sprite_groups_ProjectId",
                table: "sprite_groups",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_spritemap_cells_SpriteId",
                table: "spritemap_cells",
                column: "SpriteId");

            migrationBuilder.CreateIndex(
                name: "IX_spritemaps_ProjectId",
                table: "spritemaps",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_sprites_GroupId",
                table: "sprites",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_sprites_PaletteId",
                table: "sprites",
                column: "PaletteId");

            migrationBuilder.CreateIndex(
                name: "IX_sprites_ProjectId",
                table: "sprites",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_user_logins_UserId",
                table: "user_logins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_users_Email",
                table: "users",
                column: "Email");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "palette_entries");

            migrationBuilder.DropTable(
                name: "sprite_frames");

            migrationBuilder.DropTable(
                name: "spritemap_cells");

            migrationBuilder.DropTable(
                name: "user_logins");

            migrationBuilder.DropTable(
                name: "spritemaps");

            migrationBuilder.DropTable(
                name: "sprites");

            migrationBuilder.DropTable(
                name: "palettes");

            migrationBuilder.DropTable(
                name: "sprite_groups");

            migrationBuilder.DropTable(
                name: "projects");

            migrationBuilder.DropTable(
                name: "platform_modes");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "platforms");
        }
    }
}
