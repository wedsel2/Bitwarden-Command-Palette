using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace BitwardenCommandPalette.Core.Bw;

/// <summary>Settings for <see cref="BwCli"/>.</summary>
/// <param name="ExecutablePath">Full path of <c>bw.exe</c>.</param>
public sealed record BwCliOptions(string ExecutablePath)
{
    /// <summary>How long one call may take before bw is stopped. Two minutes by default.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(2);
}

/// <summary>
/// The only code that starts a process (ADR 7). Secrets never go in an argument: the master
/// password reaches <c>bw unlock</c> through <c>--passwordenv</c>, the session key reaches every
/// other command as <c>BW_SESSION</c>, both on the child's environment only. Every call passes
/// <c>--nointeraction</c>. bw's output is parsed, never shown or logged (ADR 13).
/// </summary>
public sealed class BwCli : IBwCli
{
    /// <summary>The child environment variable that carries the master password.</summary>
    internal const string PasswordVariable = "BWCP_PW";

    /// <summary>The child environment variable that carries the session key.</summary>
    internal const string SessionVariable = "BW_SESSION";

    private readonly BwCliOptions options;

    /// <summary>Creates a client for the given <c>bw.exe</c>.</summary>
    /// <param name="options">Where bw is and how long a call may take.</param>
    public BwCli(BwCliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(options.ExecutablePath);

        // A bare name would be resolved through the working directory and PATH, where a planted
        // bw.exe would receive the master password.
        if (!Path.IsPathFullyQualified(options.ExecutablePath))
        {
            throw new ArgumentException("The path to bw.exe must be fully qualified.", nameof(options));
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.Timeout, TimeSpan.Zero);
        this.options = options;
    }

    /// <inheritdoc />
    public async Task<BwStatus> GetStatusAsync(SessionKey? session = null, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(["status"], SessionEnvironment(session), cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result);
        return BwOutput.ParseStatus(result.Stdout);
    }

