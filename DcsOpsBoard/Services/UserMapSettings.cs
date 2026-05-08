using System;

namespace DcsOpsBoard.Services;

public interface IUserMapSettings
{
    bool ShowElevationShader { get; set; }

    event Action OnSettingsChanged;
}

public class UserMapSettings : IUserMapSettings
{

    public event Action OnSettingsChanged = () => { };

    public void NotifySettingsChanged()
    {
        OnSettingsChanged.Invoke();
    }

    public bool ShowElevationShader
    {
        get => field;
        set
        {
            if(field != value)
            {
                field = value;
                NotifySettingsChanged();
            }
        }
    } = true;

}
