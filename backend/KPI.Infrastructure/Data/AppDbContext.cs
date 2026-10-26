using KPI.Application.Audit;
using KPI.Application.Common;
using KPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace KPI.Infrastructure.Data;

public class AppDbContext : DbContext
{
    private readonly IAuditContext _audit;

    // The audit context is optional so tests and tools can build a context from options alone.
    public AppDbContext(DbContextOptions<AppDbContext> options, IAuditContext? audit = null)
        : base(options)
    {
        _audit = audit ?? NullAuditContext.Instance;
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<RequestLog> RequestLogs => Set<RequestLog>();
    public DbSet<PasswordHistory> PasswordHistory => Set<PasswordHistory>();
    public DbSet<AuthLog> AuthLogs => Set<AuthLog>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<AppSettingHistory> AppSettingHistory => Set<AppSettingHistory>();
    public DbSet<Issue> Issues => Set<Issue>();
    public DbSet<IssueComment> IssueComments => Set<IssueComment>();
    public DbSet<IssueAttachment> IssueAttachments => Set<IssueAttachment>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampAuditRows();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampAuditRows();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>Adds who/where (from the current request) to audit rows that do not carry it yet.</summary>
    private void StampAuditRows()
    {
        foreach (var entry in ChangeTracker.Entries<AuditLog>().Where(e => e.State == EntityState.Added))
        {
            var row = entry.Entity;
            row.IpAddress ??= _audit.IpAddress.Truncate(64);
            row.UserAgent ??= _audit.UserAgent.Truncate(400);
            row.RequestId ??= _audit.RequestId.Truncate(64);
            row.UserName ??= _audit.UserName.Truncate(200);
            row.UserRole ??= _audit.UserRole.Truncate(50);
            row.DepartmentName ??= _audit.DepartmentName.Truncate(200);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<RolePermission>()
            .HasKey(rp => new { rp.RoleId, rp.PermissionId });

        modelBuilder.Entity<User>()
            .HasOne(u => u.Role)
            .WithMany(r => r.Users)
            .HasForeignKey(u => u.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<User>()
            .HasOne(u => u.Department)
            .WithMany(d => d.Users)
            .HasForeignKey(u => u.DepartmentId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Department>()
            .HasOne(d => d.Manager)
            .WithMany()
            .HasForeignKey(d => d.ManagerId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<User>().Ignore(u => u.FullName);

        // ── Indexes for the queries the app actually runs (filters, joins and sort orders) ──
        modelBuilder.Entity<RolePermission>().HasIndex(rp => rp.PermissionId);

        // ── Audit and request logs ──
        var audit = modelBuilder.Entity<AuditLog>();
        audit.HasIndex(a => a.CreatedAt);
        audit.HasIndex(a => new { a.EntityName, a.EntityId });
        audit.HasIndex(a => new { a.UserId, a.CreatedAt });
        audit.HasIndex(a => a.IpAddress);
        audit.Property(a => a.IpAddress).HasMaxLength(64);
        audit.Property(a => a.UserAgent).HasMaxLength(400);
        audit.Property(a => a.RequestId).HasMaxLength(64);
        audit.Property(a => a.UserName).HasMaxLength(200);
        audit.Property(a => a.UserRole).HasMaxLength(50);
        audit.Property(a => a.DepartmentName).HasMaxLength(200);

        var request = modelBuilder.Entity<RequestLog>();
        // covers the overview's aggregates over a time window without touching the wide rows
        Cover(request.HasIndex(r => r.CreatedAt), r => new { r.Method, r.StatusCode, r.IpAddress, r.Path });
        request.HasIndex(r => new { r.UserId, r.CreatedAt });
        request.HasIndex(r => new { r.IpAddress, r.CreatedAt });
        request.HasIndex(r => new { r.StatusCode, r.CreatedAt });
        request.Property(r => r.Method).HasMaxLength(10);
        request.Property(r => r.Path).HasMaxLength(300);
        request.Property(r => r.IpAddress).HasMaxLength(64);
        request.Property(r => r.UserAgent).HasMaxLength(400);
        request.Property(r => r.RequestId).HasMaxLength(64);
        request.Property(r => r.UserEmail).HasMaxLength(254);
        request.Property(r => r.UserRole).HasMaxLength(50);

        // ── Security ──
        var history = modelBuilder.Entity<PasswordHistory>();
        history.HasOne(h => h.User).WithMany().HasForeignKey(h => h.UserId).OnDelete(DeleteBehavior.Cascade);
        history.HasIndex(h => new { h.UserId, h.CreatedAt });
        history.Property(h => h.PasswordHash).HasMaxLength(100);
        var authLog = modelBuilder.Entity<AuthLog>();
        authLog.HasIndex(a => a.At);
        authLog.HasIndex(a => new { a.Event, a.At });
        authLog.HasIndex(a => new { a.UserId, a.At });
        authLog.Property(a => a.Event).HasMaxLength(40);
        authLog.Property(a => a.Email).HasMaxLength(254);
        authLog.Property(a => a.IpAddress).HasMaxLength(64);
        authLog.Property(a => a.UserAgent).HasMaxLength(400);
        authLog.Property(a => a.Reason).HasMaxLength(100);
        authLog.Property(a => a.Detail).HasMaxLength(300);

        // ── Settings ──
        var setting = modelBuilder.Entity<AppSetting>();
        setting.HasKey(x => x.Key);
        setting.Property(x => x.Key).HasMaxLength(100);
        setting.Property(x => x.Value).HasMaxLength(500);
        setting.Property(x => x.UpdatedByName).HasMaxLength(200);
        var settingHistory = modelBuilder.Entity<AppSettingHistory>();
        settingHistory.HasIndex(x => new { x.Key, x.ChangedAt });
        settingHistory.Property(x => x.Key).HasMaxLength(100);
        settingHistory.Property(x => x.OldValue).HasMaxLength(500);
        settingHistory.Property(x => x.NewValue).HasMaxLength(500);
        settingHistory.Property(x => x.ChangedByName).HasMaxLength(200);

        // ── Issues ──
        var issue = modelBuilder.Entity<Issue>();
        issue.Property(i => i.Title).HasMaxLength(200);
        issue.Property(i => i.Description).HasMaxLength(4000);
        issue.Property(i => i.ReporterName).HasMaxLength(200);
        issue.Property(i => i.ReporterEmail).HasMaxLength(254);
        issue.Property(i => i.ReporterPhone).HasMaxLength(40);
        issue.Property(i => i.Resolution).HasMaxLength(4000);
        issue.Property(i => i.Prevention).HasMaxLength(2000);
        issue.Property(i => i.FixReference).HasMaxLength(500);
        issue.Property(i => i.RejectReason).HasMaxLength(1000);
        issue.Property(i => i.PageUrl).HasMaxLength(500);
        issue.Property(i => i.UserAgent).HasMaxLength(400);
        issue.Property(i => i.ClientInfo).HasMaxLength(1000);
        issue.Property(i => i.ErrorRequestId).HasMaxLength(100);
        issue.Property(i => i.ReporterIp).HasMaxLength(64);
        issue.Property(i => i.Version).IsConcurrencyToken();
        issue.Ignore(i => i.DisplayNumber);

        issue.HasOne(i => i.ReporterUser).WithMany().HasForeignKey(i => i.ReporterUserId).OnDelete(DeleteBehavior.SetNull);
        issue.HasOne(i => i.Assignee).WithMany().HasForeignKey(i => i.AssigneeId).OnDelete(DeleteBehavior.SetNull);
        issue.HasOne(i => i.Department).WithMany().HasForeignKey(i => i.DepartmentId).OnDelete(DeleteBehavior.SetNull);
        issue.HasMany(i => i.Comments).WithOne(c => c.Issue).HasForeignKey(c => c.IssueId).OnDelete(DeleteBehavior.Cascade);
        issue.HasMany(i => i.Attachments).WithOne(a => a.Issue).HasForeignKey(a => a.IssueId).OnDelete(DeleteBehavior.Cascade);

        // SQL Server refuses a SET NULL / CASCADE that can reach one table by two routes (Departments -> Users -> Issues and
        // Departments -> Issues, or two foreign keys from Issues to Users), or that loops (Users <-> Departments). Nothing deletes users or departments (they are
        // deactivated), so a plain "no action" behaves the same in practice and a stray manual delete is refused instead.
        if (Database.IsSqlServer())
        {
            // an index key on SQL Server may not exceed 1700 bytes; two unbounded strings in one index (900 characters each) can
            audit.Property(x => x.EntityName).HasMaxLength(200);
            audit.Property(x => x.EntityId).HasMaxLength(200);
            issue.HasOne(i => i.Department).WithMany().HasForeignKey(i => i.DepartmentId).OnDelete(DeleteBehavior.NoAction);
            issue.HasOne(i => i.Assignee).WithMany().HasForeignKey(i => i.AssigneeId).OnDelete(DeleteBehavior.NoAction);   // two SET NULL routes from Users to Issues
            modelBuilder.Entity<Department>().HasOne(d => d.Manager).WithMany().HasForeignKey(d => d.ManagerId).OnDelete(DeleteBehavior.NoAction);
        }

        issue.HasIndex(i => new { i.NumberYear, i.Number }).IsUnique();
        // covers the summary counts (open, overdue, mine) so they never read the wide rows
        Cover(issue.HasIndex(i => i.Status), i => new { i.ResponseDueAt, i.ResolveDueAt, i.FirstResponseAt, i.ReporterUserId, i.AssigneeId });
        issue.HasIndex(i => i.ResolvedAt);
        // search by title / reporter name scans this narrow index instead of the table
        Cover(issue.HasIndex(i => i.Title), i => new { i.ReporterName, i.CreatedAt });
        issue.HasIndex(i => i.CreatedAt);
        issue.HasIndex(i => new { i.Status, i.ResolveDueAt });
        issue.HasIndex(i => new { i.DepartmentId, i.Status });
        issue.HasIndex(i => new { i.AssigneeId, i.Status });
        issue.HasIndex(i => new { i.ReporterUserId, i.CreatedAt });

        var comment = modelBuilder.Entity<IssueComment>();
        comment.Property(c => c.AuthorName).HasMaxLength(200);
        comment.Property(c => c.Body).HasMaxLength(4000);
        comment.HasIndex(c => new { c.IssueId, c.CreatedAt });

        var attachment = modelBuilder.Entity<IssueAttachment>();
        attachment.Property(a => a.FileName).HasMaxLength(255);
        attachment.Property(a => a.ContentType).HasMaxLength(100);
    }

    /// <summary>An index that also stores the given columns, so a query that needs only those never reads the table row (both providers).</summary>
    private static void Cover<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.IndexBuilder<T> index, System.Linq.Expressions.Expression<Func<T, object?>> include)
    {
        SqlServerIndexBuilderExtensions.IncludeProperties(index, include);
        NpgsqlIndexBuilderExtensions.IncludeProperties(index, include);
    }
}
