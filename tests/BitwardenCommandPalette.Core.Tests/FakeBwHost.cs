using System.Text.Json.Nodes;
using BitwardenCommandPalette.Core.Bw;

namespace BitwardenCommandPalette.Core.Tests;

/// <summary>
/// Tests that start the fake bw share one process-wide FAKEBW_SCENARIO variable, so they run
/// one at a time.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class FakeBwGroup
{
    public const string Name = "Fake bw";
}

/// <summary>One call the fake bw received.</summary>
internal sealed record FakeBwCall(IReadOnlyList<string> Args, string? Session, string? PasswordVariable, string? Password, string? BwcpPw);

/// <summary>
/// Sets up the fake bw for one test: rules that answer with a recording or a made-up
/// response, and the log of calls. Arguments in rules leave out --nointeraction, which
/// <see cref="BwCli"/> always adds.
/// </summary>
internal sealed class FakeBwHost : IDisposable
{
    private const string ScenarioVariable = "FAKEBW_SCENARIO";

    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("fakebw-");
    private readonly JsonArray rules = [];

    public FakeBwHost()
    {
        Environment.SetEnvironmentVariable(ScenarioVariable, ScenarioFile);
        Save();
    }

    /// <summary>The fake bw.exe next to the test assembly.</summary>
    public static string ExecutablePath => Path.Combine(AppContext.BaseDirectory, "bw.exe");

    /// <summary>The session key in the "unlock" recording.</summary>
    public static string RecordedSessionKey =>
        (string)JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "recordings", "unlock.json")))!["stdout"]!;

    public IReadOnlyList<FakeBwCall> Calls
    {
        get
        {
            var log = Path.Combine(folder.FullName, "calls.jsonl");
            return !File.Exists(log)
                ? []
                : [.. File.ReadAllLines(log).Select(line => JsonNode.Parse(line)!).Select(call => new FakeBwCall(
                    [.. call["args"]!.AsArray().Select(argument => (string)argument!)],
                    (string?)call["session"],
                    (string?)call["passwordVariable"],
                    (string?)call["password"],
                    (string?)call["bwcpPw"]))];
        }
    }

    public string ScenarioFile => Path.Combine(folder.FullName, "scenario.json");

    /// <summary>Answers <paramref name="args"/> with a recording from tests/FakeBw/recordings.</summary>
    public FakeBwHost Reply(string recording, params string[] args)
    {
        rules.Add(new JsonObject { ["args"] = Arguments(args), ["recording"] = recording });
        Save();
        return this;
    }

    /// <summary>Answers <paramref name="args"/> with a made-up response.</summary>
    public FakeBwHost Respond(int exitCode, string stdout, string stderr, string[] args, int delayMs = 0)
    {
        rules.Add(new JsonObject
        {
            ["args"] = Arguments(args),
            ["response"] = new JsonObject { ["exitCode"] = exitCode, ["stdout"] = stdout, ["stderr"] = stderr },
            ["delayMs"] = delayMs,
        });
        Save();
        return this;
    }

    public static BwCli CreateCli(TimeSpan? timeout = null) =>
        new(new BwCliOptions(ExecutablePath) { Timeout = timeout ?? TimeSpan.FromSeconds(30) });

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(ScenarioVariable, null);
        folder.Delete(recursive: true);
    }

    private static JsonArray Arguments(string[] args) => [.. args.Append("--nointeraction").Select(argument => JsonValue.Create(argument))];

    private void Save()
    {
        var scenario = new JsonObject
        {
            ["recordings"] = Path.Combine(AppContext.BaseDirectory, "recordings"),
            ["log"] = Path.Combine(folder.FullName, "calls.jsonl"),
            ["rules"] = rules.DeepClone(),
        };
        File.WriteAllText(ScenarioFile, scenario.ToJsonString());
    }
}
