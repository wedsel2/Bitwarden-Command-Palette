using System.Text.Json;
using BitwardenCommandPalette.Core.Bw;

namespace BitwardenCommandPalette.Core.Tests;

/// <summary>
/// ADR 13: runs every BwCli flow, successful and failing, with known secrets and fails if one
/// appears in a child's arguments, our own environment, an exception or a printed result.
/// </summary>
[Collection(FakeBwGroup.Name)]
public sealed class BwCliCanaryTests : IDisposable
{
    private const string Id = "00000000-0000-4000-8000-000000000005";
    private const string MasterPasswordCanary = "canary-master-password-0e6d";
    private const string RecordedPassword = "fixture-password-full";
    private const string StderrCanary = "canary-stderr-item-name-91fa";

    private readonly FakeBwHost bw = new();

    public void Dispose() => bw.Dispose();

    [Fact]
    public async Task NoSecretLeavesThroughArgumentsMessagesOrPrintedValues()
    {
        var token = TestContext.Current.CancellationToken;
        var cli = FakeBwHost.CreateCli();
        string[] unlock = ["unlock", "--passwordenv", "BWCP_PW", "--raw"];
        bw.Reply("unlock", unlock)
            .Reply("status-unlocked", "status")
            .Reply("sync", "sync")
            .Reply("list-items", "list", "items")
            .Reply("get-password-full-login", "get", "password", Id)
            .Respond(0, "123456", string.Empty, ["get", "totp", Id])
            .Reply("lock", "lock");

        var printed = new List<string>();
        var session = await cli.UnlockAsync(new MasterPassword(MasterPasswordCanary), token);
        printed.Add(session.ToString());
        printed.Add(JsonSerializer.Serialize(new { session }));
        printed.Add((await cli.GetStatusAsync(session, token)).ToString());
        await cli.SyncAsync(session, token);
        printed.AddRange((await cli.ListLoginsAsync(session, token)).Select(login => login.ToString()));
        var password = await cli.GetPasswordAsync(session, Id, token);
        printed.Add(password.ToString());
        printed.Add($"{password}");
        printed.Add((await cli.GetTotpAsync(session, Id, token)).ToString());
        await cli.LockAsync(session, token);

        var errors = await FailEveryWayAsync(cli, session, token);

        string[] canaries = [MasterPasswordCanary, FakeBwHost.RecordedSessionKey, RecordedPassword, StderrCanary, "123456"];
        var texts = printed.Concat(errors.Select(error => error.ToString())).ToList();
        foreach (var canary in canaries)
        {
            Assert.DoesNotContain(texts, text => text.Contains(canary, StringComparison.Ordinal));
            Assert.DoesNotContain(bw.Calls, call => call.Args.Any(argument => argument.Contains(canary, StringComparison.Ordinal)));
            Assert.DoesNotContain(
                Environment.GetEnvironmentVariables().Values.Cast<object>(),
                value => value.ToString()!.Contains(canary, StringComparison.Ordinal));
        }

        // The secrets did reach the children, through the environment.
        Assert.Contains(bw.Calls, call => call.Password == MasterPasswordCanary);
        Assert.Contains(bw.Calls, call => call.Session == FakeBwHost.RecordedSessionKey);
    }

    private async Task<List<Exception>> FailEveryWayAsync(BwCli cli, SessionKey session, CancellationToken token)
    {
        var failing = new FakeBwHost();
        try
        {
            string[] unlock = ["unlock", "--passwordenv", "BWCP_PW", "--raw"];
            failing.Respond(1, string.Empty, StderrCanary + " decryption operation failed", unlock)
                .Respond(1, StderrCanary, StderrCanary, ["status"])
                .Respond(0, StderrCanary, string.Empty, ["list", "items"])
                .Respond(1, RecordedPassword, StderrCanary + " Vault is locked.", ["get", "password", Id])
                .Respond(1, string.Empty, StderrCanary + " Premium status is required", ["get", "totp", Id])
                .Respond(1, string.Empty, "More than one result was found. " + StderrCanary, ["sync"])
                .Respond(0, "{\"x\":\"" + StderrCanary + "\"}", string.Empty, ["lock"], delayMs: 15_000);
            var impatient = FakeBwHost.CreateCli(TimeSpan.FromSeconds(3));

            Func<Task>[] calls =
            [
                () => cli.UnlockAsync(new MasterPassword(MasterPasswordCanary), token),
                () => cli.GetStatusAsync(session, token),
                () => cli.ListLoginsAsync(session, token),
                () => cli.GetPasswordAsync(session, Id, token),
                () => cli.GetTotpAsync(session, Id, token),
                () => cli.SyncAsync(session, token),
                () => impatient.LockAsync(cancellationToken: token),
            ];
            var errors = new List<Exception>();
            foreach (var call in calls)
            {
                errors.Add(await Assert.ThrowsAsync<BwCliException>(call).ConfigureAwait(true));
            }

            Assert.Equal(calls.Length, failing.Calls.Count);
            return errors;
        }
        finally
        {
            failing.Dispose();

            // FakeBwHost.Dispose clears the scenario; point it back at the outer one.
            Environment.SetEnvironmentVariable("FAKEBW_SCENARIO", bw.ScenarioFile);
        }
    }
}
