using FailureWorker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var connection = Environment.GetEnvironmentVariable("PERSISTENTWORKFLOWS_FAILURE_WORKER_CONNECTION")
    ?? throw new InvalidOperationException("Missing isolated test connection.");
var catalog = new SqlConnectionStringBuilder(connection).InitialCatalog;
if (!catalog.StartsWith("PersistentWorkflowsTests_", StringComparison.Ordinal)
    || !Guid.TryParseExact(catalog["PersistentWorkflowsTests_".Length..], "N", out _))
    throw new InvalidOperationException("Worker accepts only generated test catalogs.");
var block = args.Single() == "block";
var builder = Host.CreateApplicationBuilder();
builder.Logging.ClearProviders();
WorkerSetup.Add(builder.Services, connection, new StepControl
{
    BeforeSecond = async (context, ct) =>
    {
        Console.WriteLine($"SECOND {context.IdempotencyKey} {context.Attempt}");
        if (block) await Task.Delay(Timeout.Infinite, ct);
    }
});
using var host = builder.Build();
await host.RunAsync();
