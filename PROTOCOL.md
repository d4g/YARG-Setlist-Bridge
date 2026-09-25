# Protocol, version 1

How a local app (YASS, or anything else) reads YARG's setlist from this plugin. Version 1
is **read-only**: clients are told the setlist; they cannot change it yet.

## 1. Discovery

While YARG runs with the plugin, it writes `setlist-bridge.json` into YARG's own data folder,
the one that holds `songcache.bin`:

```
Windows: %USERPROFILE%\AppData\LocalLow\YARC\YARG\<channel>\setlist-bridge.json
Linux:   ~/.config/unity3d/YARC/YARG/<channel>/setlist-bridge.json
```

`<channel>` is `release` or `nightly`, or YARG's `-persistent-data-path` if one was given.

```json
{"protocol":1,"port":36110,"token":"9f2c…","pid":12345,"plugin":"0.1.0","yarg":"v0.15"}
```

- The file is written atomically (temp file, then rename) and deleted when YARG quits.
- A crash can leave it behind. If connecting fails, check whether a process with `pid` is still
  running before trusting it.
- `token` is regenerated every time YARG starts. Re-read the file after a failed connect.
- Watch the file to notice YARG starting and stopping, as clients already do for `songcache.bin`.

## 2. Transport

TCP on `127.0.0.1:<port>`. Every message in both directions is one JSON object on one line,
UTF-8, terminated by `\n`. Lines longer than 4096 bytes from a client close the connection.

## 3. Handshake

The client's first line must arrive within 5 seconds:

```json
{"type":"auth","token":"<token from the discovery file>"}
```

If the token is wrong, the server replies `{"type":"error","code":"unauthorized"}` and closes the
connection. If it's right, the server sends:

```json
{"type":"hello","protocol":1,"plugin":"0.1.0"}
```

and then, as soon as it has one, the current `state`.

## 4. `state`

Sent once after the handshake, then every time anything in it changes.

```json
{
  "type": "state",
  "version": 17,
  "mode": "playing",
  "index": 2,
  "songs": ["3A0F…", "B71C…", "04DE…"],
  "current": "04DE…",
  "scene": "Gameplay"
}
```

| Field | Meaning |
|---|---|
| `version` | Increases by one each time the state changes, starting at 1. It resets when YARG restarts. Changes that come close together are merged, so numbers can be skipped. |
| `mode` | `idle`: there is no setlist. `building`: the menu scene is loaded and a setlist exists, including while players pick difficulties just before it starts. `playing`: a show song is loaded (scene `Gameplay` or `Score`). |
| `index` | Position of the current show song in `songs`, from 0. `null` unless `mode` is `playing`. |
| `songs` | Song hashes in setlist order, as 40 uppercase hex characters. That's the same form as YARG's playlist files and the hashes YASS reads from `songcache.bin`. |
| `current` | Hash of the song loaded in the gameplay or score scene. `null` elsewhere. It's set for single songs too, not only during a show. |
| `scene` | YARG's current scene: `Persistent`, `Menu`, `Gameplay`, `Calibration` or `Score`. |

Each message is the complete state, never a partial update, so a client only needs to keep
the latest one.

## 5. Anything else

In version 1, the server answers every message after `auth` with
`{"type":"error","code":"unsupported"}`. Commands that change the setlist are reserved for
version 2.

## 6. Limits that come from YARG

- **The setlist is read while the menu scene is loaded.** Before a show, the setlist lives on
  the music library screen. The plugin finds that screen when the menu scene loads, so
  `building` can take up to a second to appear after YARG starts.
- **`playing` is only ever reported from `Gameplay` or `Score`.** YARG marks a show as started
  when difficulty select opens, and leaves that mark in place if the player backs out. So in the
  menu, the plugin reports the music library's list (`building`) and ignores the show mark,
  except while difficulty select is actually open.
- **Skipped songs go straight to the next one.** Skipping from the setlist pause menu moves
  from `Gameplay` to `Gameplay` with `index` one higher. There's no `Score` state in between.
- **After a show, expect `idle`.** Both ending early and finishing reset YARG's show state and
  load a fresh menu, whose music library starts empty. This was confirmed in the game (YARG
  nightly b4076). Clients should still just render whatever state arrives.

A typical sequence: the first in-game test run, abridged, as plugin 0.1.1 reports it:

```
v1 Menu idle
v2 Menu building            (songs being added)
v4 Menu building            (difficulty select, same songs)
v5 Gameplay playing 1/8     current = songs[0]
v6 Gameplay playing 2/8     (skipped: no Score in between)
v8 Menu idle                (show ended early)
```
