using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersistentWorkflows.EntityFrameworkCore.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "PersistentWorkflows");

            migrationBuilder.CreateTable(
                name: "WorkflowInstances",
                schema: "PersistentWorkflows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkflowName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    InstanceKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContextJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CurrentStepIndex = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NextExecutionAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowInstances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowStepExecutions",
                schema: "PersistentWorkflows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkflowInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StepName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Attempt = table.Column<int>(type: "int", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowStepExecutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowStepExecutions_WorkflowInstances_WorkflowInstanceId",
                        column: x => x.WorkflowInstanceId,
                        principalSchema: "PersistentWorkflows",
                        principalTable: "WorkflowInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowInstances_NextExecutionAtUtc",
                schema: "PersistentWorkflows",
                table: "WorkflowInstances",
                column: "NextExecutionAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowInstances_WorkflowName_InstanceKey",
                schema: "PersistentWorkflows",
                table: "WorkflowInstances",
                columns: new[] { "WorkflowName", "InstanceKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStepExecutions_WorkflowInstanceId_StepName_Attempt",
                schema: "PersistentWorkflows",
                table: "WorkflowStepExecutions",
                columns: new[] { "WorkflowInstanceId", "StepName", "Attempt" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkflowStepExecutions",
                schema: "PersistentWorkflows");

            migrationBuilder.DropTable(
                name: "WorkflowInstances",
                schema: "PersistentWorkflows");
        }
    }
}

