using Microsoft.AspNetCore.Authorization;

namespace KPI.API.Authorization;

public record PermissionRequirement(string Permission) : IAuthorizationRequirement;