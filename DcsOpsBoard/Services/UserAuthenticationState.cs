using System;
using Microsoft.AspNetCore.Components.Authorization;

namespace DcsOpsBoard.Services;

public interface IUserAuthenticationState
{
    Task EnsureLoaded();
}

public class UserAuthenticationState : IUserAuthenticationState
{
    public event Action OnAuthenticationStateChanged = () => { };

    public bool IsAuthenticated { get; private set; } = false;

    private bool _isLoaded = false;

    private readonly AuthenticationStateProvider _authenticationStateProvider;

    public UserAuthenticationState(AuthenticationStateProvider authenticationStateProvider)
    {
        _authenticationStateProvider = authenticationStateProvider;
        _authenticationStateProvider.AuthenticationStateChanged += AuthenticationStateChanged;
    }

    private void AuthenticationStateChanged(Task<AuthenticationState> task)
    {
        var authState = task.Result;
        IsAuthenticated = authState.User.Identity?.IsAuthenticated ?? false;
        OnAuthenticationStateChanged.Invoke();
    }

    public async Task EnsureLoaded()
    {
        if(_isLoaded) return;

        var authState = await _authenticationStateProvider.GetAuthenticationStateAsync();
        IsAuthenticated = authState.User.Identity?.IsAuthenticated ?? false;
        _isLoaded = true;
    }


}
