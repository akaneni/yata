using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Yata.Services;

public enum GoogleAccountStatus { NotConfigured, SignedOut, Connecting, Connected, NeedsReconnect }

/// <summary>
/// The one Google account shared by the calendar and tasks. Each feature asks for its own permission
/// when turned on; disconnecting revokes the access for both.
/// </summary>
public sealed class GoogleAccount
{
    private CancellationTokenSource? _signIn;
    private bool _signInCancelled;

    internal GoogleAuth Auth { get; } = new();

    public GoogleAccountStatus Status { get; private set; }
    public string? Email => Auth.Email;
    /// <summary>Why the last connection attempt failed, or null.</summary>
    public string? Error { get; private set; }

    public event Action? Changed;

    public GoogleAccount()
    {
        Status = !Auth.IsConfigured ? GoogleAccountStatus.NotConfigured
               : Auth.IsSignedIn ? GoogleAccountStatus.Connected
               : GoogleAccountStatus.SignedOut;
    }

    public bool Has(IEnumerable<string> scopes) => Status == GoogleAccountStatus.Connected && Auth.Has(scopes);

    /// <summary>Signs in (or adds a permission) in the browser.</summary>
    /// <param name="missing">Shown when the user unticks the permission that was asked for.</param>
    /// <returns>Whether the access now covers <paramref name="scopes"/>.</returns>
    public async Task<bool> ConnectAsync(string[] scopes, string missing)
    {
        if (Status is GoogleAccountStatus.NotConfigured or GoogleAccountStatus.Connecting)
            return false;

        var cts = _signIn = new CancellationTokenSource();
        _signInCancelled = false;
        SetStatus(GoogleAccountStatus.Connecting, null);
        try
        {
            bool granted = await Auth.SignInAsync(scopes, cts.Token);
            SetStatus(GoogleAccountStatus.Connected, granted ? null : missing);
            return granted;
        }
        catch (OperationCanceledException)
        {
            SetStatus(SignedOutStatus, _signInCancelled ? null : "Tempo esgotado esperando o navegador.");
        }
        catch (GoogleAuthException ex)
        {
            SetStatus(SignedOutStatus, ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Net.Sockets.SocketException)
        {
            SetStatus(SignedOutStatus, "Sem conexão com o Google.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            SetStatus(SignedOutStatus, "Não foi possível abrir o navegador.");
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or
                                   System.Security.Cryptography.CryptographicException)
        {
            SetStatus(SignedOutStatus, "Não foi possível guardar o acesso neste PC.");
        }
        finally
        {
            if (_signIn == cts)
                _signIn = null;
            cts.Dispose();
        }
        return false;
    }

    public void CancelConnect()
    {
        _signInCancelled = true;
        _signIn?.Cancel();
    }

    public async Task DisconnectAsync()
    {
        CancelConnect();
        SetStatus(GoogleAccountStatus.SignedOut, null);
        await Auth.SignOutAsync();
    }

    /// <summary>A feature found the access revoked or expired.</summary>
    internal void Lost(GoogleAuthException ex)
    {
        if (ex.NeedsReconnect && Status == GoogleAccountStatus.Connected)
            SetStatus(GoogleAccountStatus.NeedsReconnect, ex.Message);
    }

    /// <summary>A connection that was already signed in keeps "Connected" when a new attempt fails.</summary>
    private GoogleAccountStatus SignedOutStatus =>
        Auth.NeedsReconnect ? GoogleAccountStatus.NeedsReconnect
        : Auth.IsSignedIn ? GoogleAccountStatus.Connected
        : GoogleAccountStatus.SignedOut;

    private void SetStatus(GoogleAccountStatus status, string? error)
    {
        Status = status;
        Error = error;
        Changed?.Invoke();
    }
}
