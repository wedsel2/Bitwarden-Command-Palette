using System.Runtime.InteropServices;
using System.Text.Json;
using BitwardenCommandPalette.Core.Bw;
using BitwardenCommandPalette.Core.Session;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Win32;

namespace BitwardenCommandPalette.Core.Tests;

/// <summary>
/// ADR 13: runs every SessionManager flow with known secrets and fails if one appears in an
/// outcome, an event, an exception, a printed or serialised value, or our own environment.
/// </summary>
public sealed class SessionCanaryTests
{
    private const string Id = "00000000-0000-4000-8000-000000000001";
    private const string MasterPasswordCanary = "canary-session-master-2b71";
    private const string SessionKeyCanary = "canary-session-key-a40c";
    private const string PasswordCanary = "canary-session-password-e93f";

    [Fact]
    public async Task NoSecretLeavesSessionManager()
    {
        var token = TestContext.Current.CancellationToken;
        var cli = new FakeCli { CorrectPassword = MasterPasswordCanary, PasswordValue = PasswordCanary, SessionKeyPrefix = SessionKeyCanary };
        cli.Logins.Add(new BwLogin(Id, "Example", "alice", [], null, HasTotp: true, RequiresReprompt: false));
        var clipboard = new FakeClipboardService();
        using var events = new FakeLockEvents();
        var time = new FakeTimeProvider();
        var printed = new List<object?>();
        var session = new SessionManager(cli, clipboard, events, time);
        await using (session.ConfigureAwait(false))
        {
            session.Locked += (sender, e) => printed.Add(e.Reason);

            printed.Add(await session.UnlockAsync(new MasterPassword("wrong"), token));
            printed.Add(await session.UnlockAsync(new MasterPassword(MasterPasswordCanary), token));
            printed.Add(await session.CopyAsync(Id, CopyField.Password, cancellationToken: token));
            printed.AddRange(session.Search(null));
            cli.FailGet = BwErrorKind.PremiumRequired;
            printed.Add(await Assert.ThrowsAsync<BwCliException>(() => session.CopyAsync(Id, CopyField.Totp, cancellationToken: token)));
            cli.FailGet = BwErrorKind.VaultLocked;
            printed.Add(await session.CopyAsync(Id, CopyField.Password, cancellationToken: token));
            cli.FailList = BwErrorKind.UnexpectedOutput;
            printed.Add(await Assert.ThrowsAsync<BwCliException>(() => session.UnlockAsync(new MasterPassword(MasterPasswordCanary), token)));
            printed.Add(session);
        }

        Assert.Equal([PasswordCanary], clipboard.Copied);
        var texts = printed.Select(value => value?.ToString() ?? string.Empty)
            .Append(JsonSerializer.Serialize(printed.Select(value => value?.ToString())))
            .ToList();
        foreach (var canary in new[] { MasterPasswordCanary, SessionKeyCanary, PasswordCanary })
        {
            Assert.DoesNotContain(texts, text => text.Contains(canary, StringComparison.Ordinal));
            Assert.DoesNotContain(
                Environment.GetEnvironmentVariables().Values.Cast<object>(),
                value => value.ToString()!.Contains(canary, StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData(SessionSwitchReason.SessionLock, LockReason.WindowsLocked)]
    [InlineData(SessionSwitchReason.ConsoleDisconnect, LockReason.WindowsLocked)]
    [InlineData(SessionSwitchReason.RemoteDisconnect, LockReason.WindowsLocked)]
    [InlineData(SessionSwitchReason.SessionLogoff, LockReason.SignOut)]
    public void SessionSwitchesThatLeaveTheUserLock(SessionSwitchReason reason, LockReason expected)
    {
        Assert.Equal(expected, SystemLockEvents.ReasonFor(reason));
    }

    [Theory]
    [InlineData(SessionSwitchReason.SessionUnlock)]
    [InlineData(SessionSwitchReason.SessionLogon)]
    [InlineData(SessionSwitchReason.ConsoleConnect)]
    [InlineData(SessionSwitchReason.RemoteConnect)]
    public void SessionSwitchesThatReturnToTheUserDoNotLock(SessionSwitchReason reason)
    {
        Assert.Null(SystemLockEvents.ReasonFor(reason));
    }

    [Theory]
    [InlineData(PowerModes.Suspend, LockReason.Sleep)]
    [InlineData(PowerModes.Resume, null)]
    [InlineData(PowerModes.StatusChange, null)]
    public void OnlySuspendLocksOnPowerChanges(PowerModes mode, LockReason? expected)
    {
        Assert.Equal(expected, SystemLockEvents.ReasonFor(mode));
    }

    [Fact]
    public void SystemLockEventsSubscribesAndUnsubscribesTwiceWithoutError()
    {
        var watching = false;

        // Windows may call back at once with the current display state; on a headless runner
        // that can be "off", so the number of requests is not asserted here.
        var error = Record.Exception(() =>
        {
            var events = new SystemLockEvents();
            events.LockRequested += (_, _) => { };
            watching = events.IsWatchingDisplay;
            events.Dispose();
            events.Dispose();
        });

        Assert.Null(error);
        Assert.True(watching);
    }

    [Theory]
    [InlineData(0, LockReason.DisplayOff)]
    [InlineData(1, null)]
    [InlineData(2, null)]
    public void OnlyADisplayThatTurnsOffLocks(int state, LockReason? expected)
    {
        Assert.Equal(expected, SystemLockEvents.ReasonForDisplayState(state));
    }

    [Fact]
    public void ADisplayOffNotificationIsReadFromThePowerSetting()
    {
        using var setting = new PowerSetting(DisplayStateGuid, length: 4, data: 0);

        Assert.Equal(LockReason.DisplayOff, SystemLockEvents.ReasonForPowerSetting(PowerSettingChange, setting.Pointer));
    }

    [Fact]
    public void OtherNotificationsDoNotLock()
    {
        using var on = new PowerSetting(DisplayStateGuid, length: 4, data: 1);
        using var otherSetting = new PowerSetting(Guid.NewGuid(), length: 4, data: 0);
        using var tooShort = new PowerSetting(DisplayStateGuid, length: 1, data: 0);
        using var off = new PowerSetting(DisplayStateGuid, length: 4, data: 0);

        Assert.Null(SystemLockEvents.ReasonForPowerSetting(PowerSettingChange, on.Pointer));
        Assert.Null(SystemLockEvents.ReasonForPowerSetting(PowerSettingChange, otherSetting.Pointer));
        Assert.Null(SystemLockEvents.ReasonForPowerSetting(PowerSettingChange, tooShort.Pointer));
        Assert.Null(SystemLockEvents.ReasonForPowerSetting(PowerSettingChange, 0));
        Assert.Null(SystemLockEvents.ReasonForPowerSetting(0x0004, off.Pointer));
    }

    private const uint PowerSettingChange = 0x8013;

    private static readonly Guid DisplayStateGuid = new("6FE69556-704A-47A0-8F24-C28D936FDA47");

    /// <summary>A POWERBROADCAST_SETTING in unmanaged memory, as Windows passes it.</summary>
    private sealed class PowerSetting : IDisposable
    {
        public PowerSetting(Guid guid, int length, int data)
        {
            Pointer = Marshal.AllocHGlobal(24);
            Marshal.Copy(guid.ToByteArray(), 0, Pointer, 16);
            Marshal.WriteInt32(Pointer, 16, length);
            Marshal.WriteInt32(Pointer, 20, data);
        }

        public nint Pointer { get; }

        public void Dispose() => Marshal.FreeHGlobal(Pointer);
    }
}
