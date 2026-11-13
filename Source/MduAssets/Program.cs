using MduAssets;
using MduAssets.Storage;

// Settings files sit next to the program, wherever it is started from.
var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});
builder.Configuration.AddEnvironmentVariables("MDU_ASSETS_");

// One JSON object per line for log collectors; readable single lines during development.
builder.Logging.ClearProviders();
if (builder.Environment.IsDevelopment())
    builder.Logging.AddSimpleConsole(console => console.SingleLine = true);
else
    builder.Logging.AddJsonConsole(console =>
    {
        console.UseUtcTimestamp = true;
        console.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
    });

var options = builder.Configuration.Get<ServerOptions>() ?? new ServerOptions();

// Prepare storage before the server accepts requests, so a broken data directory or schema stops the start.
var dataDirectory = new DataDirectory(options.DataDirectory);
dataDirectory.Prepare();
var database = new Database(dataDirectory);
MigrationRunner.Apply(database, Migration.Embedded());

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(dataDirectory);
builder.Services.AddSingleton(database);
builder.Services.AddSingleton<BlobStore>();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

var app = builder.Build();

app.UseRequestLogging();
app.MapHealthChecks("/healthz");

app.Logger.LogInformation("Data directory: {DataDirectory}", dataDirectory.Root);
app.Run();

public partial class Program;
