using System;

namespace DcsOpsBoard.Services;

public interface IUserMapSettings
{
    bool ShowBaseLayer { get; set; }
    bool ShowElevationShader { get; set; }
    bool ShowFriendlyCursors { get; set; }
    bool ShowMyCursor { get; set; }
    bool ShowDrawingLayer { get; set; }
    bool ShowBuildingObjects { get; set; }
    
    event Action OnSettingsChanged;
}

public class UserMapSettings : IUserMapSettings
{

    public event Action OnSettingsChanged = () => { };

    public void NotifySettingsChanged()
    {
        OnSettingsChanged.Invoke();
    }

    public bool ShowBaseLayer
    {
        get;
        set
        {
            if(field != value)
            {
                field = value;
                NotifySettingsChanged();
            }
        }
    } = true;

    public bool ShowElevationShader
    {
        get;
        set
        {
            if(field != value)
            {
                field = value;
                NotifySettingsChanged();
            }
        }
    } = true;

    public bool ShowFriendlyCursors
    {
        get;
        set
        {
            if(field != value)
            {
                field = value;
                NotifySettingsChanged();
            }
        }
    } = true;

    public bool ShowMyCursor
    {
        get;
        set
        {
            if(field != value)
            {
                field = value;
                NotifySettingsChanged();
            }
        }
    } = true;
    
    public bool ShowDrawingLayer
    {
        get;
        set
        {
            if(field != value)
            {
                field = value;
                NotifySettingsChanged();
            }
        }
    } = true;

    public bool ShowBuildingObjects
    {
        get;
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
