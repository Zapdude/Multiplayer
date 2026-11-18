using System;
using System.Collections.Generic;
using System.Text.Json;
using HarmonyLib;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using UnityEngine;
using Verse;

namespace Multiplayer.Client.Desyncs;

public sealed record ClientDiagnosticsEntry(
    string Kind, string Message, string Stack, int Tick, int Map, int Count, long FirstUtc, long LastUtc);

public sealed record ClientDiagnosticsBatch(
    int Version, string Epoch, long Sequence, int Tick, ClientDiagnosticsEntry[] Entries);

public sealed record ClientDiagnosticsLine(
    int PlayerId, string Username, long ReceivedUtc, ClientDiagnosticsBatch Batch);

/// <summary>
/// Captures a client's own errors and warnings and sends them to the host in small batches. Only active when the
/// host opted in (<see cref="Comp.MultiplayerGameComp.receiveClientDiagnostics"/>) and never on the host itself.
/// </summary>
public static class ClientDiagnostics
{
    private sealed class Pending
    {
        public string Kind;
        public string Message;
        public string Stack;
        public int Tick;
        public int Map;
        public int Count;
        public long FirstUtc;
        public long LastUtc;
    }

    private const int FlushIntervalSeconds = 5;
    private const int MaxEntries = 200;

    private static readonly List<Pending> order = new();
    private static readonly Dictionary<string, Pending> byKey = new(StringComparer.Ordinal);
    private static readonly object sync = new();

    private static string epoch = Guid.NewGuid().ToString("N");
    private static long nextSequence;
    private static float lastFlush;
    private static MultiplayerSession session;

    [ThreadStatic] private static bool inCapture;

    private static bool Enabled
    {
        get
        {
            if (Multiplayer.Client == null || Multiplayer.session == null) return false;
            if (Multiplayer.IsReplay) return false;
            if (Multiplayer.game?.gameComp?.receiveClientDiagnostics != true) return false;

            // The host already has its own log; it only receives other players' batches
            return Multiplayer.session.playerId != 0;
        }
    }

    internal static void Capture(string kind, string message, bool withStack)
    {
        if (inCapture || !Enabled) return;

        inCapture = true;
        try
        {
            if (string.IsNullOrEmpty(message)) return;

            var stack = withStack ? Trim(Environment.StackTrace, ClientDiagnosticsLimits.MaxStackLength) : null;
            var text = Trim(message, ClientDiagnosticsLimits.MaxMessageLength);
            var key = kind + "\n" + text + "\n" + (stack ?? "");

            var inMainThread = UnityData.IsInMainThread;
            var tick = inMainThread ? Find.TickManager?.TicksGame ?? -1 : -1;
            var map = inMainThread ? Find.CurrentMap?.uniqueID ?? -1 : -1;
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            lock (sync)
            {
                if (byKey.TryGetValue(key, out var existing))
                {
                    existing.Count++;
                    existing.LastUtc = now;
                    existing.Tick = tick;
                    existing.Map = map;
                    return;
                }

                var entry = new Pending
                {
                    Kind = kind,
                    Message = text,
                    Stack = stack,
                    Tick = tick,
                    Map = map,
                    Count = 1,
                    FirstUtc = now,
                    LastUtc = now
                };

                order.Add(entry);
                byKey[key] = entry;

                while (order.Count > MaxEntries)
                {
                    var evicted = order[0];
                    order.RemoveAt(0);
                    byKey.Remove(evicted.Kind + "\n" + evicted.Message + "\n" + (evicted.Stack ?? ""));
                }
            }
        }
        catch
        {
            // Diagnostics must never disturb the game
        }
        finally
        {
            inCapture = false;
        }
    }

    internal static void Tick()
    {
        try
        {
            if (!Enabled)
            {
                if (order.Count > 0) Reset();
                return;
            }

            if (session != Multiplayer.session) Reset();

            if (order.Count == 0) return;

            var now = Time.realtimeSinceStartup;
            if (now - lastFlush < FlushIntervalSeconds) return;
            lastFlush = now;

            SendBatch();
        }
        catch
        {
            // Never break ConstantTicker
        }
    }

    private static void SendBatch()
    {
        List<Pending> sending;

        lock (sync)
        {
            if (order.Count == 0) return;

            var count = Math.Min(ClientDiagnosticsLimits.MaxBatchEntries, order.Count);
            sending = order.GetRange(0, count);
        }

        var entries = new ClientDiagnosticsEntry[sending.Count];
        for (var i = 0; i < sending.Count; i++)
        {
            var entry = sending[i];
            entries[i] = new ClientDiagnosticsEntry(
                entry.Kind, entry.Message, entry.Stack, entry.Tick, entry.Map, entry.Count, entry.FirstUtc, entry.LastUtc);
        }

        var batch = new ClientDiagnosticsBatch(
            1, epoch, nextSequence, Find.TickManager?.TicksGame ?? -1, entries);

        var payload = JsonSerializer.SerializeToUtf8Bytes(batch);
        if (payload.Length > ClientDiagnosticsLimits.MaxPayload) return;

        var connection = Multiplayer.Client;
        if (connection == null) return;

        connection.Send(new ClientDiagnosticsPacket(nextSequence, payload));
        nextSequence++;

        lock (sync)
        {
            foreach (var entry in sending)
            {
                order.Remove(entry);
                byKey.Remove(entry.Kind + "\n" + entry.Message + "\n" + (entry.Stack ?? ""));
            }
        }
    }

    internal static void Reset()
    {
        lock (sync)
        {
            order.Clear();
            byKey.Clear();
        }

        epoch = Guid.NewGuid().ToString("N");
        nextSequence = 0;
        lastFlush = 0;
        session = Multiplayer.session;
    }

    private static string Trim(string value, int max)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= max ? value : value.Substring(0, max);
    }
}

[HarmonyPatch(typeof(Log), nameof(Log.Error))]
internal static class CaptureLogError
{
    static void Postfix(string text) => ClientDiagnostics.Capture("Error", text, withStack: true);
}

[HarmonyPatch(typeof(Log), nameof(Log.Warning))]
internal static class CaptureLogWarning
{
    static void Postfix(string text) => ClientDiagnostics.Capture("Warning", text, withStack: false);
}

[HarmonyPatch(typeof(Log), nameof(Log.ErrorOnce))]
internal static class CaptureLogErrorOnce
{
    static void Postfix(string text, int key) => ClientDiagnostics.Capture("ErrorOnce", text, withStack: true);
}

[HarmonyPatch(typeof(Log), nameof(Log.WarningOnce))]
internal static class CaptureLogWarningOnce
{
    static void Postfix(string text, int key) => ClientDiagnostics.Capture("WarningOnce", text, withStack: false);
}
