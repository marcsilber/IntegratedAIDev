namespace AIDev.Api.Models;

public enum RequestType
{
    Bug,
    Feature,
    Enhancement,
    Question
}

public enum Priority
{
    Low,
    Medium,
    High,
    Critical
}

public enum RequestStatus
{
    New,
    NeedsClarification,
    Triaged,
    Approved,
    InProgress,
    Done,
    Rejected
}

public enum AgentDecision
{
    Approve,
    Reject,
    Clarify
}