    /// <inheritdoc />
    public async Task<SessionKey> UnlockAsync(MasterPassword password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(password);
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [PasswordVariable] = password.RevealForChildEnvironment(),
        };
        var result = await RunAsync(["unlock", "--passwordenv", PasswordVariable, "--raw"], environment, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result);
        var key = result.Stdout.Trim();
        return key.Length == 0 ? throw new BwCliException(BwErrorKind.UnexpectedOutput) : new SessionKey(key);
    }

    /// <inheritdoc />
    public async Task LockAsync(SessionKey? session = null, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(["lock"], SessionEnvironment(session), cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result);
    }

    /// <inheritdoc />
    public async Task SyncAsync(SessionKey session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var result = await RunAsync(["sync"], SessionEnvironment(session), cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BwLogin>> ListLoginsAsync(SessionKey session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var result = await RunAsync(["list", "items"], SessionEnvironment(session), cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result);
        return BwOutput.ParseLogins(result.Stdout);
    }

    /// <inheritdoc />
    public async Task<Secret> GetPasswordAsync(SessionKey session, string itemId, CancellationToken cancellationToken = default)
    {
        // A password is copied exactly as bw returns it, whitespace included.
        var value = await GetFieldAsync(session, "password", itemId, cancellationToken).ConfigureAwait(false);
        return value.Length == 0 ? throw new BwCliException(BwErrorKind.UnexpectedOutput) : new Secret(value);
    }

    /// <inheritdoc />
    public async Task<Secret> GetTotpAsync(SessionKey session, string itemId, CancellationToken cancellationToken = default)
    {
        var value = (await GetFieldAsync(session, "totp", itemId, cancellationToken).ConfigureAwait(false)).Trim();
        return value.Length == 0 ? throw new BwCliException(BwErrorKind.UnexpectedOutput) : new Secret(value);
    }

    /// <summary>Maps a failed call to one of our error kinds by bw's known messages.</summary>
    /// <param name="stderr">bw's error output, which is read here and never kept.</param>
    /// <returns>The kind of failure.</returns>
    internal static BwErrorKind Classify(string stderr)
    {
        if (stderr.Contains("You are not logged in", StringComparison.Ordinal))
        {
            return BwErrorKind.NotLoggedIn;
        }

        if (stderr.Contains("Vault is locked", StringComparison.Ordinal))
        {
            return BwErrorKind.VaultLocked;
        }

        if (stderr.Contains("Premium status is required", StringComparison.Ordinal))
        {
            return BwErrorKind.PremiumRequired;
        }

        if (stderr.Contains("More than one result was found", StringComparison.Ordinal))
        {
            return BwErrorKind.AmbiguousItem;
        }

        if (stderr.Contains("decryption operation failed", StringComparison.Ordinal)
            || stderr.Contains("Invalid master password", StringComparison.Ordinal))
        {
            return BwErrorKind.InvalidPassword;
        }

        // Matched as a whole line: bw or Node may print notices before it.
        var lines = stderr.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return lines.Contains("Not found.", StringComparer.Ordinal) ? BwErrorKind.NotFound : BwErrorKind.Failed;
    }

    private static Dictionary<string, string>? SessionEnvironment(SessionKey? session) =>
        session is null
            ? null
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [SessionVariable] = session.RevealForChildEnvironment() };

    private static void EnsureSuccess(BwResult result)
    {
        if (result.ExitCode != 0)
        {
            throw new BwCliException(Classify(result.Stderr));
        }
    }

    private static void StopProcess(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // It exited in the meantime.
        }
        catch (Win32Exception)
        {
            // It is exiting and can no longer be stopped.
        }
    }

    private async Task<string> GetFieldAsync(SessionKey session, string field, string itemId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(itemId);

        // bw get also accepts a search term; only an ID can never match the wrong item. bw
        // recognises only the hyphenated form, so braces or bare hex would become a search.
        // TryParseExact also accepts surrounding whitespace, which bw would not.
        if (!Guid.TryParseExact(itemId, "D", out var id)
            || !string.Equals(itemId, id.ToString("D"), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("An item ID is required, not a name or search term.", nameof(itemId));
        }

        var result = await RunAsync(["get", field, itemId], SessionEnvironment(session), cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result);
        return result.Stdout;
    }

    private ProcessStartInfo CreateStartInfo(IReadOnlyList<string> arguments, Dictionary<string, string>? environment)
    {
        var startInfo = new ProcessStartInfo(options.ExecutablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.ArgumentList.Add("--nointeraction");

        // The child gets exactly the secrets this call passes, never ones inherited from us.
        startInfo.Environment.Remove(SessionVariable);
        startInfo.Environment.Remove(PasswordVariable);
        foreach (var (name, value) in environment ?? [])
        {
            startInfo.Environment[name] = value;
        }

        return startInfo;
    }

    private async Task<BwResult> RunAsync(IReadOnlyList<string> arguments, Dictionary<string, string>? environment, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = CreateStartInfo(arguments, environment) };
        try
        {
            _ = process.Start();
        }
        catch (Win32Exception exception)
        {
            throw new BwCliException(BwErrorKind.CouldNotStart, exception);
        }

        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Timeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);

            // A process bw started could keep the pipes open after bw exits; the limit covers that too.
            await Task.WhenAll(stdout, stderr).WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            StopProcess(process);
            cancellationToken.ThrowIfCancellationRequested();
            throw new BwCliException(BwErrorKind.TimedOut);
        }

        return new BwResult(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
    }
}

/// <summary>One finished bw call. Holds raw output, so it is never printed.</summary>
/// <param name="exitCode">bw's exit code.</param>
/// <param name="stdout">bw's standard output.</param>
/// <param name="stderr">bw's error output.</param>
internal sealed class BwResult(int exitCode, string stdout, string stderr)
{
    /// <summary>bw's exit code.</summary>
    public int ExitCode { get; } = exitCode;

    /// <summary>bw's standard output; may hold secrets.</summary>
    public string Stdout { get; } = stdout;

    /// <summary>bw's error output; may hold vault data such as item names.</summary>
    public string Stderr { get; } = stderr;

    /// <summary>Only the exit code, never the output.</summary>
    /// <returns>A description without bw output.</returns>
    public override string ToString() => $"bw exited with {ExitCode}";
}
