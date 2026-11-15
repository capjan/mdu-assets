using System.Security.Claims;
using MduAssets.Security;
using MduAssets.Storage;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MduAssets.Api;

public sealed record AuditEntryResponse(long Id, DateTimeOffset At, string? User, string? Project, string Action, string? Target, string? Details, string? Ip);

/// <summary>A page of audit entries, newest first; <c>Next</c> is the <c>before</c> value of the following page.</summary>
public sealed record AuditList(IReadOnlyList<AuditEntryResponse> Items, long? Next);

/// <summary>The audit log (OPS-03): admins see everything, owners the log of their project without client addresses.</summary>
public static class AuditEndpoints
{
    public const int PageSize = 100;

    public static void Map(RouteGroupBuilder api, RouteGroupBuilder admin)
    {
        admin.MapGet("/admin/audit", All);
        api.MapGet("/projects/{project}/audit", OfProject).RequirePassword();
    }

    private static AuditList All(long? before, AuditStore audit) => Page(audit.List(null, PageSize + 1, before), withIp: true);

    private static Results<Ok<AuditList>, ProblemHttpResult> OfProject(
        string project, long? before, ClaimsPrincipal user, ProjectAccess access, AuditStore audit)
    {
        if (!access.TryGet(user, project, ProjectRole.Owner, out var target, out var problem))
            return problem;
        return TypedResults.Ok(Page(audit.List(target.Id, PageSize + 1, before), withIp: user.IsAdmin()));
    }

    private static AuditList Page(IReadOnlyList<AuditEntry> entries, bool withIp)
    {
        var items = entries.Take(PageSize)
            .Select(e => new AuditEntryResponse(e.Id, e.At, e.UserName, e.ProjectSlug, e.Action, e.Target, e.Details, withIp ? e.Ip : null))
            .ToList();
        return new AuditList(items, entries.Count > PageSize ? items[^1].Id : null);
    }
}
