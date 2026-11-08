using System.Runtime.InteropServices;
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

    /// <summary>The display turned off, as it does when a Modern Standby PC sleeps (ADR 16).</summary>
    DisplayOff,
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
    /// <summary>
    /// Raised on Windows lock, sleep, sign-out or the display turning off, on whatever thread
    /// Windows uses.
    /// </summary>
    event EventHandler<LockEventArgs>? LockRequested;
}

/// <summary>
/// <see cref="ILockEvents"/> from <see cref="SystemEvents"/>, which runs its own hidden
/// window and message loop when the process has none, as an extension does not, and from the
/// console display state (ADR 16).
/// </summary>
/// <remarks>
/// The live test (spike question 4) showed the session events reach the extension process, and
/// that a Modern Standby PC sends no <c>PowerModeChanged</c> when it sleeps: it only locks the
/// session, and not even that when sign-in after sleep is set to Never. The display always turns
/// off, so that locks too.
/// </remarks>
public sealed partial class SystemLockEvents : ILockEvents
{
    // GUID_CONSOLE_DISPLAY_STATE: the display of the console session is off (0), on (1) or dimmed (2).
    private static readonly Guid ConsoleDisplayState = new("6FE69556-704A-47A0-8F24-C28D936FDA47");

    private const uint DeviceNotifyCallback = 2;
    private const uint PowerSettingChange = 0x8013;
    private const int DisplayOffState = 0;

    // POWERBROADCAST_SETTING: a GUID, a DWORD data length, then the data.
    private const int SettingLengthOffset = 16;
    private const int SettingDataOffset = 20;

    // Kept in fields for as long as Windows may call back: the delegate and the parameter block
    // it points through must not be collected or freed while registered.
    private readonly PowerSettingCallback callback;
    private readonly nint subscribeParameters;
    private readonly nint registration;
    private bool disposed;

    /// <summary>Subscribes to the Windows session, power and display events.</summary>
    public SystemLockEvents()
    {
        // The display subscription first: if it throws, nothing static refers to this object yet.
        callback = OnPowerSetting;
        subscribeParameters = Marshal.AllocHGlobal(2 * nint.Size);
        try
        {
            Marshal.WriteIntPtr(subscribeParameters, Marshal.GetFunctionPointerForDelegate(callback));
            Marshal.WriteIntPtr(subscribeParameters, nint.Size, 0);
            if (PowerSettingRegisterNotification(in ConsoleDisplayState, DeviceNotifyCallback, subscribeParameters, out registration) != 0)
            {
                // The session events of ADR 9 still apply; IsWatchingDisplay says this one did not.
                registration = 0;
            }
        }
        catch
        {
            Marshal.FreeHGlobal(subscribeParameters);
            throw;
        }

        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionEnding += OnSessionEnding;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint PowerSettingCallback(nint context, uint type, nint setting);

    /// <inheritdoc />
    public event EventHandler<LockEventArgs>? LockRequested;

    /// <summary>
    /// Whether Windows accepted the display state subscription (ADR 16). When false, only the
    /// session and power events lock, and the extension can say so.
    /// </summary>
    public bool IsWatchingDisplay => registration != 0;

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
        if (registration != 0)
        {
            _ = PowerSettingUnregisterNotification(registration);
        }

        Marshal.FreeHGlobal(subscribeParameters);

        // A callback Windows started before the unregistration must still find its delegate.
        GC.KeepAlive(callback);
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

    /// <summary>The lock a console display state calls for, if any (ADR 16).</summary>
    /// <param name="state">Off (0), on (1) or dimmed (2).</param>
    /// <returns>The lock reason when the display turned off, otherwise null.</returns>
    internal static LockReason? ReasonForDisplayState(int state) => state == DisplayOffState ? LockReason.DisplayOff : null;

    /// <summary>The lock a power setting notification calls for, if any.</summary>
    /// <param name="type">The notification type Windows passed.</param>
    /// <param name="setting">The POWERBROADCAST_SETTING Windows passed, or zero.</param>
    /// <returns>The lock reason for a display that turned off, otherwise null.</returns>
    internal static LockReason? ReasonForPowerSetting(uint type, nint setting)
    {
        if (type != PowerSettingChange || setting == 0
            || Marshal.PtrToStructure<Guid>(setting) != ConsoleDisplayState
            || Marshal.ReadInt32(setting, SettingLengthOffset) < sizeof(int))
        {
            return null;
        }

        return ReasonForDisplayState(Marshal.ReadInt32(setting, SettingDataOffset));
    }

    [LibraryImport("powrprof.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint PowerSettingRegisterNotification(in Guid settingGuid, uint flags, nint recipient, out nint registrationHandle);

    [LibraryImport("powrprof.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint PowerSettingUnregisterNotification(nint registrationHandle);

    // Runs on a Windows thread pool thread. Windows also calls it once with the current state on
    // registration, which only locks if the display is already off.
    private uint OnPowerSetting(nint context, uint type, nint setting)
    {
        Raise(ReasonForPowerSetting(type, setting));
        return 0;
    }

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
