![banner](https://github.com/Zapdude/Multiplayer/blob/personal/dev-integration/About/Preview.png)



Multiplayer Bodged is a personal fork of [Multiplayer](https://github.com/rwmt/Multiplayer/) that's built upon the dev branch, includes feisty new PRs, and my own fixes. It's only hosted on GitHub for the sake of my sanity and is solely intended to be used by me and my friends currently.

## Building for RimWorld 1.7

This source branch targets RimWorld 1.7, .NET 10, and Harmony 3. SDK selection
comes from `global.json`; later installed .NET 10 SDKs are allowed by `latestMinor`.
Prepatcher is not required. Normal builds have no game-version selector.

```sh
dotnet build Source/Multiplayer.sln -c Release
dotnet test Source/Tests/Tests.csproj -c Release --no-build
```

The client build stages resolved mod dependencies into `1.7/Assemblies` and
`1.7/AssembliesCustom`. Game and Harmony DLLs are supplied by the game/mod
installation and must not be packaged. Full host/join, reload/rejoin, stockpile
filter/cache, and ritual scenarios still require live game testing.

## Desync tracing modes

RNG, world, map, and command synchronization checks remain enabled in every mode.
The normal **Fast** setting records lightweight event context (tick, RNG state and
thing identity), without walking managed call stacks or formatting stack strings.
**None** disables diagnostic event history. **Slow** additionally captures detailed
managed stacks for each eligible event and can significantly increase CPU and memory
usage. Use Slow only when collecting diagnostics for a reproducible issue.

Diagnostic history also requires the session's desync trace logging to be enabled.
The selector is available in the normal mod settings in Release builds, and the
chosen mode is saved. Managed tracing works on ARM64 as well as x64.

## Workshop releases

`./workshop_bundler.sh` builds the current RimWorld 1.7 mod and retains 1.5 and
1.6 in the release package. The older versions come from their frozen upstream
release sources; they do not share the 1.7 implementation. For 1.6, the bundler
uses `rw-1.6` when available, otherwise commit
`9df94bd04ea9a94c00bd39a276e58c4c2f965740`.

## Donations

If you’re feeling generous these are people who have contributed greatly to the original mod’s development and upkeep. I don't know why you're considering donating here, though... this is a fork. Why are you reading this, actually?

**[Zetrith](https://patreon.com/zetrith)** - Creator, Core, Support\
**[NotFood](https://ko-fi.com/notfood)** - Core, Mod Compatiblity, Compatibility Commissions\
**[Sokyran](https://ko-fi.com/sokyran)** - Core, Mod Compatiblity, Compatibility Commissions\
[Nebual](https://ko-fi.com/Nebual) - Core\
[Thomas107500](https://ko-fi.com/thomas107500) - Mod Compatiblity\
[Luz](https://ko-fi.com/llavorre) - Support, Admin\
[Mistress Mia](https://ko-fi.com/miaamakiir) - Support, Admin\
[Swept](https://ko-fi.com/swept) - Support, Admin, Website

## Notes
Thanks to Pardeike for making [Harmony](https://github.com/pardeike/Harmony) and RevenantX for creating [LiteNetLib](https://github.com/RevenantX/LiteNetLib). Thanks to me for being awesome and deciding to slowly learn C# (I hate it).

## AI Disclosure

I have been using deepseek to simplify checking traces, and to assist me in pulling up relevant files which fire errors. Some of the code is written by me, some is written by Deepseek and carefully verified by me. As time goes on and I learn more I intend to turn "some of" into "all of." If we let machines do the thinking for us we will die a miserable death indeed, hence all solutions are thought up by me and guided by BigData(tm) until I can take the training wheels off. 
