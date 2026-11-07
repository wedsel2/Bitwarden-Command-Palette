namespace BitwardenCommandPalette.Core.Clipboard;

/// <summary>
/// The only way anything reaches the clipboard (ADR 11). Each copy is kept out of clipboard
/// history, cloud clipboard and clipboard managers, and cleared after <see cref="ClearDelay"/>
/// or at lock, but only while the clipboard still holds exactly what was copied.
/// </summary>
public interface IClipboardService
{
    /// <summary>
    /// How long a copy stays on the clipboard, from <see cref="ClipboardService.MinimumClearDelay"/>
    /// to <see cref="ClipboardService.MaximumClearDelay"/>. A change applies to the next copy.
    /// </summary>
    TimeSpan ClearDelay { get; set; }

    /// <summary>Copies a password or TOTP code and schedules the clear.</summary>
    /// <param name="secret">The value to copy.</param>
    /// <exception cref="ClipboardException">The clipboard could not be used.</exception>
    void Copy(Secret secret);

    /// <summary>Copies a username, by the same rule as a password (ADR 11).</summary>
    /// <param name="username">The username. It must not be null or empty.</param>
    /// <exception cref="ClipboardException">The clipboard could not be used.</exception>
    void CopyUsername(string username);

    /// <summary>
    /// Clears the clipboard now if it still holds the last copy, as at lock. A later copy by
    /// the user is never wiped.
    /// </summary>
    void ClearIfOurs();
}

/// <summary>The clipboard service of <see cref="IClipboardService"/>.</summary>
/// <remarks>
/// The last copied value is held as a <see cref="Secret"/> until it has been checked against
/// the clipboard, and dropped after (ADR 11). When the clipboard is busy at that moment, the
/// check is retried every <see cref="RetryInterval"/> up to <see cref="ClearAttempts"/> times,
/// then the value is dropped without clearing.
/// </remarks>
public sealed class ClipboardService : IClipboardService, IDisposable
{
    /// <summary>The clear delay unless the settings say otherwise.</summary>
    public static readonly TimeSpan DefaultClearDelay = TimeSpan.FromSeconds(20);

    /// <summary>The shortest clear delay allowed.</summary>
    public static readonly TimeSpan MinimumClearDelay = TimeSpan.FromSeconds(5);

    /// <summary>The longest clear delay allowed.</summary>
    public static readonly TimeSpan MaximumClearDelay = TimeSpan.FromSeconds(120);

    /// <summary>How many times a clear is tried while the clipboard is busy.</summary>
    internal const int ClearAttempts = 10;

    /// <summary>The wait between two clear attempts.</summary>
    internal static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(1);

    private readonly IClipboard clipboard;
    private readonly TimeProvider time;
    private readonly Lock gate = new();

    private TimeSpan clearDelay = DefaultClearDelay;
    private Secret? copied;
    private ITimer? timer;
    private int attempts;

    // Identifies the current copy, so a timer callback already queued for an earlier one does nothing.
    private long generation;
    private bool disposed;

    /// <summary>Creates the service on the Windows clipboard.</summary>
    public ClipboardService()
        : this(new Win32Clipboard(), TimeProvider.System)
    {
    }

    /// <summary>Creates the service on the given clipboard and clock.</summary>
    /// <param name="clipboard">The clipboard.</param>
    /// <param name="time">The clock that runs the clear timer.</param>
    internal ClipboardService(IClipboard clipboard, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(time);
        this.clipboard = clipboard;
        this.time = time;
    }

    /// <inheritdoc />
    public TimeSpan ClearDelay
    {
        get
        {
            lock (gate)
            {
                return clearDelay;
            }
        }

        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, MinimumClearDelay);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaximumClearDelay);
            lock (gate)
            {
                clearDelay = value;
            }
        }
    }

    /// <inheritdoc />
    public void Copy(Secret secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            // If this throws, the previous copy may still be on the clipboard, so its pending
            // clear stays; the new value is never held.
            clipboard.SetText(secret.RevealForClipboard());
            Forget();
            copied = secret;
            Schedule(clearDelay);
        }
    }

    /// <inheritdoc />
    public void CopyUsername(string username)
    {
        ArgumentException.ThrowIfNullOrEmpty(username);
        Copy(new Secret(username));
    }

    /// <inheritdoc />
    public void ClearIfOurs()
    {
        lock (gate)
        {
            if (disposed || copied is null)
            {
                return;
            }

            StopTimer();
            attempts = 0;
            TryClearOrRetry();
        }
    }

    /// <summary>Clears the clipboard if it still holds the last copy, once, and stops the timer.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            StopTimer();
            _ = TryClear();
            copied = null;
        }
    }

    private void OnTimer(long expected)
    {
        lock (gate)
        {
            if (disposed || expected != generation)
            {
                return;
            }

            TryClearOrRetry();
        }
    }

    // Caller holds the gate.
    private void TryClearOrRetry()
    {
        attempts++;
        if (!TryClear() && attempts < ClearAttempts)
        {
            Schedule(RetryInterval);
        }
        else
        {
            Forget();
        }
    }

    // Caller holds the gate. False when the clipboard could not be used.
    private bool TryClear()
    {
        if (copied is null)
        {
            return true;
        }

        try
        {
            _ = clipboard.ClearIfHolds(copied.RevealForClipboard());
            return true;
        }
        catch (ClipboardException)
        {
            return false;
        }
    }

    // Caller holds the gate.
    private void Schedule(TimeSpan delay)
    {
        StopTimer();
        var expected = generation;
        timer = time.CreateTimer(_ => OnTimer(expected), null, delay, Timeout.InfiniteTimeSpan);
    }

    // Caller holds the gate. Drops the held value and any pending clear.
    private void Forget()
    {
        StopTimer();
        copied = null;
        attempts = 0;
    }

    // Caller holds the gate.
    private void StopTimer()
    {
        generation++;
        timer?.Dispose();
        timer = null;
    }
}
