using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace OpenAITunnelManager.FakeTunnelClient;

public static class Marker
{
}

public static class Program
{
    private static readonly JsonSerializerOptions CompactJson = new() { WriteIndented = false };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            return await ExecuteAsync(args);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    private static async Task<int> ExecuteAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("missing command");
            return 1;
        }

        if (args.Length == 1 && args[0] == "--version")
        {
            Console.WriteLine("v0.0.14-fake");
            return 0;
        }

        if (args.Length >= 2 && args[1] == "--help" && args[0] is "profiles" or "runtimes" or "doctor")
        {
            Console.WriteLine($"fake {args[0]} help");
            return 0;
        }

        if (args[0] == "profiles") return ExecuteProfiles(args);
        if (args[0] == "runtimes") return ExecuteRuntimes(args);
        if (args[0] == "init") return ExecuteInit(args);
        if (args[0] == "doctor") return ExecuteDoctor(args);
        if (args[0] == "run") return await ExecuteRunAsync(args);

        Console.Error.WriteLine($"unsupported command: {string.Join(' ', args)}");
        return 1;
    }

    private static int ExecuteProfiles(string[] args)
    {
        if (args.Length >= 3 && args[1] == "list" && args.Contains("--json", StringComparer.Ordinal))
        {
            Directory.CreateDirectory(ProfileDirectory);
            var entries = Directory.EnumerateFiles(ProfileDirectory)
                .Where(static path => new[] { ".yaml", ".yml", ".json" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => new JsonObject
                {
                    ["name"] = Path.GetFileNameWithoutExtension(path),
                    ["path"] = Path.GetFullPath(path)
                });
            var array = new JsonArray(entries.Select(static item => (JsonNode)item).ToArray());
            Console.WriteLine(array.ToJsonString(CompactJson));
            return 0;
        }

        if (args.Length >= 2 && args[1] == "add")
        {
            var name = PositionalAfter(args, "add");
            var source = Option(args, "--from-file");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(source) || !File.Exists(source))
            {
                Console.Error.WriteLine("invalid profiles add request");
                return 2;
            }

            var text = File.ReadAllText(source);
            if (!Regex.IsMatch(text, @"tunnel_[0-9a-f]{32}", RegexOptions.CultureInvariant) ||
                !(text.Contains("url:", StringComparison.Ordinal) || text.Contains("command:", StringComparison.Ordinal) || text.Contains("\"url\"", StringComparison.Ordinal) || text.Contains("\"command\"", StringComparison.Ordinal)))
            {
                Console.Error.WriteLine("profile validation failed");
                return 3;
            }

            Directory.CreateDirectory(ProfileDirectory);
            var destination = Path.Combine(ProfileDirectory, name + ".yaml");
            File.WriteAllText(destination, text);
            Console.WriteLine(destination);
            return 0;
        }

        Console.Error.WriteLine("unsupported profiles command");
        return 1;
    }

    private static int ExecuteInit(string[] args)
    {
        var name = Option(args, "--profile");
        var tunnelId = Option(args, "--tunnel-id");
        var keyRef = Option(args, "--control-plane-api-key-ref", "env:CONTROL_PLANE_API_KEY");
        var url = Option(args, "--mcp-server-url");
        var command = Option(args, "--mcp-command");
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(tunnelId) || (string.IsNullOrWhiteSpace(url) && string.IsNullOrWhiteSpace(command)))
        {
            Console.Error.WriteLine("invalid init request");
            return 2;
        }

        Directory.CreateDirectory(ProfileDirectory);
        var path = Path.Combine(ProfileDirectory, name + ".yaml");
        var targetSection = !string.IsNullOrWhiteSpace(url)
            ? $"  server_urls:\n    - channel: main\n      url: {JsonSerializer.Serialize(url)}\n"
            : $"  commands:\n    - channel: main\n      command: {JsonSerializer.Serialize(command)}\n";
        var yaml = $"config_version: 1\ncontrol_plane:\n  tunnel_id: {JsonSerializer.Serialize(tunnelId)}\n  api_key: {JsonSerializer.Serialize(keyRef)}\nmcp:\n{targetSection}";
        File.WriteAllText(path, yaml);
        Console.WriteLine(path);
        return 0;
    }

    private static int ExecuteRuntimes(string[] args)
    {
        if (args.Length >= 2 && args[1] == "list")
        {
            var state = LoadState();
            var runtimes = EnsureRuntimes(state);
            var aliases = new JsonArray();
            foreach (var pair in runtimes.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (pair.Value is JsonObject runtime) aliases.Add(runtime.DeepClone());
            }
            Console.WriteLine(new JsonObject { ["aliases"] = aliases }.ToJsonString(CompactJson));
            return 0;
        }

        if (args.Length >= 2 && args[1] == "connect")
        {
            var alias = Option(args, "--alias");
            var tunnelId = Option(args, "--tunnel-id");
            var profileName = Option(args, "--profile");
            var profileDir = Option(args, "--profile-dir");
            var url = Option(args, "--mcp-server-url");
            var command = Option(args, "--mcp-command");
            if (string.IsNullOrWhiteSpace(alias) || string.IsNullOrWhiteSpace(tunnelId) || string.IsNullOrWhiteSpace(profileName) || (string.IsNullOrWhiteSpace(url) && string.IsNullOrWhiteSpace(command)))
            {
                Console.Error.WriteLine("invalid runtimes connect request");
                return 2;
            }

            var state = LoadState();
            var runtimes = EnsureRuntimes(state);
            var previous = runtimes[alias] as JsonObject;
            var connectCount = previous?["connect_count"]?.GetValue<int>() ?? 0;
            var profilePath = ResolveRuntimeProfile(profileName, profileDir, tunnelId, url, command);
            Directory.CreateDirectory(RuntimeDirectory);
            var logPath = Path.Combine(RuntimeDirectory, alias + ".log");
            File.AppendAllText(logPath, "INFO runtime connected\n");
            var targetKind = !string.IsNullOrWhiteSpace(url) ? "server_url" : "command";
            var targetValue = !string.IsNullOrWhiteSpace(url) ? url : command;
            var runtime = new JsonObject
            {
                ["alias"] = alias,
                ["tunnel_id"] = tunnelId,
                ["profile_name"] = profileName,
                ["profile_path"] = profilePath,
                ["config_path"] = profilePath,
                ["process_running"] = true,
                ["healthy"] = true,
                ["ready"] = true,
                ["runtime_state"] = "ready",
                ["health_url"] = "http://127.0.0.1:54321/healthz",
                ["health_details_url"] = "http://127.0.0.1:54321/details",
                ["mcp_health_url"] = "http://127.0.0.1:54321/mcp",
                ["log_path"] = logPath,
                ["pid"] = 4242,
                ["connect_count"] = connectCount + 1,
                ["process"] = new JsonObject { ["target_kind"] = targetKind, ["target_value"] = targetValue }
            };
            runtimes[alias] = runtime;
            SaveState(state);
            Console.WriteLine(new JsonObject { ["ready"] = true }.ToJsonString(CompactJson));
            return 0;
        }

        if (args.Length >= 3 && args[1] == "status")
        {
            var alias = args[2];
            if (string.Equals(Environment.GetEnvironmentVariable("FAKE_STALE_STATUS"), alias, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine(new JsonObject
                {
                    ["alias"] = alias,
                    ["stale"] = true,
                    ["runtime_state"] = "stale",
                    ["process_running"] = false,
                    ["error"] = "remote tunnel not found"
                }.ToJsonString(CompactJson));
                return 4;
            }
            var runtime = Runtime(alias);
            if (runtime is null)
            {
                Console.Error.WriteLine("unknown alias");
                return 3;
            }
            Console.WriteLine(runtime.ToJsonString(CompactJson));
            return 0;
        }

        if (args.Length >= 3 && args[1] == "stop")
        {
            var alias = args[2];
            if (string.Equals(Environment.GetEnvironmentVariable("FAKE_FAIL_STOP"), alias, StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("stop failed");
                return 9;
            }
            var state = LoadState();
            var runtime = EnsureRuntimes(state)[alias] as JsonObject;
            if (runtime is null)
            {
                Console.Error.WriteLine("unknown alias");
                return 3;
            }
            runtime["process_running"] = false;
            runtime["healthy"] = false;
            runtime["ready"] = false;
            runtime["runtime_state"] = "stopped";
            SaveState(state);
            Console.WriteLine(new JsonObject { ["stopped"] = alias }.ToJsonString(CompactJson));
            return 0;
        }

        if (args.Length >= 3 && args[1] == "rm")
        {
            var alias = args[2];
            var state = LoadState();
            EnsureRuntimes(state).Remove(alias);
            SaveState(state);
            Console.WriteLine(new JsonObject { ["removed"] = alias }.ToJsonString(CompactJson));
            return 0;
        }

        Console.Error.WriteLine("unsupported runtimes command");
        return 1;
    }

    private static int ExecuteDoctor(string[] args)
    {
        if (!args.Contains("--explain", StringComparer.Ordinal))
        {
            Console.Error.WriteLine("doctor requires --explain");
            return 2;
        }
        Console.WriteLine("Doctor passed");
        return 0;
    }

    private static async Task<int> ExecuteRunAsync(string[] args)
    {
        var profile = Option(args, "--profile");
        var healthFile = Option(args, "--health.url-file");
        if (string.IsNullOrWhiteSpace(profile) || string.IsNullOrWhiteSpace(healthFile))
        {
            Console.Error.WriteLine("invalid run request");
            return 2;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(healthFile))!);
        await File.WriteAllTextAsync(healthFile, "http://127.0.0.1:1/healthz");
        Console.WriteLine("INFO foreground profile started");
        Console.Out.Flush();
        await Task.Delay(Timeout.InfiniteTimeSpan);
        return 0;
    }

    private static string ResolveRuntimeProfile(string profileName, string profileDir, string tunnelId, string url, string command)
    {
        if (!string.IsNullOrWhiteSpace(profileDir))
        {
            foreach (var extension in new[] { ".yaml", ".yml", ".json" })
            {
                var candidate = Path.Combine(profileDir, profileName + extension);
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
        }
        foreach (var extension in new[] { ".yaml", ".yml", ".json" })
        {
            var candidate = Path.Combine(ProfileDirectory, profileName + extension);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        Directory.CreateDirectory(RuntimeDirectory);
        var generated = Path.Combine(RuntimeDirectory, profileName + ".yaml");
        var target = !string.IsNullOrWhiteSpace(url)
            ? $"  server_urls:\n    - channel: main\n      url: {JsonSerializer.Serialize(url)}\n"
            : $"  commands:\n    - channel: main\n      command: {JsonSerializer.Serialize(command)}\n";
        File.WriteAllText(generated, $"config_version: 1\ncontrol_plane:\n  tunnel_id: {JsonSerializer.Serialize(tunnelId)}\n  api_key: \"env:CONTROL_PLANE_API_KEY\"\nmcp:\n{target}");
        return Path.GetFullPath(generated);
    }

    private static JsonObject? Runtime(string alias)
    {
        var state = LoadState();
        return EnsureRuntimes(state)[alias] as JsonObject;
    }

    private static JsonObject LoadState()
    {
        var file = StateFile;
        if (!File.Exists(file)) return new JsonObject { ["runtimes"] = new JsonObject() };
        return JsonNode.Parse(File.ReadAllText(file)) as JsonObject ?? new JsonObject { ["runtimes"] = new JsonObject() };
    }

    private static void SaveState(JsonObject state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StateFile)!);
        File.WriteAllText(StateFile, state.ToJsonString(CompactJson));
    }

    private static JsonObject EnsureRuntimes(JsonObject state)
    {
        if (state["runtimes"] is JsonObject runtimes) return runtimes;
        runtimes = new JsonObject();
        state["runtimes"] = runtimes;
        return runtimes;
    }

    private static string Option(string[] args, string name, string fallback = "")
    {
        var index = Array.FindIndex(args, item => string.Equals(item, name, StringComparison.Ordinal));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
    }

    private static string PositionalAfter(string[] args, string value)
    {
        var index = Array.FindIndex(args, item => string.Equals(item, value, StringComparison.Ordinal));
        return index >= 0 && index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal) ? args[index + 1] : string.Empty;
    }

    private static string ProfileDirectory => FullEnvironmentPath("FAKE_PROFILE_DIR", Path.Combine(Path.GetTempPath(), "OpenAITunnelManager.Fake", "profiles"));
    private static string RuntimeDirectory => FullEnvironmentPath("FAKE_RUNTIME_DIR", Path.Combine(Path.GetTempPath(), "OpenAITunnelManager.Fake", "runtime"));
    private static string StateFile => FullEnvironmentPath("FAKE_TUNNEL_STATE", Path.Combine(Path.GetTempPath(), "OpenAITunnelManager.Fake", "state.json"));

    private static string FullEnvironmentPath(string name, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return Path.GetFullPath(string.IsNullOrWhiteSpace(value) ? fallback : value);
    }
}
