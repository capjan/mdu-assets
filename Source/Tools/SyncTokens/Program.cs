// Copies tokens.css from the MDU design system into the web interface and records where it came from.
// Usage (in Source): dotnet run --project Tools/SyncTokens -- <path to mdu-design-system>
// The pin file lets TokensTest detect hand edits of the copy; see ADR-0010.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: dotnet run --project Tools/SyncTokens -- <path to mdu-design-system>");
    return 2;
}

var designSystem = Path.GetFullPath(args[0]);
var source = Path.Combine(designSystem, "tokens", "tokens.css");
if (!File.Exists(source))
{
    Console.Error.WriteLine($"No tokens/tokens.css in {designSystem}.");
    return 1;
}

var ui = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "MduAssets", "Ui"));
if (!Directory.Exists(ui))
{
    Console.Error.WriteLine($"Cannot find the web interface folder at {ui}.");
    return 1;
}

var bytes = File.ReadAllBytes(source);
File.WriteAllBytes(Path.Combine(ui, "tokens.css"), bytes);

var pin = new Dictionary<string, string>
{
    ["source"] = "mdu-design-system/tokens/tokens.css",
    ["commit"] = GitCommit(designSystem) ?? "unknown",
    ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(bytes)),
};
File.WriteAllText(Path.Combine(ui, "tokens.pin.json"),
    JsonSerializer.Serialize(pin, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);

Console.WriteLine($"Copied tokens.css ({bytes.Length:N0} bytes) from {pin["commit"]}.");
return 0;

static string? GitCommit(string directory)
{
    try
    {
        using var git = Process.Start(new ProcessStartInfo("git", ["-C", directory, "rev-parse", "--short", "HEAD"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });
        var output = git?.StandardOutput.ReadToEnd().Trim();
        git?.WaitForExit();
        return git?.ExitCode == 0 && !string.IsNullOrEmpty(output) ? output : null;
    }
    catch (System.ComponentModel.Win32Exception)
    {
        return null;
    }
}
