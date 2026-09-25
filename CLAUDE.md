# CLAUDE.md

## What this is

A BepInEx 5 plugin, loaded into YARG's Mono runtime. It publishes YARG's current setlist
over newline-delimited JSON on `127.0.0.1` so that YASS, a sibling repo, can use it.
[PROTOCOL.md](PROTOCOL.md) is the contract between them. Change it together with the code
and bump `Plugin.ProtocolVersion` for anything that breaks existing clients.

## Build

`dotnet build src -c Release`. It needs `local.props` (copied from `local.props.example`)
pointing `YargManagedDir` at an installed YARG's `YARG_Data\Managed`. The build compiles
against the **installed game's** assemblies, not YARG's source, because those are what the
plugin meets at runtime. Never commit or copy those DLLs; every reference is `Private="false"`.

`python tools/watch.py [release|nightly|<dir>]` (standard library only, deliberately, so
testing needs nothing beyond Python) connects to a running bridge and prints what
it sends. It's also the reference client.

## Design rules

- **Only `SetlistProbe.cs` touches YARG types.** Every YARG-facing method there is
  `NoInlining`, so when a YARG update renames something, the JIT error is thrown at a call
  site in `Plugin.Update`. That catch disables the plugin instead of breaking YARG.
- **Poll, don't patch.** Version 1 has no Harmony patches: it reads public members only
  (`GlobalVariables.State`, `MusicLibraryMenu.ShowPlaylist`, `PathHelper`). Add a patch only
  when polling can't do the job, and record the reason here.
- **The main thread never touches a socket.** `BridgeServer.Publish` stores the latest message
  and signals; a sender thread does the writes. Commands in protocol version 2 must go the
  other way through a queue that `Update` drains, because YARG's state may only be changed on
  the main thread.
- **`BridgeServer` and `SetlistSnapshot` don't reference Unity or YARG**, so they can be
  tested with a plain .NET console app that compiles those two files.
- The discovery file lives in YARG's data folder, not BepInEx's, because that's the folder
  YASS already finds and watches.

## YARG facts this depends on

- Before a show, the setlist is `MusicLibraryMenu.ShowPlaylist` on the menu instance, which
  exists only while the menu scene is loaded. Once the show starts, it's copied into
  `GlobalVariables.State.ShowSongs` + `ShowIndex`. That is why `SetlistProbe` checks
  `PlayingAShow` first.
- The score screen reads `ShowSongs[ShowIndex]` again on every Continue. That's why editing
  the show list in place will work for protocol version 2.
- YARG release builds use Mono (`YARG_Data\Managed\Assembly-CSharp.dll`, no `GameAssembly.dll`).
  If they switch to IL2CPP, this plugin needs BepInEx 6 and interop assemblies.
