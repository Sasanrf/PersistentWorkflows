using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersistentWorkflows.EntityFrameworkCore.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class DurableExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CancellationRequested",
                schema: "PersistentWorkflows",
                table: "WorkflowInstances",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ConsecutiveFailures",
                schema: "PersistentWorkflows",
                table: "WorkflowInstances",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ContextSchemaVersion",
                schema: "PersistentWorkflows",
                table: "WorkflowInstances",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "CurrentStepName",
                schema: "PersistentWorkflows",
                table: "WorkflowInstances",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefinitionHash",
                schema: "PersistentWorkflows",
                table: "WorkflowInstances",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "DefinitionVersion",
                schema: "PersistentWorkflows",
                table: "WorkflowInstances",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "InputHash",
                schema: "PersistentWorkflows",
                table: "WorkflowInstances",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LastErrorCode",
                schema: "PersistentWorkflows",
                table: "WorkflowInstances",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastErrorMessage",
                schema: "PersistentWorkflows",
                table: "WorkflowInstances",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LeaseExpiresAtUtc",
                schema: "PersistentWorkflows",
                table: "WorkflowInstances",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LeaseToken",
                schema: "PersistentWorkflows",
                table: "WorkflowInstances",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WaitingSignal",
                schema: "PersistentWorkflows",
                table: "WorkflowInstances",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WorkflowSignals",
                schema: "PersistentWorkflows",
                columns: table => new
                {
                    WorkflowInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowSignals", x => new { x.WorkflowInstanceId, x.Name });
                    table.ForeignKey(
                        name: "FK_WorkflowSignals_WorkflowInstances_WorkflowInstanceId",
                        column: x => x.WorkflowInstanceId,
                        principalSchema: "PersistentWorkflows",
                        principalTable: "WorkflowInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowInstances_Status_LeaseExpiresAtUtc",
                schema: "PersistentWorkflows",
                table: "WorkflowInstances",
                columns: new[] { "Status", "LeaseExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowInstances_Status_NextExecutionAtUtc",
                schema: "PersistentWorkflows",
                table: "WorkflowInstances",
                columns: new[] { "Status", "NextExecutionAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkflowSignals",
                schema: "PersistentWorkflows");

            migrationBuilder.DropIndex(
                name: "IX_WorkflowInstances_Status_LeaseExpiresAtUtc",
                schema: "PersistentWorkflows",
                table: "WorkflowInstances");

            migrationBuilder.DropIndex(
                name: "IX_WorkflowInstances_Status_NextExecutionAtUtc",
                schema: "PersistentWorkflows",
                table: "WorkflowInstances");

            migrationBuilder.DropColumn(
                name: "CancellationRequested",
                schema: "PersistentWorkflows",
                table: "WorkflowInstances");

            migrationBuilder.DropColumn(
                name: "ConsecutiveFailures",
                schema: "PersistentWorkflows",
                table: "WorkflowInstances");

            migrationBuilder.DropColumn(
                name: "ContextSchemaVersion",
                schema: "PersistentWorkflows",
                table: "WorkflowInstances");

            migrationBuilder.DropColumn(
                name: "CurrentStepName",
                schema: "PersistentWorkflows",
                table: "WorkflowInstances");

            migrationBuilder.DropColumn(
                name: "DefinitionHash",
                schema: "PersistentWorkflows",
                table: "WorkflowInstances");

            migrationBuilder.DropColumn(
                name: "DefinitionVersion",
                schema: "PersistentWorkflows",
                table: "WorkflowInstances");

            migrationBuilder.DropColumn(
                name: "InputHash",
                schema: "PersistentWorkflows",
                table: "WorkflowInstances");

            migrationBuilder.DropColumn(
                name: "LastErrorCode",
                schema: "PersistentWorkflows",
                table: "WorkflowInstances");

            migrationBuilder.DropColumn(
                name: "LastErrorMessage",
                schema: "PersistentWorkflows",
                table: "WorkflowInstances");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAtUtc",
                schema: "PersistentWorkflows",
                table: "WorkflowInstances");

            migrationBuilder.DropColumn(
                name: "LeaseToken",
                schema: "PersistentWorkflows",
                table: "WorkflowInstances");

            migrationBuilder.DropColumn(
                name: "WaitingSignal",
                schema: "PersistentWorkflows",
                table: "WorkflowInstances");
        }
    }
}

