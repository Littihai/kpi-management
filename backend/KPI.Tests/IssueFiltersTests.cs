using KPI.Domain.Entities;
using KPI.Domain.Issues;
using System.Text.RegularExpressions;
using KPI.Infrastructure.Data;
using KPI.Infrastructure.Services;
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

    private static string Who(Guid? id, Guid me) => id is null ? "nobody" : id == me ? "me" : "someone else";

    [Fact]
    public void The_attention_flags_agree_with_the_state_machine_for_every_combination()
    {
        var me = Guid.NewGuid();
        var someoneElse = Guid.NewGuid();

        foreach (var queueWorker in new[] { true, false })
        {
            var attention = IssueFilters.Attention(me, queueWorker, Now).Compile();
            var overdue = IssueFilters.Overdue(Now).Compile();

            foreach (var issue in Combinations())
            foreach (Guid? assignee in new Guid?[] { null, me, someoneElse })
            foreach (Guid? reporter in new Guid?[] { null, me, someoneElse })
            {
                issue.AssigneeId = assignee;
                issue.ReporterUserId = reporter;
                var flags = attention(issue);
                var what = $"{Describe(issue)}, assignee {Who(assignee, me)}, reporter {Who(reporter, me)}, queue worker {queueWorker}";

                var late = IssueStateMachine.IsResponseOverdue(issue.Status, issue.FirstResponseAt, issue.ResponseDueAt, Now)
                           || IssueStateMachine.IsResolveOverdue(issue.Status, issue.ResolveDueAt, Now);
                Assert.True(flags.Overdue == (queueWorker && late), $"overdue (state machine): {what}");
                Assert.True(flags.Overdue == (queueWorker && overdue(issue)), $"overdue (filter): {what}");

                Assert.True(flags.NewInQueue == (queueWorker && issue.Status == IssueStatus.New && assignee is null), $"new in queue: {what}");
                // The assignee has the next move exactly when they could resolve the issue.
                Assert.True(flags.AssignedToMe == (assignee == me && IssueStateMachine.CanApply(issue.Status, IssueAction.Resolve)), $"assigned to me: {what}");
                Assert.True(flags.WaitingForMe == (reporter == me && issue.Status == IssueStatus.WaitingForReporter), $"waiting for me: {what}");
                Assert.True(flags.ToConfirm == (reporter == me && IssueStateMachine.CanApply(issue.Status, IssueAction.Confirm)), $"to confirm: {what}");

                // The attention query leaves these out before working out the flags, so they must never have any.
                if (issue.Status is IssueStatus.Closed or IssueStatus.Rejected)
                    Assert.False(flags.NewInQueue || flags.AssignedToMe || flags.Overdue || flags.WaitingForMe || flags.ToConfirm, $"finished for good: {what}");
            }
        }
    }

    [Fact]
    public void Every_attention_rule_as_a_filter_agrees_with_its_flag_for_every_combination()
    {
        var me = Guid.NewGuid();
        var someoneElse = Guid.NewGuid();

        foreach (var queueWorker in new[] { true, false })
        {
            var flagsOf = IssueFilters.Attention(me, queueWorker, Now).Compile();
            var rules = Enum.GetValues<IssueAttentionRule>().ToDictionary(r => r, r => IssueFilters.AttentionMatches(r, me, queueWorker, Now).Compile());

            foreach (var issue in Combinations())
            foreach (Guid? assignee in new Guid?[] { null, me, someoneElse })
            foreach (Guid? reporter in new Guid?[] { null, me, someoneElse })
            {
                issue.AssigneeId = assignee;
                issue.ReporterUserId = reporter;
                var flags = flagsOf(issue);
                var what = $"{Describe(issue)}, assignee {Who(assignee, me)}, reporter {Who(reporter, me)}, queue worker {queueWorker}";

                Assert.True(rules[IssueAttentionRule.NewInQueue](issue) == flags.NewInQueue, $"new in queue: {what}");
                Assert.True(rules[IssueAttentionRule.AssignedToMe](issue) == flags.AssignedToMe, $"assigned to me: {what}");
                Assert.True(rules[IssueAttentionRule.Overdue](issue) == flags.Overdue, $"overdue: {what}");
                Assert.True(rules[IssueAttentionRule.WaitingForMe](issue) == flags.WaitingForMe, $"waiting for me: {what}");
                Assert.True(rules[IssueAttentionRule.ToConfirm](issue) == flags.ToConfirm, $"to confirm: {what}");
                Assert.True(rules[IssueAttentionRule.Any](issue) ==
                            (flags.NewInQueue || flags.AssignedToMe || flags.Overdue || flags.WaitingForMe || flags.ToConfirm), $"any: {what}");
            }
        }
    }

    [Fact]
    public void Near_or_overdue_contains_overdue_and_agrees_with_the_state_machine_for_every_combination()
    {
        var overdue = IssueFilters.Overdue(Now).Compile();
        var near = IssueFilters.NearOrOverdue(Now).Compile();

        foreach (var issue in Combinations())
        {
            if (overdue(issue)) Assert.True(near(issue), $"overdue but not near: {Describe(issue)}");
            Assert.True(
                IssueStateMachine.IsNearOrOverdue(issue.Status, issue.FirstResponseAt, issue.ResponseDueAt, issue.ResolveDueAt, Now) == near(issue),
                $"near: {Describe(issue)}");
        }
    }

    [Fact]
    public void Near_or_overdue_is_false_when_the_clock_is_paused_or_the_issue_is_finished()
    {
        var near = IssueFilters.NearOrOverdue(Now).Compile();
        var closeToBoth = new[] { IssueStatus.WaitingForReporter, IssueStatus.OnHold, IssueStatus.Resolved, IssueStatus.Closed, IssueStatus.Rejected };

        foreach (var status in closeToBoth)
        {
            var issue = new Issue { Status = status, FirstResponseAt = Now.AddHours(-1), ResponseDueAt = Now.AddHours(-2), ResolveDueAt = Now.AddMinutes(1) };
            Assert.False(near(issue), status.ToString());
            Assert.False(IssueStateMachine.IsNearOrOverdue(status, issue.FirstResponseAt, issue.ResponseDueAt, issue.ResolveDueAt, Now), status.ToString());
        }
    }

    [Fact]
    public void An_issue_enters_the_warning_window_exactly_at_its_edge()
    {
        var near = IssueFilters.NearOrOverdue(Now).Compile();

        Issue Running(DateTime resolveDue) => new()
        {
            Status = IssueStatus.InProgress, FirstResponseAt = Now.AddHours(-1), ResponseDueAt = Now.AddHours(-2), ResolveDueAt = resolveDue,
        };
        var edge = Now + IssueSlaPolicy.NearResolve;
        Assert.False(near(Running(edge)));                       // still a whole window away
        Assert.True(near(Running(edge.AddTicks(-1))));           // one tick inside
        Assert.False(near(Running(edge.AddMinutes(1))));

        Issue Unanswered(DateTime responseDue) => new()
        {
            Status = IssueStatus.New, FirstResponseAt = null, ResponseDueAt = responseDue, ResolveDueAt = Now.AddDays(5),
        };
        var responseEdge = Now + IssueSlaPolicy.NearResponse;
        Assert.False(near(Unanswered(responseEdge)));
        Assert.True(near(Unanswered(responseEdge.AddTicks(-1))));
    }

    [Fact]
    public void The_view_filters_only_read_indexed_columns_on_both_engines()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        var engines = new[]
        {
            new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=localhost;Database=never-opened;Username=x;Password=x").Options,
            new DbContextOptionsBuilder<AppDbContext>().UseSqlServer("Server=unused;Database=unused;TrustServerCertificate=True").Options,
        };

        foreach (var options in engines)
        {
            using var db = new AppDbContext(options);
            var queries = new List<string> { db.Issues.Where(IssueFilters.NearOrOverdue(Now)).ToQueryString() };
            foreach (var rule in Enum.GetValues<IssueAttentionRule>())
            foreach (var queueWorker in new[] { true, false })
                queries.Add(db.Issues.Where(IssueFilters.AttentionMatches(rule, Guid.NewGuid(), queueWorker, Now)).ToQueryString());

            // The filter reads only status, assignee, reporter and deadlines (what the Issues indexes cover); never a text column or a join.
            foreach (var sql in queries)
            {
                Assert.DoesNotContain("JOIN", sql);
                var where = sql[sql.IndexOf("WHERE", StringComparison.Ordinal)..];
                foreach (var column in new[] { "Title", "Description", "ReporterName", "ReporterEmail", "UserAgent" })
                    Assert.DoesNotContain(column, where);
            }
        }
    }

    // As above: the in-memory database accepts anything, so check that both real engines can translate the attention counts,
    // and that they stay one query over one table.
    [Fact]
    public void The_attention_counts_translate_to_a_single_query_on_both_database_engines()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        var engines = new[]
        {
            new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=localhost;Database=never-opened;Username=x;Password=x").Options,
            new DbContextOptionsBuilder<AppDbContext>().UseSqlServer("Server=unused;Database=unused;TrustServerCertificate=True").Options,
        };

        foreach (var options in engines)
        foreach (var queueWorker in new[] { true, false })
        {
            using var db = new AppDbContext(options);

            var sql = IssueService.AttentionCounts(db.Issues, Guid.NewGuid(), queueWorker, Now).ToQueryString();

            Assert.Single(Regex.Matches(sql, "FROM [\\[\"]Issues[\\]\"]"));
            Assert.Contains("GROUP BY", sql);
            Assert.DoesNotContain("COUNT(NULL)", sql, StringComparison.OrdinalIgnoreCase);   // a rule that cannot apply must not become this: SQL Server refuses it
            Assert.Contains("ReporterUserId", sql);
            Assert.Contains("AssigneeId", sql);
            // Deadlines matter only to people who work the queue, so for everyone else they are not even read.
            Assert.Equal(queueWorker, sql.Contains("ResolveDueAt"));
            Assert.Equal(queueWorker, sql.Contains("ResponseDueAt"));
        }
    }
}
