using System.Net.Sockets;
using System.Text;

namespace NotificationSystem.Services;

public enum IdempotencyClaimResult { Acquired, AlreadyCompleted, InProgress }

// Minimal RESP client for the atomic Redis operations this worker needs.
public sealed class NotificationIdempotencyStore(IConfiguration configuration)
{
    private readonly string _connectionString = configuration["Redis:ConnectionString"] ?? "localhost:6379";
    private readonly int _leaseSeconds = configuration.GetValue<int?>("Redis:ProcessingLeaseSeconds") ?? 300;
    private readonly int _retentionSeconds = (configuration.GetValue<int?>("Redis:CompletedKeyRetentionDays") ?? 30) * 86400;

    public async Task<IdempotencyClaimResult> TryClaimAsync(string notificationId, string owner)
    {
        var key = $"notifications:idempotency:{notificationId}";
        var response = await ExecuteAsync("SET", key, $"processing:{owner}", "NX", "EX", _leaseSeconds.ToString());
        if (response == "+OK") return IdempotencyClaimResult.Acquired;
        var current = await ExecuteAsync("GET", key);
        return current == "$9\r\ncompleted\r\n" ? IdempotencyClaimResult.AlreadyCompleted : IdempotencyClaimResult.InProgress;
    }

    public async Task<bool> CompleteAsync(string notificationId, string owner)
    {
        const string script = "if redis.call('GET',KEYS[1]) == ARGV[1] then redis.call('SET',KEYS[1],'completed','EX',ARGV[2]); return 1 end return 0";
        return await ExecuteAsync("EVAL", script, "1", Key(notificationId), $"processing:{owner}", _retentionSeconds.ToString()) == ":1\r\n";
    }

    public async Task<bool> ReleaseAsync(string notificationId, string owner)
    {
        const string script = "if redis.call('GET',KEYS[1]) == ARGV[1] then return redis.call('DEL',KEYS[1]) end return 0";
        return await ExecuteAsync("EVAL", script, "1", Key(notificationId), $"processing:{owner}") == ":1\r\n";
    }

    private static string Key(string id) => $"notifications:idempotency:{id}";

    private async Task<string> ExecuteAsync(params string[] args)
    {
        var parts = _connectionString.Split(',', ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var hostPort = parts[0].Split(':');
        var host = hostPort[0];
        var port = int.TryParse(hostPort.ElementAtOrDefault(1), out var parsedPort) ? parsedPort : 6379;
        var password = parts.FirstOrDefault(part => part.StartsWith("password=", StringComparison.OrdinalIgnoreCase))?[9..];
        using var client = new TcpClient();
        await client.ConnectAsync(host, port);
        await using var stream = client.GetStream();
        if (!string.IsNullOrWhiteSpace(password))
        {
            await WriteCommandAsync(stream, "AUTH", password);
            await ReadResponseAsync(stream);
        }
        await WriteCommandAsync(stream, args);
        return await ReadResponseAsync(stream);
    }

    private static async Task WriteCommandAsync(NetworkStream stream, params string[] args)
    {
        var builder = new StringBuilder().Append('*').Append(args.Length).Append("\r\n");
        foreach (var arg in args) builder.Append('$').Append(Encoding.UTF8.GetByteCount(arg)).Append("\r\n").Append(arg).Append("\r\n");
        var bytes = Encoding.UTF8.GetBytes(builder.ToString());
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
    }

    private static async Task<string> ReadResponseAsync(NetworkStream stream)
    {
        var bytes = new List<byte>();
        var buffer = new byte[1];
        while (await stream.ReadAsync(buffer) == 1)
        {
            bytes.Add(buffer[0]);
            if (bytes.Count >= 2 && bytes[^2] == '\r' && bytes[^1] == '\n') break;
        }
        var response = Encoding.UTF8.GetString(bytes.ToArray());
        if (response.StartsWith('$') && response != "$-1\r\n")
        {
            var length = int.Parse(response.AsSpan(1, response.Length - 3));
            var payload = new byte[length + 2];
            var offset = 0;
            while (offset < payload.Length) offset += await stream.ReadAsync(payload.AsMemory(offset));
            return response + Encoding.UTF8.GetString(payload);
        }
        return response;
    }
}
