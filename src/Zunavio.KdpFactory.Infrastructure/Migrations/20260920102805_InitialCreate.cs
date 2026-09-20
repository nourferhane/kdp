using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zunavio.KdpFactory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_definitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PromptDriveFileId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    PromptDriveUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    PromptVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PromptHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PromptTextCache = table.Column<string>(type: "text", nullable: true),
                    PromptLastSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_definitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "background_jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    AvailableAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LockedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_background_jobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    WorkingTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FinalTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Marketplace = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Language = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TargetAge = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BookType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Season = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CurrentGate = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    MarketScore = table.Column<int>(type: "integer", nullable: true),
                    CurrentManuscriptVersion = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CurrentVisualBibleVersion = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CurrentProductionVersion = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    QaResult = table.Column<string>(type: "text", nullable: true),
                    NextAction = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SelectedConceptJson = table.Column<string>(type: "text", nullable: true),
                    DriveFolderId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    DriveFolderUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ExternalId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    row_version = table.Column<long>(type: "bigint", rowVersion: true, nullable: false, defaultValueSql: "0")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_projects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "agent_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    InputVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OutputVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PromptSnapshot = table.Column<string>(type: "text", nullable: false),
                    PromptVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    PromptHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    InputSnapshotJson = table.Column<string>(type: "text", nullable: true),
                    OutputJson = table.Column<string>(type: "text", nullable: true),
                    Summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    BlockingIssuesJson = table.Column<string>(type: "text", nullable: true),
                    GateRecommendation = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    OpenAiRequestId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    InputTokens = table.Column<int>(type: "integer", nullable: true),
                    OutputTokens = table.Column<int>(type: "integer", nullable: true),
                    EstimatedCost = table.Column<decimal>(type: "numeric(18,6)", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agent_runs_agent_definitions_AgentDefinitionId",
                        column: x => x.AgentDefinitionId,
                        principalTable: "agent_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agent_runs_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workflow_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    HumanReviewRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    OldGate = table.Column<int>(type: "integer", nullable: true),
                    NewGate = table.Column<int>(type: "integer", nullable: true),
                    Title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    PayloadJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_workflow_events_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetType = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DriveFileId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    DriveUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedByAgentRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    QaStatus = table.Column<int>(type: "integer", nullable: false),
                    ContentJson = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_assets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_assets_agent_runs_CreatedByAgentRunId",
                        column: x => x.CreatedByAgentRunId,
                        principalTable: "agent_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_assets_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "human_review_requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewType = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    PayloadJson = table.Column<string>(type: "text", nullable: true),
                    ResolutionPayloadJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolutionComment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_human_review_requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_human_review_requests_agent_runs_AgentRunId",
                        column: x => x.AgentRunId,
                        principalTable: "agent_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_human_review_requests_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agent_definitions_Code",
                table: "agent_definitions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agent_runs_AgentDefinitionId",
                table: "agent_runs",
                column: "AgentDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_agent_runs_IdempotencyKey",
                table: "agent_runs",
                column: "IdempotencyKey");

            migrationBuilder.CreateIndex(
                name: "IX_agent_runs_ProjectId",
                table: "agent_runs",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_agent_runs_RunCode",
                table: "agent_runs",
                column: "RunCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_assets_AssetCode",
                table: "assets",
                column: "AssetCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_assets_CreatedByAgentRunId",
                table: "assets",
                column: "CreatedByAgentRunId");

            migrationBuilder.CreateIndex(
                name: "IX_assets_ProjectId_AssetType",
                table: "assets",
                columns: new[] { "ProjectId", "AssetType" });

            migrationBuilder.CreateIndex(
                name: "IX_background_jobs_IdempotencyKey",
                table: "background_jobs",
                column: "IdempotencyKey");

            migrationBuilder.CreateIndex(
                name: "IX_background_jobs_Status_AvailableAt",
                table: "background_jobs",
                columns: new[] { "Status", "AvailableAt" });

            migrationBuilder.CreateIndex(
                name: "IX_human_review_requests_AgentRunId",
                table: "human_review_requests",
                column: "AgentRunId");

            migrationBuilder.CreateIndex(
                name: "IX_human_review_requests_ProjectId_Status",
                table: "human_review_requests",
                columns: new[] { "ProjectId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_projects_ExternalId",
                table: "projects",
                column: "ExternalId");

            migrationBuilder.CreateIndex(
                name: "IX_projects_ProjectCode",
                table: "projects",
                column: "ProjectCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_events_ProjectId_CreatedAt",
                table: "workflow_events",
                columns: new[] { "ProjectId", "CreatedAt" });

            EnsureRowVersionTrigger(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_projects_bump_row_version ON projects;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS kdp_bump_row_version();");
            migrationBuilder.DropTable(
                name: "assets");

            migrationBuilder.DropTable(
                name: "background_jobs");

            migrationBuilder.DropTable(
                name: "human_review_requests");

            migrationBuilder.DropTable(
                name: "workflow_events");

            migrationBuilder.DropTable(
                name: "agent_runs");

            migrationBuilder.DropTable(
                name: "agent_definitions");

            migrationBuilder.DropTable(
                name: "projects");
        }

        /// <summary>
        /// Maintains the optimistic-concurrency row_version counter on projects.
        /// Every UPDATE bumps the value so concurrent writers race as expected.
        /// </summary>
        private static void EnsureRowVersionTrigger(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION kdp_bump_row_version()
                RETURNS trigger AS $$
                BEGIN
                    NEW.row_version = COALESCE(OLD.row_version, 0) + 1;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER trg_projects_bump_row_version
                BEFORE UPDATE ON projects
                FOR EACH ROW
                EXECUTE FUNCTION kdp_bump_row_version();
                """);
        }
    }
}
