using KPI.Domain.Entities;
using KPI.Domain.Issues;
using KPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KPI.Tests;

public class IssueFiltersTests
{
    private static readonly DateTime Now = new(2026, 10, 17, 8, 0, 0, DateTimeKind.Utc);

    /// <summary>Every status, with the deadlines on either side of "now" and with or without a first response.</summary>
    private static List<Issue> Combinations()
    {
        var issues = new List<Issue>();
        foreach (var status in Enum.GetValues<IssueStatus>())
        foreach (DateTime? firstResponse in new DateTime?[] { null, Now.AddMinutes(-30) })
        foreach (var responseDue in new[] { Now.AddHours(-1), Now.AddHours(1) })
        foreach (var resolveDue in new[] { Now.AddHours(-1), Now.AddHours(1) })
            issues.Add(new Issue
            {
                Status = status, FirstResponseAt = firstResponse, ResponseDueAt = responseDue, ResolveDueAt = resolveDue,
            });
        return issues;
    }

    private static string Describe(Issue i) =>
        $"{i.Status}, first response {(i.FirstResponseAt is null ? "none" : "given")}, " +
        $"response due {(i.ResponseDueAt < Now ? "passed" : "ahead")}, resolve due {(i.ResolveDueAt < Now ? "passed" : "ahead")}";

    [Fact]
    public void The_database_filters_agree_with_the_in_memory_rules_for_every_combination()
    {
        var open = IssueFilters.Open.Compile();
        var finished = IssueFilters.Finished.Compile();
        var overdue = IssueFilters.Overdue(Now).Compile();

        foreach (var issue in Combinations())
        {
            Assert.True(IssueStateMachine.IsOpen(issue.Status) == open(issue), $"open: {Describe(issue)}");
            Assert.True(!IssueStateMachine.IsOpen(issue.Status) == finished(issue), $"finished: {Describe(issue)}");

            var expected = IssueStateMachine.IsResponseOverdue(issue.Status, issue.FirstResponseAt, issue.ResponseDueAt, Now)
                           || IssueStateMachine.IsResolveOverdue(issue.Status, issue.ResolveDueAt, Now);
            Assert.True(expected == overdue(issue), $"overdue: {Describe(issue)}");
        }
    }

    [Fact]
    public void Waiting_for_the_reporter_stops_the_resolve_clock()
    {
        var waiting = new Issue
        {
            Status = IssueStatus.WaitingForReporter, FirstResponseAt = Now.AddHours(-5),
            ResponseDueAt = Now.AddHours(-6), ResolveDueAt = Now.AddHours(-1),
        };

        Assert.False(IssueStateMachine.IsResolveOverdue(waiting.Status, waiting.ResolveDueAt, Now));
        Assert.False(IssueFilters.Overdue(Now).Compile()(waiting));
        Assert.True(IssueFilters.Open.Compile()(waiting));
    }

    // The in-memory test database accepts any expression, so also check that PostgreSQL can translate them.
    // ToQueryString builds the SQL without opening a connection.
    [Fact]
    public void The_filters_translate_to_SQL_on_PostgreSQL()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=never-opened;Username=x;Password=x").Options;
        using var db = new AppDbContext(options);

        var overdue = db.Issues.Where(IssueFilters.Overdue(Now)).ToQueryString();
        var open = db.Issues.Where(IssueFilters.Open).ToQueryString();
        var finished = db.Issues.Where(IssueFilters.Finished).ToQueryString();

        Assert.Contains("\"ResolveDueAt\"", overdue);
        Assert.Contains("\"ResponseDueAt\"", overdue);
        Assert.Contains("\"Status\"", open);
        Assert.Contains("\"Status\"", finished);
    }
}
