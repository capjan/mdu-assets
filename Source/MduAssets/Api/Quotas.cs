using MduAssets.Maintenance;
using MduAssets.Storage;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MduAssets.Api;

/// <summary>Project quotas (OPS-02): checked when an upload or a new version adds bytes.</summary>
public static class Quotas
{
    public const string ProblemType = "urn:mdu-assets:problem:quota-exceeded";

    /// <summary>
    /// Refuses <paramref name="addedBytes"/> that would take the project over its quota. The received blob is removed again
    /// when nothing else uses it.
    /// </summary>
    public static ProblemHttpResult? Check(Project project, ProjectStore projects, long addedBytes, string blobSha256, BlobCleanup cleanup)
    {
        if (project.QuotaBytes is not { } quota)
            return null;
        var used = projects.UsedBytes(project.Id);
        if (used + addedBytes <= quota)
            return null;
        cleanup.Collect([blobSha256]);
        return TypedResults.Problem(
            $"The project uses {used:N0} of {quota:N0} bytes; this image needs {addedBytes:N0} more. Delete old versions or images in the trash, or ask an admin for more space.",
            statusCode: StatusCodes.Status422UnprocessableEntity, title: "Quota exceeded", type: ProblemType);
    }
}
