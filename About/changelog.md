# Multiplayer Bodged changelog

A personal fork of [Multiplayer](https://github.com/rwmt/Multiplayer), built on
`upstream/dev` at `ddfccdb` (0.11.5), with six upstream pull requests integrated
by hand and fixes of its own.

This fork is for personal use. Reports about it should not go to the upstream
team.

**Current fork version:** 0.11.7 (RimWorld 1.6, protocol 59).

## Base

`upstream/dev` at `ddfccdb` — "Update .NET version to 10 (#1008)".

## Integrated pull requests

Six PRs, none of them in a released Multiplayer build.

### #996 — Broad desync fixes (WoodyWizard)

The largest of the six. Covers the join flow, simulation contexts, multifaction
handling and the tick loop, plus smaller bug fixes. It comes from a full review
of the desync-relevant code: command log and join points, server, RNG and
simulation contexts, sync framework, sessions, multifaction, async time and the
tick loop, storyteller and gravship.

### #998 — Fix some cases of Steam connection leak (mibac138)

Fixes a leaked connection that stopped a player from rejoining a game. Steam P2P
doesn't flush remaining packets or send a disconnection message, so a client
that closes its side can leave the server holding a dead connection.

### #1004 — Notify user when dev-mode command is rejected (mibac138)

Minor UX improvement: a rejected dev-mode command now tells the user instead of
failing silently.

### #1009 — Dispose the game before clearing maps on reload paths (mibac138)

`ClearAllMapsAndWorld` drops managed references, but the game holds unmanaged
ones (the path finder, for example) that leak if they aren't cleaned up. The
game is now disposed first.

### #1010 — Add RimRef Local for IDE source navigation (mibac138)

Debug builds use locally installed 1.6.4871 assemblies; Release and CI still use
the NuGet references. The local game version must match exactly.

### #1011 — Fix ritual role assignment desyncs (Kuinox)

Fixes a desync that also caused the wrong pawn to be selected during Ideology
role changes and the Gauranlen tree connection ritual.

## Merge resolutions

The six PRs overlap, so I merged them by hand rather than replaying each branch.
Thirteen conflict hunks across nine files were resolved individually. Where two
PRs touched the same code, I kept the newer design.

- **`NetworkingSteam.cs`** — kept #996's `SteamP2PIntegration` session handling
  (`CloseSession`, `KeepSession`, `CloseSessionAfterFlush`, `CloseDueSessions`).
  Dropped #998's duplicate `SteamClientConn.OnClose` and the orphaned
  `SteamBaseConn.OnClose`, which #996 had already removed.
- **`SteamIntegration.cs`** — kept #996's `P2PSessionRequest_t` handler and
  `RejectPlayerJoinRequest`. Dropped #998's `PendingJoinRequestCallback`, which
  would double-accept, since the window's Accept button already runs it.
- **`MultiplayerServer.cs`** — kept #998's `StartServer` and #996's `TryStop`
  ordering with the stopped latch.
- **`SyncRituals.cs`** — kept #1011's `SetPreInvoke` with #996's body.
- **`JoinData.cs`** — kept `CRC32IgnoringLineEndings()` and the `writeTime`
  argument.

### Test fix

`ReaderBoundsTest.ModListLimitsMatchBetweenInitAndJoinData` was written against
#996's `ModFile`, which had only `path` and `hash`. `dev` added the version
strings and `writeTime`, so the test's literal left them null and the roundtrip
threw. The test fills them in now.

## My fixes

### Sync opinion trace-hash compression

`ClientSyncInfoPacket` carries a `SyncOpinion` whose `traceHashes` list holds one
entry per trace. On a long session that list grows without bound, and the packet
eventually exceeds the writer's element limit. The client then throws
`WriterException: List too big`, which drops the sync opinion and leaves the
session unable to compare state. The list was capped at `1 << 21` (2,097,152)
entries, fine for small lists but it threw errors for me.

The trace hashes now go out as a dictionary of distinct values plus an index list
into it, instead of a flat list.

- `SyncOpinion` gains `traceHashValues` and `traceHashIndices`.
- Indices go out as `ushort` when the distinct count is at most 65,536, with a
  written boolean width flag so the reader knows which width to expect.
- `MaxTraceHashValues` is `1 << 18` (262,144) distinct values.
- `MaxTraceHashIndices` is `1 << 22` (4,194,304) traces, derived from
  `ClientSyncInfoPacket.MaxLength` rather than reusing the old hash-count cap.
- `BinderOf.UShort()` was added to support the narrower index width.

In my testing it halved payload size on average with large modlists.

### Sync opinion encoding on the join path

Forwarded sync opinions in the world data were read with the report format, so a
join while the server held a stored opinion shifted the stream and disconnected
the joiner. They are read with `SyncOpinion` now, and the index width follows the
largest index. Out-of-range indices are rejected instead of cast, and null lists
serialize as empty. Opinions produced by `CompressTraceHashes` keep their
encoding, so the protocol is unchanged.

### Protocol

`MpVersion.Protocol` is 58. This fork can't connect to stock 0.11.5 clients, and
stock clients can't connect to it.

### Tests

`Source/Tests/SyncOpinionCompressionTest.cs` covers the compression, including
`LargeTraceCountSerializes`, which checks that a list of 2,532,642 traces with
1,000 distinct values round-trips correctly. The full suite passes.

## Rejoin and reload fixes

- A desynced or rejoining host is no longer the sync reference or uploader, so a
  desync-triggered join point doesn't wait on a client that can't produce the
  save. World uploads with a missing save, session or map are rejected.
- A failed load rebuilds the command queue and stops the session instead of
  leaving the client recording commands it never applies.
- A failed map entry restores the enclosing map's context, so the rest of the
  world tick still runs with one.
- A nested long event restores the enclosing event's faction on exit instead of
  the local player's.
- The disabled map streaming path is gone; its packet id stays reserved.
- A rejoining client no longer logs an inconsistent-map error on its first
  player count report.

## Fork housekeeping

- Renamed to **Multiplayer Bodged** with package ID `zap.multiplayerbodged`, and
  declared incompatible with `rwmt.Multiplayer` so the Workshop version can't
  load alongside it.
- `SyncConfigs.ignoredConfigsModIds` updated to the new package ID. Both the
  config-sync filter and the settings-path redirect match on
  `PackageIdNonUnique`, so the old entry would have stopped excluding this mod's
  own settings.
- Removed the upstream website and Discord links: the constants, the server
  browser buttons, the chat message posted on every hosted game, the unused
  `ChatMsg_Url` class, the two icon fields and their textures.
- `About/Manifest.xml` points at this fork instead of upstream.
