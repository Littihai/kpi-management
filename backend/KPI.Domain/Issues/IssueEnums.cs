namespace KPI.Domain.Issues;

public enum IssueStatus
{
    New = 0,
    Assigned = 1,
    InProgress = 2,
    WaitingForReporter = 3,
    Resolved = 4,
    Closed = 5,
    Rejected = 6,
    Reopened = 7,

    /// <summary>Waiting for a vendor, a spare part, a budget... The resolve clock is paused, like WaitingForReporter.</summary>
    OnHold = 8,
    // Reserved (append-only numbers, stored as ints): Merged = 9, PendingApproval = 10.
}

public enum IssuePriority
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3,
}

/// <summary>How badly the reporter is affected. IT turns this into a priority.</summary>
public enum IssueImpact
{
    Minor = 0,
    Degraded = 1,
    Blocking = 2,
}

public enum IssueCategory
{
    Bug = 0,          // not working / error
    WrongData = 1,
    Slow = 2,         // slow or hangs
    Improvement = 3,  // request a change
    HowTo = 4,        // question about using it
}

/// <summary>Which part of the system, or "general IT", the issue is about.</summary>
public enum IssueModule
{
    Application = 0,  // the main program / business system (was "Kpi" when this product tracked KPIs)
    Login = 1,
    Dashboard = 2,
    Export = 3,
    Users = 4,
    Issues = 5,
    ItGeneral = 6,   // computers, network, printers... anything that is not this program
    Other = 7,
}

public enum IssueRootCause
{
    None = 0,
    CodeBug = 1,
    DataIssue = 2,
    UserError = 3,
    Configuration = 4,
    Infrastructure = 5,
    CannotReproduce = 6,
    Other = 7,
}
