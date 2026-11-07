using System.Text.Json.Nodes;
using BitwardenCommandPalette.Core.Bw;

namespace BitwardenCommandPalette.Core.Tests;

public sealed class VaultIndexTests
{
    private readonly VaultIndex index = new();

    [Fact]
    public void NothingIsFoundWhileNothingIsLoaded()
    {
        Assert.False(index.IsLoaded);
        Assert.Equal(0, index.Count);
        Assert.Empty(index.Search("anything"));
        Assert.Null(index.Find(Id(1)));
    }

    [Fact]
    public void AnEmptyQueryListsEveryLoginByName()
    {
        index.Load([Login(1, "zeta"), Login(2, "Alpha"), Login(3, "beta")]);

        Assert.Equal(["Alpha", "beta", "zeta"], Names(index.Search(null)));
        Assert.Equal(["Alpha", "beta", "zeta"], Names(index.Search("   ")));
    }

    [Fact]
    public void TheNameRanksAboveTheWebsiteAboveTheUsername()
    {
        index.Load(
        [
            Login(1, "Personal", username: "github-user"),
            Login(2, "Code hosting", uris: ["https://github.com/login"]),
            Login(3, "GitHub"),
        ]);

        Assert.Equal(["GitHub", "Code hosting", "Personal"], Names(index.Search("github")));
    }

    [Fact]
    public void AStartBeatsAWordStartBeatsTheMiddle()
    {
        index.Load([Login(1, "Mybank"), Login(2, "My bank"), Login(3, "Bank of us"), Login(4, "Bank")]);

        Assert.Equal(["Bank", "Bank of us", "My bank", "Mybank"], Names(index.Search("bank")));
    }

    [Fact]
    public void EveryWordMustMatchSomewhere()
    {
        index.Load(
        [
            Login(1, "Work mail", username: "me@work.example"),
            Login(2, "Home mail", username: "me@home.example"),
            Login(3, "Work chat"),
        ]);

        Assert.Equal(["Work mail"], Names(index.Search("mail work")));
        Assert.Equal(["Home mail"], Names(index.Search("home me@")));
        Assert.Empty(index.Search("work nothing"));
    }

    [Theory]
    [InlineData("cafe")]
    [InlineData("CAFÉ")]
    [InlineData("ｃａｆｅ")]
    public void CaseAccentsAndWidthAreIgnored(string query)
    {
        index.Load([Login(1, "Café"), Login(2, "Tea")]);

        Assert.Equal(["Café"], Names(index.Search(query)));
    }

    [Fact]
    public void WebsitesAreMatchedByHostWithoutWww()
    {
        index.Load(
        [
            Login(1, "A", uris: ["https://www.example.org/path?q=secret"]),
            Login(2, "B", uris: ["androidapp://com.example.app"]),
            Login(3, "C", uris: ["not a uri"]),
        ]);

        Assert.Equal(["A"], Names(index.Search("example.org")));
        Assert.Empty(index.Search("www"));
        Assert.Empty(index.Search("path"));
        Assert.Equal(["B"], Names(index.Search("com.example")));
        Assert.Equal(["C"], Names(index.Search("uri")));
    }

    [Fact]
    public void AnyNameMatchBeatsAnExactWebsiteOrUsername()
    {
        index.Load(
        [
            Login(1, "Personal", username: "github"),
            Login(2, "Code", uris: ["https://github"]),
            Login(3, "MyGithub"),
        ]);

        Assert.Equal(["MyGithub", "Code", "Personal"], Names(index.Search("github")));
    }

    [Fact]
    public void AUriWithoutASchemeIsReducedToItsHost()
    {
        index.Load(
        [
            Login(1, "A", uris: ["example.com/reset?token=abc"]),
            Login(2, "B", uris: ["login.example.net:8443/app"]),
        ]);

        Assert.Empty(index.Search("reset"));
        Assert.Empty(index.Search("token"));
        Assert.Empty(index.Search("app"));
        Assert.Equal(["A"], Names(index.Search("example.com")));
        Assert.Equal(["B"], Names(index.Search("login.example")));
    }

