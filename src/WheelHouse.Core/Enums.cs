namespace WheelHouse.Core;

/// <summary>Lifecycle state of an orchestration session.</summary>
public enum SessionStatus
{
    Planning,
    Running,
    Paused,
    Completed,
    Failed
}

/// <summary>
/// State of an atomic unit of work. Named WorkItemStatus to avoid colliding
/// with System.Threading.Tasks.TaskStatus.
/// </summary>
public enum WorkItemStatus
{
    Pending,
    InProgress,
    Verifying,
    AwaitingApproval,
    Completed,
    Failed
}

/// <summary>How a <see cref="Models.CommandRule"/> matches an incoming command string.</summary>
public enum RuleMatchType
{
    Prefix,
    Exact,
    Regex,
    Contains
}

/// <summary>Action taken when a <see cref="Models.CommandRule"/> matches.</summary>
public enum RuleAction
{
    AutoApprove,
    AutoDeny,
    Prompt
}

/// <summary>Local RAG indexing state of a <see cref="Models.Workspace"/>.</summary>
public enum IndexState
{
    None,
    Queued,
    Indexing,
    Indexed,
    Failed,
    Unavailable
}

/// <summary>Assessed risk level of a task.</summary>
public enum RiskLevel
{
    Low,
    Medium,
    High
}
