using System;

namespace DcsOpsBoard.Components.Modals.AreYouSure;

public class AreYouSureService
{
    public Func<Task<bool>>? ShowConfirmation { get; set; }

    public string Title { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;

    public async Task<bool> ShowAndWait(string title, string body)
    {
        Title = title;
        Body = body;
        try
        {
            if (ShowConfirmation is not null)
            {
                bool result = await ShowConfirmation.Invoke();
                return result;
            }
        }
        finally
        {
            Title = string.Empty;
            Body = string.Empty;
        }
        return false;
    }
}
