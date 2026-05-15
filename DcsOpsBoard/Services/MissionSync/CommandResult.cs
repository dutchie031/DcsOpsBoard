using System;

namespace DcsOpsBoard.Services.MissionSync;

public class CommandResult
{
    public bool Succeeded { get; private set; }
    public string? ErrorMessage { get; private set; }

    private CommandResult(bool succeeded, string? errorMessage)
    {
        Succeeded = succeeded;
        ErrorMessage = errorMessage;
    }

    public static CommandResult Success() => new (true, null);
    public static CommandResult Failure(string errorMessage) => new (false, errorMessage);
}
