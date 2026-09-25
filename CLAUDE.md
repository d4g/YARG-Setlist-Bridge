# CLAUDE.md

## What this is

A BepInEx 5 plugin, loaded into YARG's Mono runtime. It publishes YARG's current setlist
over newline-delimited JSON on `127.0.0.1`, and takes commands to edit it, so that YASS, a
sibling repo, can show and manage it.
[PROTOCOL.md](PROTOCOL.md) is the contract between them. Change it together with the code
and bump `Plugin.ProtocolVersion` for anything that breaks existing clients.

## Build

`dotnet build src -c Release`. It needs `local.props` (copied from `local.props.example`)
pointing `YargManagedDir` at an installed YARG's `YARG_Data\Managed`. The build compiles
against the **installed game's** assemblies, not YARG's source, because those are what the
plugin meets at runtime. Never commit or copy those DLLs; every reference is `Private="false"`.

`dotnet test tests` runs the unit tests: the server over a real loopback socket, and the
command rules. The test project compiles the Unity-free files in from `src/` rather than
referencing the plugin, which can't load without YARG.

`python tools/watch.py [release|nightly|<dir>]` (standard library only, deliberately, so
testing needs nothing beyond Python) connects to a running bridge and prints what
it sends. It's also the reference client.

## Design rules

- **Only `SetlistProbe.cs` and `SetlistProbe.Edit.cs` touch YARG types.** Every YARG-facing method there is
  `NoInlining`, so when a YARG update renames something, the JIT error is thrown at a call
  site in `Plugin.Update`. That catch disables the plugin instead of breaking YARG.
- **Poll, don't patch.** There are no Harmony patches: it reads and edits public members only
  (`GlobalVariables.State`, `MusicLibraryMenu.ShowPlaylist`, `SongContainer`, `PathHelper`).
  Add a patch only when that can't do the job, and record the reason here.
- **The main thread never touches a socket.** `BridgeServer.Publish` and `Reply` store the
  message and signal; a sender thread does the writes. Commands come the other way through a
  queue that `Update` drains, because YARG's state may only be changed on the main thread.
  After a successful command, `Update` publishes before taking the next, so a command's
  `version` is always checked against the list it will change.
- **Edit rules live in `SetlistCommands`, generically over a list.** YARG's two setlist lists
  (hashes in the library, song entries during a show) follow the same rules; `SetlistProbe.Edit`
  only picks the list and the first editable index.
- **Never call `Playlist.MoveSongUp/Down`** on the setlist: they save to disk even when it is
  ephemeral. Edit `SongHashes` directly.
- **`BridgeServer`, `SetlistCommands` and `SetlistSnapshot` don't reference Unity or YARG.**
  Keep them that way; the tests depend on it.
- The discovery file lives in YARG's data folder, not BepInEx's, because that's the folder
  YASS already finds and watches.

## YARG facts this depends on

- Before a show, the setlist is `MusicLibraryMenu.ShowPlaylist` on the menu instance, which
  exists only while the menu scene is loaded. Once the show starts, it's copied into
  `GlobalVariables.State.ShowSongs` + `ShowIndex`. That is why `SetlistProbe` checks
  `PlayingAShow` first.
- The score screen reads `ShowSongs[ShowIndex]` again on every Continue, which is why editing
  `ShowSongs` in place during a show works.
- `MusicLibraryMenu.SetNavigationScheme(true)` pops YARG's navigation stack, so it is not
  called from outside; see `SetlistProbe.Edit.cs` for what that costs.
- YARG release builds use Mono (`YARG_Data\Managed\Assembly-CSharp.dll`, no `GameAssembly.dll`).
  If they switch to IL2CPP, this plugin needs BepInEx 6 and interop assemblies.
