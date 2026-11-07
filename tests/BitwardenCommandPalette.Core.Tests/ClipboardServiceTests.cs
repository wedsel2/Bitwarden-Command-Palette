using BitwardenCommandPalette.Core.Clipboard;
using Microsoft.Extensions.Time.Testing;

namespace BitwardenCommandPalette.Core.Tests;

public sealed class ClipboardServiceTests : IDisposable
{
    private const string Password = "canary-clipboard-password-4c1e";
    private const string Username = "canary-clipboard-user-77b0";

    private readonly FakeClipboard clipboard = new();
    private readonly FakeTimeProvider time = new();
    private readonly ClipboardService service;

    public ClipboardServiceTests() => service = new ClipboardService(clipboard, time);

    public void Dispose() => service.Dispose();

    [Fact]
    public void ACopyIsMarkedAndClearedAfterTheDefaultDelay()
    {
        service.Copy(new Secret(Password));

        Assert.Equal(Password, clipboard.Text);
        Assert.True(clipboard.Marked);

        time.Advance(ClipboardService.DefaultClearDelay - TimeSpan.FromMilliseconds(1));
        Assert.Equal(Password, clipboard.Text);

        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Null(clipboard.Text);
    }

    [Fact]
    public void AUsernameFollowsTheSameRule()
    {
        service.CopyUsername(Username);

        Assert.Equal(Username, clipboard.Text);
        Assert.True(clipboard.Marked);
        time.Advance(ClipboardService.DefaultClearDelay);
        Assert.Null(clipboard.Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AnEmptyUsernameIsRefused(string? username)
    {
        Assert.ThrowsAny<ArgumentException>(() => service.CopyUsername(username!));
        Assert.Equal(0, clipboard.Writes);
    }

    [Fact]
    public void ALaterCopyByTheUserIsNeverWiped()
    {
        service.Copy(new Secret(Password));
        clipboard.UserCopies("something the user copied");

        time.Advance(ClipboardService.DefaultClearDelay);
        service.ClearIfOurs();

        Assert.Equal("something the user copied", clipboard.Text);
        Assert.Equal(0, clipboard.Clears);
    }

    [Fact]
    public void ANewCopyReplacesThePendingClear()
    {
        service.Copy(new Secret(Password));
        time.Advance(TimeSpan.FromSeconds(15));
        service.CopyUsername(Username);

        time.Advance(TimeSpan.FromSeconds(15));
        Assert.Equal(Username, clipboard.Text);

        time.Advance(TimeSpan.FromSeconds(5));
        Assert.Null(clipboard.Text);
        Assert.Equal(1, clipboard.Clears);
    }

    [Fact]
    public void ClearIfOursClearsNowAndCancelsTheTimer()
    {
        service.Copy(new Secret(Password));

        service.ClearIfOurs();
        Assert.Null(clipboard.Text);

        clipboard.UserCopies(Password);
        time.Advance(ClipboardService.MaximumClearDelay);
        Assert.Equal(Password, clipboard.Text);
        Assert.Equal(1, clipboard.Clears);
    }

    [Fact]
    public void ClearIfOursWithoutACopyDoesNothing()
    {
        clipboard.UserCopies("user text");

        service.ClearIfOurs();

        Assert.Equal("user text", clipboard.Text);
        Assert.Equal(0, clipboard.Reads);
    }

    [Fact]
    public void TheValueIsDroppedAfterTheCheck()
    {
        service.Copy(new Secret(Password));
        time.Advance(ClipboardService.DefaultClearDelay);
        var reads = clipboard.Reads;

        clipboard.UserCopies(Password);
        service.ClearIfOurs();
        time.Advance(ClipboardService.MaximumClearDelay);

        Assert.Equal(reads, clipboard.Reads);
        Assert.Equal(Password, clipboard.Text);
    }

    [Fact]
    public void TheConfiguredDelayAppliesToTheNextCopy()
    {
        service.ClearDelay = ClipboardService.MinimumClearDelay;
        service.Copy(new Secret(Password));

        time.Advance(ClipboardService.MinimumClearDelay);

        Assert.Null(clipboard.Text);
        Assert.Equal(ClipboardService.MinimumClearDelay, service.ClearDelay);
    }

    [Theory]
    [InlineData(4.999)]
    [InlineData(120.001)]
    [InlineData(0)]
    [InlineData(-1)]
    public void ADelayOutsideTheRangeIsRefused(double seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => service.ClearDelay = TimeSpan.FromSeconds(seconds));
        Assert.Equal(ClipboardService.DefaultClearDelay, service.ClearDelay);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(120)]
    public void TheBoundsAreAllowed(int seconds)
    {
        service.ClearDelay = TimeSpan.FromSeconds(seconds);

        Assert.Equal(TimeSpan.FromSeconds(seconds), service.ClearDelay);
    }

    [Fact]
    public void AFailedCopyHoldsNothingAndSchedulesNothing()
    {
        clipboard.FailNext = 1;

        Assert.Throws<ClipboardException>(() => service.Copy(new Secret(Password)));

        time.Advance(ClipboardService.MaximumClearDelay);
        service.ClearIfOurs();
        Assert.Equal(0, clipboard.Reads);
    }

    [Fact]
    public void AFailedCopyKeepsThePreviousCopysClear()
    {
        service.Copy(new Secret(Password));
        clipboard.FailNext = 1;

        Assert.Throws<ClipboardException>(() => service.CopyUsername(Username));
        Assert.Equal(Password, clipboard.Text);

        time.Advance(ClipboardService.DefaultClearDelay);
        Assert.Null(clipboard.Text);
    }

    [Fact]
    public void ABusyClipboardIsRetriedUntilItClears()
    {
        service.Copy(new Secret(Password));
        clipboard.FailNext = 3;

        time.Advance(ClipboardService.DefaultClearDelay);
        Assert.Equal(Password, clipboard.Text);

        AdvanceRetries(2);
        Assert.Equal(Password, clipboard.Text);

        AdvanceRetries(1);
        Assert.Null(clipboard.Text);
    }

    [Fact]
    public void AClipboardThatStaysBusyIsGivenUpAfterTheLastAttempt()
    {
        service.Copy(new Secret(Password));
        clipboard.FailNext = int.MaxValue;

        time.Advance(ClipboardService.DefaultClearDelay);
        AdvanceRetries(ClipboardService.ClearAttempts * 2);
        Assert.Equal(ClipboardService.ClearAttempts, clipboard.FailedCalls);

        clipboard.FailNext = 0;
        time.Advance(ClipboardService.MaximumClearDelay);
        service.ClearIfOurs();
        Assert.Equal(Password, clipboard.Text);
    }

    [Fact]
    public void ClearIfOursRetriesWhenTheClipboardIsBusy()
    {
        service.Copy(new Secret(Password));
        clipboard.FailNext = 1;

        service.ClearIfOurs();
        Assert.Equal(Password, clipboard.Text);

        time.Advance(ClipboardService.RetryInterval);
        Assert.Null(clipboard.Text);
    }

    [Fact]
    public void DisposeClearsOursOnceAndRefusesLaterCopies()
    {
        service.Copy(new Secret(Password));

        service.Dispose();
        service.Dispose();

        Assert.Null(clipboard.Text);
        Assert.Throws<ObjectDisposedException>(() => service.Copy(new Secret(Password)));
        service.ClearIfOurs();
        time.Advance(ClipboardService.MaximumClearDelay);
        Assert.Equal(1, clipboard.Clears);
    }

    [Fact]
    public void NoCopiedValueAppearsInAnExceptionOrAPrintedValue()
    {
        clipboard.FailNext = 2;
        var errors = new List<Exception>
        {
            Assert.Throws<ClipboardException>(() => service.Copy(new Secret(Password))),
            Assert.Throws<ClipboardException>(() => service.CopyUsername(Username)),
        };

        string[] texts = [.. errors.Select(error => error.ToString()), service.ToString()!, new Secret(Password).ToString()];
        foreach (var canary in new[] { Password, Username })
        {
            Assert.DoesNotContain(texts, text => text.Contains(canary, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ThePublicConstructorTouchesTheClipboardOnlyWhenUsed()
    {
        using var real = new ClipboardService();

        Assert.Equal(ClipboardService.DefaultClearDelay, real.ClearDelay);
        real.ClearIfOurs();
    }

    [Fact]
    public void ClipboardExceptionKeepsItsMessageAndCause()
    {
        var cause = new InvalidOperationException("cause");

        Assert.False(string.IsNullOrEmpty(new ClipboardException().Message));
        Assert.Equal("busy", new ClipboardException("busy").Message);
        Assert.Same(cause, new ClipboardException("busy", cause).InnerException);
    }

    [Fact]
    public void TheConstructorRefusesNull()
    {
        Assert.Throws<ArgumentNullException>(() => new ClipboardService(null!, time));
        Assert.Throws<ArgumentNullException>(() => new ClipboardService(clipboard, null!));
        Assert.Throws<ArgumentNullException>(() => service.Copy(null!));
    }

    // The fake clock fires one timer per Advance; a retry scheduled inside a callback needs its own.
    private void AdvanceRetries(int count)
    {
        for (var i = 0; i < count; i++)
        {
            time.Advance(ClipboardService.RetryInterval);
        }
    }

    /// <summary>An in-memory clipboard that can pretend to be busy.</summary>
    private sealed class FakeClipboard : IClipboard
    {
        public string? Text { get; private set; }

        public bool Marked { get; private set; }

        public int Writes { get; private set; }

        public int Reads { get; private set; }

        public int Clears { get; private set; }

        public int FailedCalls { get; private set; }

        /// <summary>How many of the next calls throw as if another application held the clipboard.</summary>
        public int FailNext { get; set; }

        public void UserCopies(string text)
        {
            Text = text;
            Marked = false;
        }

        public void SetText(string text)
        {
            ThrowIfBusy();
            Writes++;
            Text = text;
            Marked = true;
        }

        public bool ClearIfHolds(string text)
        {
            ThrowIfBusy();
            Reads++;
            if (!string.Equals(Text, text, StringComparison.Ordinal))
            {
                return false;
            }

            Clears++;
            Text = null;
            Marked = false;
            return true;
        }

        private void ThrowIfBusy()
        {
            if (FailNext > 0)
            {
                FailNext--;
                FailedCalls++;
                throw new ClipboardException("Could not open the clipboard (Windows error 5).");
            }
        }
    }
}
