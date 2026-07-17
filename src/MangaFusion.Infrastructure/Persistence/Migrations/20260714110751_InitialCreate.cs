using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MangaFusion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Theme = table.Column<string>(type: "TEXT", nullable: true),
                    DefaultLanguage = table.Column<string>(type: "TEXT", nullable: true),
                    PreferredKind = table.Column<int>(type: "INTEGER", nullable: true),
                    HomeAcrossLibraries = table.Column<bool>(type: "INTEGER", nullable: false),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: true),
                    SecurityStamp = table.Column<string>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", nullable: true),
                    PhoneNumber = table.Column<string>(type: "TEXT", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LockoutEnd = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Authors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    SourceId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    SourceAuthorId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Authors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Downloads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SeriesId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    MediaKind = table.Column<int>(type: "INTEGER", nullable: false),
                    ReleaseId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ChapterId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    PagesDone = table.Column<int>(type: "INTEGER", nullable: false),
                    PagesTotal = table.Column<int>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", nullable: true),
                    HangfireJobId = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Downloads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImportBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", nullable: true),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MigrationBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", nullable: true),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    DivertedFolders = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Series",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    AltTitles = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    CoverPath = table.Column<string>(type: "TEXT", nullable: true),
                    ContentRating = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: true),
                    OriginalLanguage = table.Column<string>(type: "TEXT", nullable: true),
                    PreferredGroups = table.Column<string>(type: "TEXT", nullable: false),
                    GracePeriodDays = table.Column<int>(type: "INTEGER", nullable: true),
                    AutoDownload = table.Column<bool>(type: "INTEGER", nullable: false),
                    Languages = table.Column<string>(type: "TEXT", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastScannedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Series", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "SourceCredentials",
                columns: table => new
                {
                    SourceId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    EncryptedPayload = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceCredentials", x => x.SourceId);
                });

            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Group = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SourceId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    SourceTagId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClaimType = table.Column<string>(type: "TEXT", nullable: true),
                    ClaimValue = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClaimType = table.Column<string>(type: "TEXT", nullable: true),
                    ClaimValue = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderKey = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "TEXT", nullable: true),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RoleId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LoginProvider = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: true),
                    SeriesId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Severity = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ImportSeries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GroupTitle = table.Column<string>(type: "TEXT", nullable: false),
                    MatchedSourceSeriesId = table.Column<string>(type: "TEXT", nullable: true),
                    MatchedTitle = table.Column<string>(type: "TEXT", nullable: true),
                    TitleOverride = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ExistingLibrarySeriesId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CommittedLibrarySeriesId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CommitItemsDone = table.Column<int>(type: "INTEGER", nullable: true),
                    CommitItemsTotal = table.Column<int>(type: "INTEGER", nullable: true),
                    CommitPageDone = table.Column<int>(type: "INTEGER", nullable: true),
                    CommitPageTotal = table.Column<int>(type: "INTEGER", nullable: true),
                    CommitError = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportSeries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportSeries_ImportBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "ImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MigrationSeries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FolderName = table.Column<string>(type: "TEXT", nullable: false),
                    ComicInfoSeriesTitle = table.Column<string>(type: "TEXT", nullable: true),
                    MatchedSourceId = table.Column<string>(type: "TEXT", nullable: true),
                    MatchedSourceSeriesId = table.Column<string>(type: "TEXT", nullable: true),
                    MatchedTitle = table.Column<string>(type: "TEXT", nullable: true),
                    Regime = table.Column<int>(type: "INTEGER", nullable: false),
                    Confidence = table.Column<double>(type: "REAL", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ConflictReason = table.Column<string>(type: "TEXT", nullable: true),
                    ExistingLibrarySeriesId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CommittedLibrarySeriesId = table.Column<Guid>(type: "TEXT", nullable: true),
                    GroupRanking = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationSeries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MigrationSeries_MigrationBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "MigrationBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Artifacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SeriesId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Format = table.Column<int>(type: "INTEGER", nullable: false),
                    Origin = table.Column<int>(type: "INTEGER", nullable: false),
                    Path = table.Column<string>(type: "TEXT", nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    Hash = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    PageCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Artifacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Artifacts_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Follows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SeriesId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Languages = table.Column<string>(type: "TEXT", nullable: false),
                    AutoDownload = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Follows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Follows_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Follows_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeriesArtists",
                columns: table => new
                {
                    ArtistOfId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ArtistsId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeriesArtists", x => new { x.ArtistOfId, x.ArtistsId });
                    table.ForeignKey(
                        name: "FK_SeriesArtists_Authors_ArtistsId",
                        column: x => x.ArtistsId,
                        principalTable: "Authors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SeriesArtists_Series_ArtistOfId",
                        column: x => x.ArtistOfId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeriesAuthors",
                columns: table => new
                {
                    AuthorOfId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AuthorsId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeriesAuthors", x => new { x.AuthorOfId, x.AuthorsId });
                    table.ForeignKey(
                        name: "FK_SeriesAuthors_Authors_AuthorsId",
                        column: x => x.AuthorsId,
                        principalTable: "Authors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SeriesAuthors_Series_AuthorOfId",
                        column: x => x.AuthorOfId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeriesReadingEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SeriesId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Dismissed = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeriesReadingEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeriesReadingEntries_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SeriesReadingEntries_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeriesSourceLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SeriesId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SourceSeriesId = table.Column<string>(type: "TEXT", nullable: false),
                    IsMetadataPrimary = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeriesSourceLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeriesSourceLinks_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeriesTags",
                columns: table => new
                {
                    SeriesId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TagsId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeriesTags", x => new { x.SeriesId, x.TagsId });
                    table.ForeignKey(
                        name: "FK_SeriesTags_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SeriesTags_Tags_TagsId",
                        column: x => x.TagsId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ImportItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ImportSeriesId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FolderName = table.Column<string>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    Format = table.Column<int>(type: "INTEGER", nullable: false),
                    ParsedVolume = table.Column<string>(type: "TEXT", nullable: true),
                    PageCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    Include = table.Column<bool>(type: "INTEGER", nullable: false),
                    Number = table.Column<string>(type: "TEXT", nullable: true),
                    Volume = table.Column<string>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportItems_ImportSeries_ImportSeriesId",
                        column: x => x.ImportSeriesId,
                        principalTable: "ImportSeries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MigrationItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MigrationSeriesId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    UuidPrefix = table.Column<string>(type: "TEXT", nullable: true),
                    Number = table.Column<string>(type: "TEXT", nullable: true),
                    NumberKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ChapterTitle = table.Column<string>(type: "TEXT", nullable: true),
                    PageCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    MatchedSourceChapterId = table.Column<string>(type: "TEXT", nullable: true),
                    MatchedGroup = table.Column<string>(type: "TEXT", nullable: true),
                    Disposition = table.Column<int>(type: "INTEGER", nullable: false),
                    IsWinner = table.Column<bool>(type: "INTEGER", nullable: false),
                    FlagReason = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MigrationItems_MigrationSeries_MigrationSeriesId",
                        column: x => x.MigrationSeriesId,
                        principalTable: "MigrationSeries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ArtifactChapters",
                columns: table => new
                {
                    ArtifactId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChapterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    PageCount = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArtifactChapters", x => new { x.ArtifactId, x.ChapterId });
                    table.ForeignKey(
                        name: "FK_ArtifactChapters_Artifacts_ArtifactId",
                        column: x => x.ArtifactId,
                        principalTable: "Artifacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChapterReleases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChapterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SourceChapterId = table.Column<string>(type: "TEXT", nullable: false),
                    ScanlationGroups = table.Column<string>(type: "TEXT", nullable: false),
                    GroupKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PageCount = table.Column<int>(type: "INTEGER", nullable: true),
                    IsExternal = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExternalUrl = table.Column<string>(type: "TEXT", nullable: true),
                    DiscoveredAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChapterReleases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Chapters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SeriesId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Language = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Number = table.Column<string>(type: "TEXT", nullable: true),
                    NumberSort = table.Column<decimal>(type: "TEXT", nullable: true),
                    NumberKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Volume = table.Column<string>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: true),
                    ActiveArtifactId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ActiveReleaseId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Chapters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Chapters_Artifacts_ActiveArtifactId",
                        column: x => x.ActiveArtifactId,
                        principalTable: "Artifacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Chapters_ChapterReleases_ActiveReleaseId",
                        column: x => x.ActiveReleaseId,
                        principalTable: "ChapterReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Chapters_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReadingProgress",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChapterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PageIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    Completed = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingProgress", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReadingProgress_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReadingProgress_Chapters_ChapterId",
                        column: x => x.ChapterId,
                        principalTable: "Chapters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArtifactChapters_ChapterId",
                table: "ArtifactChapters",
                column: "ChapterId");

            migrationBuilder.CreateIndex(
                name: "IX_Artifacts_SeriesId",
                table: "Artifacts",
                column: "SeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Authors_SourceId_SourceAuthorId",
                table: "Authors",
                columns: new[] { "SourceId", "SourceAuthorId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChapterReleases_ChapterId",
                table: "ChapterReleases",
                column: "ChapterId");

            migrationBuilder.CreateIndex(
                name: "IX_ChapterReleases_SourceId_SourceChapterId",
                table: "ChapterReleases",
                columns: new[] { "SourceId", "SourceChapterId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Chapters_ActiveArtifactId",
                table: "Chapters",
                column: "ActiveArtifactId");

            migrationBuilder.CreateIndex(
                name: "IX_Chapters_ActiveReleaseId",
                table: "Chapters",
                column: "ActiveReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_Chapters_SeriesId_Language_NumberKey",
                table: "Chapters",
                columns: new[] { "SeriesId", "Language", "NumberKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Downloads_CreatedAt",
                table: "Downloads",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Downloads_Status",
                table: "Downloads",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Follows_SeriesId",
                table: "Follows",
                column: "SeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_Follows_UserId_SeriesId",
                table: "Follows",
                columns: new[] { "UserId", "SeriesId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportItems_ImportSeriesId",
                table: "ImportItems",
                column: "ImportSeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportSeries_BatchId",
                table: "ImportSeries",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_MigrationItems_MigrationSeriesId_NumberKey",
                table: "MigrationItems",
                columns: new[] { "MigrationSeriesId", "NumberKey" });

            migrationBuilder.CreateIndex(
                name: "IX_MigrationSeries_BatchId",
                table: "MigrationSeries",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_Kind_ReadAt",
                table: "Notifications",
                columns: new[] { "UserId", "Kind", "ReadAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingProgress_ChapterId",
                table: "ReadingProgress",
                column: "ChapterId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingProgress_UserId_ChapterId",
                table: "ReadingProgress",
                columns: new[] { "UserId", "ChapterId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Series_Kind",
                table: "Series",
                column: "Kind");

            migrationBuilder.CreateIndex(
                name: "IX_SeriesArtists_ArtistsId",
                table: "SeriesArtists",
                column: "ArtistsId");

            migrationBuilder.CreateIndex(
                name: "IX_SeriesAuthors_AuthorsId",
                table: "SeriesAuthors",
                column: "AuthorsId");

            migrationBuilder.CreateIndex(
                name: "IX_SeriesReadingEntries_SeriesId",
                table: "SeriesReadingEntries",
                column: "SeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_SeriesReadingEntries_UserId_SeriesId",
                table: "SeriesReadingEntries",
                columns: new[] { "UserId", "SeriesId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeriesSourceLinks_SeriesId",
                table: "SeriesSourceLinks",
                column: "SeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_SeriesSourceLinks_SourceId_SourceSeriesId",
                table: "SeriesSourceLinks",
                columns: new[] { "SourceId", "SourceSeriesId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeriesTags_TagsId",
                table: "SeriesTags",
                column: "TagsId");

            migrationBuilder.CreateIndex(
                name: "IX_Tags_Kind_SourceId_SourceTagId",
                table: "Tags",
                columns: new[] { "Kind", "SourceId", "SourceTagId" });

            migrationBuilder.AddForeignKey(
                name: "FK_ArtifactChapters_Chapters_ChapterId",
                table: "ArtifactChapters",
                column: "ChapterId",
                principalTable: "Chapters",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ChapterReleases_Chapters_ChapterId",
                table: "ChapterReleases",
                column: "ChapterId",
                principalTable: "Chapters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Chapters_Artifacts_ActiveArtifactId",
                table: "Chapters");

            migrationBuilder.DropForeignKey(
                name: "FK_ChapterReleases_Chapters_ChapterId",
                table: "ChapterReleases");

            migrationBuilder.DropTable(
                name: "ArtifactChapters");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "Downloads");

            migrationBuilder.DropTable(
                name: "Follows");

            migrationBuilder.DropTable(
                name: "ImportItems");

            migrationBuilder.DropTable(
                name: "MigrationItems");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "ReadingProgress");

            migrationBuilder.DropTable(
                name: "SeriesArtists");

            migrationBuilder.DropTable(
                name: "SeriesAuthors");

            migrationBuilder.DropTable(
                name: "SeriesReadingEntries");

            migrationBuilder.DropTable(
                name: "SeriesSourceLinks");

            migrationBuilder.DropTable(
                name: "SeriesTags");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "SourceCredentials");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "ImportSeries");

            migrationBuilder.DropTable(
                name: "MigrationSeries");

            migrationBuilder.DropTable(
                name: "Authors");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "Tags");

            migrationBuilder.DropTable(
                name: "ImportBatches");

            migrationBuilder.DropTable(
                name: "MigrationBatches");

            migrationBuilder.DropTable(
                name: "Artifacts");

            migrationBuilder.DropTable(
                name: "Chapters");

            migrationBuilder.DropTable(
                name: "ChapterReleases");

            migrationBuilder.DropTable(
                name: "Series");
        }
    }
}
