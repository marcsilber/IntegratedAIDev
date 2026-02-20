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
    Triaged,
    Approved,
    InProgress,
    Done,
    Rejected
}
