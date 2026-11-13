namespace MduAssets;

/// <summary>
/// Settings from <c>appsettings.json</c> and <c>MDU_ASSETS_*</c> environment variables,
/// for example <c>MDU_ASSETS_DATADIRECTORY=/data</c>.
/// </summary>
public sealed class ServerOptions
{
    /// <summary>Folder for the database and all files. Relative paths start at the working directory.</summary>
    public string DataDirectory { get; set; } = "data";
}
