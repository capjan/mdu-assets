using System.Diagnostics;

namespace MduAssets;

/// <summary>
/// Writes one log line per request with method, path, status and duration.
/// The query string is left out, so tokens or other secrets in it never reach the log.
/// </summary>
internal static partial class RequestLogging
{
    public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder app)
    {
        var logger = app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger("MduAssets.Requests");
        return app.Use(async (context, next) =>
        {
            var start = Stopwatch.GetTimestamp();
            try
            {
                await next(context);
            }
            finally
            {
                var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                Request(logger, context.Request.Method, context.Request.Path.Value ?? "/", context.Response.StatusCode, elapsed);
            }
        });
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Method} {Path} {StatusCode} {ElapsedMs:0.0} ms")]
    private static partial void Request(ILogger logger, string method, string path, int statusCode, double elapsedMs);
}
