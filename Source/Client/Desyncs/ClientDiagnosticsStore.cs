using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Multiplayer.Common.Networking.Packet;

namespace Multiplayer.Client.Desyncs;

/// <summary>
/// Host-side storage for client diagnostics. One append-only JSONL file per client per session under
/// MpDesyncs/ClientLogs. Old files are rotated out so a long-running host does not fill the disk.
/// </summary>
internal static class ClientDiagnosticsStore
{
    private const int MaxFiles = 20;

    private static readonly Dictionary<int, StreamWriter> writers = new();
    private static string directory;

    internal static void Append(ServerDiagnosticsPacket packet)
    {
        try
        {
            var batch = JsonSerializer.Deserialize<ClientDiagnosticsBatch>(packet.payload);
            if (batch == null) return;

            var username = Multiplayer.session?.GetPlayerInfo(packet.playerId)?.username ?? "unknown";
            var line = new ClientDiagnosticsLine(
                packet.playerId,
                username,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                batch);

            var writer = WriterFor(packet.playerId, username);
            writer.WriteLine(JsonSerializer.Serialize(line));
            writer.Flush();
        }
        catch
        {
            // A malformed or interrupted batch must not affect the host
        }
    }

    private static StreamWriter WriterFor(int playerId, string username)
    {
        if (writers.TryGetValue(playerId, out var writer)) return writer;

        directory ??= Path.Combine(Multiplayer.DesyncsDir, "ClientLogs");
        Directory.CreateDirectory(directory);

        Rotate();

        var name = $"client-{playerId}-{Sanitize(username)}-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl";
        var stream = new FileStream(
            Path.Combine(directory, name), FileMode.Append, FileAccess.Write, FileShare.Read);
        writer = new StreamWriter(stream, new UTF8Encoding(false));

        writers[playerId] = writer;
        return writer;
    }

    private static void Rotate()
    {
        var files = new DirectoryInfo(directory)
            .GetFiles("*.jsonl")
            .OrderBy(f => f.LastWriteTimeUtc)
            .ToList();

        while (files.Count >= MaxFiles)
        {
            try
            {
                files[0].Delete();
            }
            catch
            {
                // Keep going; a locked file just means one extra file this session
            }

            files.RemoveAt(0);
        }
    }

    private static string Sanitize(string username)
    {
        if (string.IsNullOrEmpty(username)) return "unknown";

        var builder = new StringBuilder(Math.Min(username.Length, 24));
        foreach (var c in username)
        {
            if (builder.Length >= 24) break;
            if (char.IsLetterOrDigit(c) || c is '-' or '_') builder.Append(c);
        }

        return builder.Length == 0 ? "unknown" : builder.ToString();
    }

    internal static void Reset()
    {
        foreach (var writer in writers.Values)
        {
            try
            {
                writer.Dispose();
            }
            catch
            {
                // Ignore; the process is leaving the session anyway
            }
        }

        writers.Clear();
        directory = null;
    }
}
