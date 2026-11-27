namespace OpenAITunnelManager.Infrastructure.Updates;

public static class ManagerUpdateInstaller
{
    // Only overlay files shipped with the release; portable application data and
    // the separately managed tunnel-client must not be replaced by an app update.
    public static void Apply(string root, string payload, string backup)
    {
        var changes = new Stack<(string Destination, string? Original)>();
        var files = Directory.GetFiles(payload, "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var source in files)
            {
                var relative = Path.GetRelativePath(payload, source);
                var first = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
                if (first.Equals("config", StringComparison.OrdinalIgnoreCase) ||
                    first.Equals("logs", StringComparison.OrdinalIgnoreCase) ||
                    first.Equals("state", StringComparison.OrdinalIgnoreCase) ||
                    first.Equals("tunnel-client", StringComparison.OrdinalIgnoreCase))
                    continue;

                var destination = Path.Combine(root, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                string? original = null;
                if (File.Exists(destination))
                {
                    original = Path.Combine(backup, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(original)!);
                    File.Move(destination, original);
                }
                changes.Push((destination, original));
                File.Move(source, destination);
            }
        }
        catch (Exception installError)
        {
            var errors = new List<Exception>();
            while (changes.TryPop(out var change))
            {
                try
                {
                    if (File.Exists(change.Destination)) File.Delete(change.Destination);
                    if (change.Original is not null) File.Move(change.Original, change.Destination);
                }
                catch (Exception error) { errors.Add(error); }
            }
            if (errors.Count > 0)
                throw new AggregateException($"更新失败，部分文件恢复失败；备份位于 {backup}", new[] { installError }.Concat(errors));
            throw;
        }
    }
}
