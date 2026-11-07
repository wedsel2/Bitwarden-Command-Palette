using Microsoft.Win32;

namespace BitwardenCommandPalette.Core.Session;

/// <summary>Why the vault was locked.</summary>
public enum LockReason
{
    /// <summary>The user ran the lock command.</summary>
    Command,

    /// <summary>No search or copy for the idle timeout.</summary>
    IdleTimeout,

    /// <summary>The absolute timeout after unlocking passed.</summary>
    AbsoluteTimeout,

    /// <summary>The system clock moved back, so neither timeout can be trusted.</summary>
    ClockChanged,

    /// <summary>Windows was locked, or the session was disconnected.</summary>
    WindowsLocked,

    /// <summary>The computer went to sleep.</summary>
    Sleep,

    /// <summary>The user is signing out or Windows is shutting down.</summary>
    SignOut,

    /// <summary>The extension is shutting down.</summary>
    Shutdown,

    /// <summary><c>bw</c> reported the vault locked, so our session key is no longer valid.</summary>
    CliLocked,

    /// <summary>Unlocking failed after the session key was issued.</summary>
    UnlockFailed,
}

/// <summary>Carries the reason for a lock.</summary>
/// <param name="reason">Why the vault is or should be locked.</param>
public sealed class LockEventArgs(LockReason reason) : EventArgs
{
    /// <summary>Why the vault is or should be locked.</summary>
    public LockReason Reason { get; } = reason;
}

/// <summary>Windows events that must lock the vault at once (ADR 9).</summary>
public interface ILockEvents : IDisposable
{
    /// <summary>Raised on Windows lock, sleep or sign-out, on whatever thread Windows uses.</summary>
    event EventHandler<LockEventArgs>? LockRequested;
}

/// <summary>
/// <see cref="ILockEvents"/> from <see cref="SystemEvents"/>, which runs its own hidden
/// window and message loop when the process has none, as an extension does not.
/// </summary>
/// <remarks>
/// Whether these events reach a Command Palette extension process is spike question 4. If they
/// do not, <c>WTSRegisterSessionNotification</c> replaces this class behind the same interface.
/// </remarks>
public sealed class SystemLockEvents : ILockEvents
{
    private bool disposed;

    /// <summary>Subscribes to the Windows session and power events.</summary>
    public SystemLockEvents()
    {
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionEnding += OnSessionEnding;
    }

    /// <inheritdoc />
    public event EventHandler<LockEventArgs>? LockRequested;

    /// <summary>Unsubscribes; the static Windows events would otherwise keep this alive.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionEnding -= OnSessionEnding;
    }

    /// <summary>The lock a session switch calls for, if any.</summary>
    /// <param name="reason">What happened to the Windows session.</param>
    /// <returns>The lock reason, or null for a switch that does not lock (such as an unlock).</returns>
    internal static LockReason? ReasonFor(SessionSwitchReason reason) => reason switch
    {
        SessionSwitchReason.SessionLock => LockReason.WindowsLocked,
        SessionSwitchReason.ConsoleDisconnect => LockReason.WindowsLocked,
        SessionSwitchReason.RemoteDisconnect => LockReason.WindowsLocked,
        SessionSwitchReason.SessionLogoff => LockReason.SignOut,
        _ => null,
    };

    /// <summary>The lock a power change calls for, if any.</summary>
    /// <param name="mode">The power change.</param>
    /// <returns>The lock reason, or null for a change that does not lock.</returns>
    internal static LockReason? ReasonFor(PowerModes mode) => mode == PowerModes.Suspend ? LockReason.Sleep : null;

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e) => Raise(ReasonFor(e.Reason));

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e) => Raise(ReasonFor(e.Mode));

    private void OnSessionEnding(object? sender, SessionEndingEventArgs e) => Raise(LockReason.SignOut);

    private void Raise(LockReason? reason)
    {
        if (reason is { } value)
        {
            LockRequested?.Invoke(this, new LockEventArgs(value));
        }
    }
}
