using System;
using System.Collections.Generic;
using Multiplayer.Client.Patches;

namespace Multiplayer.Client.Compatibility;

// Local-only presentation scopes for compat mods: UI work that must stay off the shared RNG stream,
// unique id counters and desync traces.
public static class Presentation
{
    private struct Entry
    {
        public LocalOnlyScope Scope;
        public long Token;
    }

    public const int Version = 2;

    [ThreadStatic] private static List<Entry> entries;
    [ThreadStatic] private static long nextToken;

    public static IDisposable EnterLocalOnly() => LocalOnlyScope.Enter();

    public static bool BeginLocalOnly(out long token)
    {
        var list = entries ??= new List<Entry>();

        token = ++nextToken;
        if (token == 0) token = ++nextToken;

        list.Add(new Entry { Scope = LocalOnlyScope.Enter(), Token = token });
        return true;
    }

    public static void EndLocalOnly(long token)
    {
        var list = entries;
        if (token == 0 || list == null || list.Count == 0) return;

        var last = list[list.Count - 1];
        if (last.Token != token)
        {
            foreach (var entry in list)
                if (entry.Token == token)
                    throw new InvalidOperationException("Local-only scopes must end in LIFO order");
            return;
        }

        list.RemoveAt(list.Count - 1);
        last.Scope.Dispose();
    }

    public static void RunLocal(Action presentation)
    {
        if (presentation == null) throw new ArgumentNullException(nameof(presentation));

        using (LocalOnlyScope.Enter())
            presentation();
    }
}