    [Fact]
    public void ACombiningMarkOrEmojiIsNotAWordBoundary()
    {
        index.Load([Login(1, "Caféclub"), Login(2, "Big club"), Login(3, "\U0001F600club")]);

        // The accented one matches in the middle of a word, so it comes last. The emoji is
        // decoded as one character, not as two halves, and as it is not a letter it starts a
        // word, tying with "Big club".
        var names = Names(index.Search("club"));
        Assert.Equal("Caféclub", names[^1]);
        Assert.Equal(["Big club", "\U0001F600club"], names[..2].Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("́", new[] { "Bank", "Mail" })]
    [InlineData("‍", new[] { "Bank", "Mail" })]
    [InlineData("bank ­", new[] { "Bank" })]
    public void AWordOfOnlyIgnorableCharactersIsDropped(string query, string[] expected)
    {
        index.Load([Login(1, "Bank"), Login(2, "Mail")]);

        Assert.Equal(expected, Names(index.Search(query)));
    }

    [Fact]
    public void TheWholePhraseInTheNameComesFirst()
    {
        index.Load([Login(1, "Bank my stuff"), Login(2, "My bank"), Login(3, "My bank savings")]);

        Assert.Equal(["My bank", "My bank savings", "Bank my stuff"], Names(index.Search("my bank")));
    }

    [Fact]
    public void NoNameOrUsernameIsEverPrinted()
    {
        const string name = "canary-item-name-77d1";
        const string username = "canary-username-2b90";
        index.Load([new BwLogin(Id(1), name, username, ["https://canary.example"], null, HasTotp: true, RequiresReprompt: false)]);

        string[] printed = [index.ToString()!, index.Find(Id(1))!.ToString(), .. index.Search("canary").Select(login => login.ToString())];

        Assert.DoesNotContain(printed, text => text.Contains(name, StringComparison.Ordinal) || text.Contains(username, StringComparison.Ordinal));
    }

    [Fact]
    public void TheLimitIsRespectedAndValidated()
    {
        index.Load(Enumerable.Range(1, 150).Select(number => Login(number, $"login {number:000}")));

        Assert.Equal(VaultIndex.DefaultLimit, index.Search("login").Count);
        Assert.Equal(5, index.Search("login", limit: 5).Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => index.Search("login", limit: 0));
    }

    [Fact]
    public void EqualScoresAreOrderedByNameThenId()
    {
        index.Load([Login(2, "Same"), Login(1, "Same"), Login(3, "same")]);

        Assert.Equal([Id(1), Id(2), Id(3)], index.Search("same").Select(login => login.Id));
    }

    [Fact]
    public void FindLooksUpById()
    {
        index.Load([Login(1, "One"), Login(2, "Two")]);

        Assert.Equal("Two", index.Find(Id(2))!.Name);
        Assert.Equal("Two", index.Find(Id(2).ToUpperInvariant())!.Name);
        Assert.Null(index.Find(Id(3)));
    }

    [Fact]
    public void LoadReplacesAndClearDrops()
    {
        index.Load([Login(1, "Old")]);
        index.Load([Login(2, "New"), Login(3, "Newer")]);

        Assert.Equal(2, index.Count);
        Assert.Null(index.Find(Id(1)));

        index.Clear();

        Assert.False(index.IsLoaded);
        Assert.Empty(index.Search(null));
    }

    [Fact]
    public void TheRecordedFixturesAreSearchable()
    {
        var stdout = (string)JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "recordings", "list-items.json")))!["stdout"]!;
        index.Load(BwOutput.ParseLogins(stdout));

        Assert.Equal(9, index.Count);
        Assert.Equal("BWCP Fixture - Full login", index.Search("full").Single().Name);
        Assert.Equal("BWCP Fixture - Full login", index.Search("login.example").Single().Name);
        Assert.Equal(2, index.Search("duplicate").Count);
        Assert.Contains("名前", index.Search("名前").Single().Name, StringComparison.Ordinal);
        Assert.Contains("Ünïcode", index.Search("unicode").Single().Name, StringComparison.Ordinal);
    }

    [Fact]
    public void ArgumentsAreValidated()
    {
        Assert.Throws<ArgumentNullException>(() => index.Load(null!));
        Assert.Throws<ArgumentNullException>(() => index.Find(null!));
    }

    private static string Id(int number) => $"00000000-0000-4000-8000-{number:d12}";

    private static BwLogin Login(int number, string name, string? username = null, string[]? uris = null) =>
        new(Id(number), name, username, uris ?? [], FolderId: null, HasTotp: false, RequiresReprompt: false);

    private static string[] Names(IEnumerable<BwLogin> logins) => [.. logins.Select(login => login.Name)];
}
