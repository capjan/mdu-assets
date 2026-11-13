using MduAssets;
using MduAssets.Api;
using MduAssets.Delivery;
using MduAssets.Security;
using MduAssets.Storage;
using Microsoft.AspNetCore.Authentication;

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

ServerSettings settings;
DataDirectory dataDirectory;
Database database;
string? createdAdmin;
try
{
    settings = ServerSettings.Create(builder.Configuration.Get<ServerOptions>() ?? new ServerOptions(), builder.Environment.IsDevelopment());

    // Prepare storage before the server accepts requests, so a broken data directory or schema stops the start.
    dataDirectory = new DataDirectory(settings.Options.DataDirectory);
    dataDirectory.Prepare();
    database = new Database(dataDirectory);
    MigrationRunner.Apply(database, Migration.Embedded());
    createdAdmin = FirstAdmin.EnsureExists(new UserStore(database), builder.Configuration["ADMIN_USER"], builder.Configuration["ADMIN_PASSWORD"]);
}
catch (StartupException exception)
{
    Console.Error.WriteLine($"mdu-assets cannot start: {exception.Message}");
    return 1;
}

builder.Services.AddSingleton(settings);
builder.Services.AddSingleton(dataDirectory);
builder.Services.AddSingleton(database);
builder.Services.AddSingleton<BlobStore>();
builder.Services.AddSingleton<UserStore>();
builder.Services.AddSingleton<AssetStore>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<CredentialCache>();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(json => json.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(settings.ApplyTo);
builder.Services
    .AddAuthentication(BasicAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>(BasicAuthenticationHandler.SchemeName, null);
builder.Services.AddAuthorization();

var app = builder.Build();

app.UseForwardedHeaders();
app.UseRequestLogging();
app.Use((context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    return next(context);
});
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/healthz");

// Public links: only on the public host, without authentication.
app.MapMethods("/i/{id}", ["GET", "HEAD"], PublicLinks.Serve).RequireHost(settings.PublicHost);
app.MapMethods("/i/{id}/{**name}", ["GET", "HEAD"], PublicLinks.Serve).RequireHost(settings.PublicHost);

// API: only on the UI host, signed in.
var api = app.MapGroup("/api/v1").RequireHost(settings.UiHost).RequireAuthorization();
api.MapPost("/projects/{project}/assets", UploadEndpoint.Handle);

if (createdAdmin is not null)
    app.Logger.LogInformation("Created the first administrator {Admin}", createdAdmin);
app.Logger.LogInformation("Data directory: {DataDirectory}", dataDirectory.Root);
app.Logger.LogInformation("Web interface and API: {UiBaseUrl}, public links: {PublicBaseUrl}", settings.UiBaseUrl, settings.PublicBaseUrl);
app.Run();
return 0;

public partial class Program;
