// Test stand-in for bw.exe. Tests never run the real bw (ADR 13).
//
// FAKEBW_SCENARIO names a JSON file:
//   { "recordings": "<folder>", "log": "<file>",
//     "rules": [ { "args": [...], "recording": "<name>" | "response": { exitCode, stdout, stderr },
//                  "delayMs": 0 } ] }
// The first rule whose arguments match exactly answers with that recording or response.
// Every call is appended to the log with its arguments, its BW_SESSION and the value of the
// variable named by --passwordenv, and BWCP_PW, so tests can check what reached the child.
using System.Text;
using System.Text.Json.Nodes;

Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

var scenarioPath = Environment.GetEnvironmentVariable("FAKEBW_SCENARIO");
if (string.IsNullOrEmpty(scenarioPath))
{
    Console.Error.WriteLine("fake bw: FAKEBW_SCENARIO is not set");
    return 98;
}

var scenario = JsonNode.Parse(File.ReadAllText(scenarioPath))!;
var passwordIndex = Array.IndexOf(args, "--passwordenv");
var passwordVariable = passwordIndex >= 0 && passwordIndex + 1 < args.Length ? args[passwordIndex + 1] : null;
var call = new JsonObject
{
    ["args"] = new JsonArray([.. args.Select(argument => JsonValue.Create(argument))]),
    ["session"] = Environment.GetEnvironmentVariable("BW_SESSION"),
    ["passwordVariable"] = passwordVariable,
    ["password"] = passwordVariable is null ? null : Environment.GetEnvironmentVariable(passwordVariable),

    // Logged for every call, so a test can see an inherited value reach a child that never asked.
    ["bwcpPw"] = Environment.GetEnvironmentVariable("BWCP_PW"),
};
File.AppendAllText((string)scenario["log"]!, call.ToJsonString() + "\n");

foreach (var rule in scenario["rules"]!.AsArray())
{
    var ruleArgs = rule!["args"]!.AsArray().Select(node => (string)node!);
    if (!ruleArgs.SequenceEqual(args))
    {
        continue;
    }

    if (rule["delayMs"] is { } delay)
    {
        Thread.Sleep((int)delay);
    }

    var response = rule["recording"] is { } recording
        ? JsonNode.Parse(File.ReadAllText(Path.Combine((string)scenario["recordings"]!, (string)recording! + ".json")))!
        : rule["response"]!;
    Console.Out.Write((string?)response["stdout"] ?? string.Empty);
    Console.Error.Write((string?)response["stderr"] ?? string.Empty);
    return (int)response["exitCode"]!;
}

Console.Error.WriteLine("fake bw: no rule matches this call");
return 99;
