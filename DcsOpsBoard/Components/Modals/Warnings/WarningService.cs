using System;
using System.Threading.Tasks;

namespace DcsOpsBoard.Components.Modals.Warnings;

public class WarningService
{
    public Func<string, string, WarningSeverity, Task>? ShowWarning { get; set; }

    public async Task ShowWarningAsync(string title, string message, WarningSeverity severity)
    {
        if (ShowWarning is not null)
        {
            await ShowWarning.Invoke(title, message, severity);
        }
    }
}

public enum WarningSeverity
{
    INFO,
    WARNING,
    ERROR
}
