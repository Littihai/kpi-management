using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KPI.Infrastructure.Data.Migrations
{
    /// <summary>
    /// The KPI feature was removed from the product. Its tables are NOT dropped: they are renamed to Archive_* so every
    /// row survives, nothing can reach them any more, and Down puts them back exactly as they were. EF generated a
    /// DropTable here; it was replaced on purpose. Dropping the archive is a separate decision (take a backup first).
    /// </summary>
    public partial class ArchiveKpiTables : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(name: "KpiProgressLogs", newName: "Archive_KpiProgressLogs");
            migrationBuilder.RenameTable(name: "Projects", newName: "Archive_Projects");
            migrationBuilder.RenameTable(name: "Kpis", newName: "Archive_Kpis");

            migrationBuilder.Sql("COMMENT ON TABLE \"Archive_Kpis\" IS 'Archived data of the removed KPI feature. Read-only; safe to drop after a backup.';");
            migrationBuilder.Sql("COMMENT ON TABLE \"Archive_Projects\" IS 'Archived data of the removed KPI feature. Read-only; safe to drop after a backup.';");
            migrationBuilder.Sql("COMMENT ON TABLE \"Archive_KpiProgressLogs\" IS 'Archived data of the removed KPI feature. Read-only; safe to drop after a backup.';");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("COMMENT ON TABLE \"Archive_Kpis\" IS NULL;");
            migrationBuilder.Sql("COMMENT ON TABLE \"Archive_Projects\" IS NULL;");
            migrationBuilder.Sql("COMMENT ON TABLE \"Archive_KpiProgressLogs\" IS NULL;");

            migrationBuilder.RenameTable(name: "Archive_Kpis", newName: "Kpis");
            migrationBuilder.RenameTable(name: "Archive_Projects", newName: "Projects");
            migrationBuilder.RenameTable(name: "Archive_KpiProgressLogs", newName: "KpiProgressLogs");
        }
    }
}
