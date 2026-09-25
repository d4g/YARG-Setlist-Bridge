# YARG Setlist Bridge

A [BepInEx](https://github.com/BepInEx/BepInEx) plugin for [YARG](https://yarg.in). It
lets apps on the same computer, such as [YASS](https://github.com/DevPrice/YASS), see YARG's
current setlist and add, remove or reorder its songs.

It doesn't change any of YARG's files. It runs inside the game and works on the setlist in
memory, the same one YARG's own music library edits. It listens only on `127.0.0.1`, and a client needs a token that YARG writes into its
own data folder. The wire format is in [PROTOCOL.md](PROTOCOL.md).

**Status:** version 0.2 (protocol version 2): reading and editing the setlist.

## Install

1. Download BepInEx **5.4.23.x**, the `win_x64` build for Windows or `linux_x64` for Linux,
   from the [BepInEx releases page](https://github.com/BepInEx/BepInEx/releases). YARG uses
   Mono, so BepInEx 5 is the right version; BepInEx 6 isn't needed.
2. Extract it into the YARG install folder, next to `YARG.exe`. For a launcher install, that's
   `…\YARG Installs\<id>\installation\`.
3. Start YARG once so BepInEx creates its folders, then quit.
4. Copy `YargSetlistBridge.dll` into `BepInEx\plugins\YargSetlistBridge\`.
5. Start YARG. `BepInEx\LogOutput.log` should contain `Listening on 127.0.0.1:36110`, and
   YARG's data folder should contain `setlist-bridge.json` (see [PROTOCOL.md](PROTOCOL.md) §1
   for where that folder is).

On Linux, start YARG through BepInEx's `run_bepinex.sh`.

### Watch it live (optional)

`tools/watch.py` connects to the plugin and prints every setlist change. It needs Python 3.8
or later, with no packages to install, and runs on Windows and Linux.

```
python tools/watch.py            # release
python tools/watch.py nightly    # nightly, or pass a data folder path
```

Leave it running, then add songs to a setlist in YARG.

### If the log shows nothing from the plugin

Some Unity 6 games destroy BepInEx's manager object as soon as they start. In
`BepInEx\config\BepInEx.cfg`, set `HideManagerGameObject = true` under `[Chainloader]`.

### Updates

The YARC Launcher replaces the install folder when it updates YARG, and that can remove
BepInEx. Reinstall it after updating. YARG updates can also change the internal names this
plugin reads. If they do, the plugin logs `Disabled: … this YARG version is probably
unsupported` and turns itself off. YARG keeps running normally.

## Settings

`BepInEx\config\dev.yalcybuild.setlistbridge.cfg`, created on first run:

| Setting | Default | |
|---|---|---|
| `Server.Port` | `36110` | `0` picks a free port. Clients read the port from the discovery file, so any value works. |
| `Server.PollIntervalSeconds` | `0.25` | How often the plugin checks the setlist for changes. |
| `Game.ToastOnAdd` | `true` | Show a toast in YARG when an app adds a song. Never shown during gameplay. |

## Build

You need the .NET SDK (any version that can build `netstandard2.1`) and an installed copy of
YARG to compile against.

```powershell
copy local.props.example local.props   # then set YargManagedDir, and optionally YargInstallDir
dotnet build src -c Release
```

The DLL is written to `src\bin\Release\netstandard2.1\YargSetlistBridge.dll`. If
`YargInstallDir` is set, the build also copies it into that install's BepInEx plugins folder.

YARG's and Unity's assemblies are referenced from the install folder. They're never copied
into this repo or into the build output.

## License

In the public domain under [the Unlicense](LICENSE), the same as YASS.

## Not affiliated

This is an unofficial mod. It isn't affiliated with or supported by YARC. Please don't report
bugs to YARG from a modded install; remove the plugin first and check whether the bug still
happens.
