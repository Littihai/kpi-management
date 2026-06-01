using KPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace KPI.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Kpi> Kpis => Set<Kpi>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<KpiProgressLog> KpiProgressLogs => Set<KpiProgressLog>();

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

        // KPI
        modelBuilder.Entity<Kpi>()
            .HasOne(k => k.Department)
            .WithMany()
            .HasForeignKey(k => k.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Kpi>()
            .HasOne(k => k.Owner)
            .WithMany()
            .HasForeignKey(k => k.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Kpi>()
            .HasOne(k => k.ApprovedBy)
            .WithMany()
            .HasForeignKey(k => k.ApprovedById)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Kpi>()
            .Property(k => k.Progress)
            .HasPrecision(5, 2);

        modelBuilder.Entity<Kpi>()
            .Property(k => k.Target)
            .HasPrecision(5, 2);

        // Project
        modelBuilder.Entity<Project>()
            .HasOne(p => p.Kpi)
            .WithMany(k => k.Projects)
            .HasForeignKey(p => p.KpiId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Project>()
            .Property(p => p.Weight)
            .HasPrecision(5, 2);

        modelBuilder.Entity<Project>()
            .Property(p => p.Progress)
            .HasPrecision(5, 2);
    }
}