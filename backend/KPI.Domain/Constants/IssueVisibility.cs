namespace KPI.Domain.Constants;

/// <summary>Whose issues a role may see. The numbers are stored in the database: do not renumber.</summary>
public enum IssueVisibility
{
    /// <summary>Only the issues the person reported.</summary>
    Own = 0,
    /// <summary>Their own, plus every issue reported in their department.</summary>
    Department = 1,
    /// <summary>Every issue.</summary>
    All = 2,
}
