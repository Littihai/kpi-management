using KPI.Application.Admin;
using KPI.Application.Issues;
using KPI.Domain.Constants;
using KPI.Domain.Interfaces;

namespace KPI.Infrastructure.Services;

/// <summary>
/// The Ctrl+K box. Issues go through the issue list's own scope and search rule. People are offered only to whoever may open
/// the user list (<c>user.view</c>), and through the same service that list uses, so a Manager sees their own department only.
/// </summary>
public class QuickSearchService : IQuickSearchService
{
    private const int UserHits = 5;

    private readonly IIssueService _issues;
    private readonly IOrgAdminService _org;
    private readonly IUserRepository _users;

    public QuickSearchService(IIssueService issues, IOrgAdminService org, IUserRepository users)
    {
        _issues = issues;
        _org = org;
        _users = users;
    }

    public async Task<QuickSearchResult> SearchAsync(string? term, Guid userId)
    {
        // Throws for someone without issue.view or a deactivated account, like every other issue call.
        var issues = await _issues.QuickSearchAsync(term, userId);
        if ((term ?? string.Empty).Trim().Length < IQuickSearchService.MinLength) return new QuickSearchResult(issues, []);

        var caller = await _users.GetByIdWithPermissionsAsync(userId);
        if (caller is null || !caller.Role.RolePermissions.Any(rp => rp.Permission.Name == Permissions.User.View))
            return new QuickSearchResult(issues, []);

        var found = await _org.SearchUsersAsync(userId, new UserQuery(Search: term!.Trim(), Active: true, PageSize: UserHits));
        var people = found.Items.Select(u => new UserHitDto(u.Id, $"{u.FirstName} {u.LastName}".Trim(), u.Email, u.Department)).ToList();
        return new QuickSearchResult(issues, people);
    }
}
