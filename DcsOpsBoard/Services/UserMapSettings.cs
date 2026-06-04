using System;

namespace DcsOpsBoard.Services;

public interface IUserMapSettings
{
    bool ShowBaseLayer { get; set; }
    MapMode MapMode { get; set; }
    bool ShowElevationShader { get; set; }
    bool ShowFriendlyCursors { get; set; }
    bool ShowMyCursor { get; set; }
    bool ShowDrawingLayer { get; set; }
    bool ShowBuildingObjects { get; set; }
    
    event Func<Task> OnSettingsChanged;
}


public class UserMapSettings : IUserMapSettings
{

    public event Func<Task> OnSettingsChanged = () => Task.CompletedTask;

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

    public MapMode MapMode
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
    } = MapMode.Default;

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

public enum MapMode
{
    Default = 0,
    Dark = 1
}
