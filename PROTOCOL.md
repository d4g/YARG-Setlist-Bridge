# Protocol, version 2

How a local app (YASS, or anything else) reads and edits YARG's setlist through this plugin.

| Version | Plugin | Adds |
|---|---|---|
| 1 | 0.1.x | Reading the setlist (§4). |
| 2 | 0.2.x | Editing it: add, remove, move, clear (§5). Everything in version 1 is unchanged. |

A client written for version 1 works with a version 2 plugin if it accepts `protocol` 2 in
the discovery file: the only new server message is `result`, which only answers commands.

## 1. Discovery

While YARG runs with the plugin, it writes `setlist-bridge.json` into YARG's own data folder,
the one that holds `songcache.bin`:

```
Windows: %USERPROFILE%\AppData\LocalLow\YARC\YARG\<channel>\setlist-bridge.json
Linux:   ~/.config/unity3d/YARC/YARG/<channel>/setlist-bridge.json
```

`<channel>` is `release` or `nightly`, or YARG's `-persistent-data-path` if one was given.

```json
{"protocol":2,"port":36110,"token":"9f2c…","pid":12345,"plugin":"0.2.0","yarg":"v0.15"}
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
{"type":"hello","protocol":2,"plugin":"0.2.0"}
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

## 5. Commands (version 2)

After the handshake a client may send commands. Each gets exactly one `result`, matched by
`id`, which the client chooses and the server echoes back:

```json
{"type":"add","id":"17","hash":"52302429C0ACBCD1612B144FCCB3565BB2C20109"}
{"type":"result","id":"17","ok":true}
{"type":"result","id":"18","ok":false,"code":"duplicate"}
```

| Command | Fields | Does |
|---|---|---|
| `add` | `hash`, optional `index` | Inserts the song at `index`, or at the end. |
| `remove` | `hash` | Removes the song. |
| `move` | `hash`, `index` | Moves the song so that it ends up at `index`. |
| `clear` | | Removes every song that may be removed (see below). |

- `hash` is 40 hex characters, in either case.
- `index` is a position in `songs` as the latest `state` shows it, counted from 0.
- Every command also takes an optional `version`: the `state` version the client was looking
  at. If the setlist has changed since then, the command is refused with `conflict` rather
  than applied to a list the client hasn't seen. Send it with anything that depends on
  positions, such as `move`, or an `add` with an `index`.
- Commands are applied in the order received, on YARG's main thread, within a frame or two.
  After a successful one, the new `state` is published before the next command is looked at.
  Its `result` may arrive before or after that `state`.

**During a show** (`mode` is `playing`), the songs already played and the one playing are
history. Nothing may be removed, moved or inserted at or before `index`; `clear` removes only
the songs after it. **Before a show**, the whole list is editable, and adding to an empty
setlist (`idle`) starts a new one.

| `code` | Meaning |
|---|---|
| `invalid` | A field is missing or malformed, or `index` is out of range. |
| `unknown_song` | YARG's library has no song with that hash. |
| `duplicate` | The song is already in the setlist. YARG's setlists hold each song once. |
| `not_found` | `remove` or `move` named a song that isn't in the setlist. |
| `locked` | The song was already played or is playing. |
| `full` | The setlist is at the plugin's limit of 200 songs. |
| `conflict` | `version` was given and the setlist has changed since. Re-read and retry. |
| `busy` | YARG can't take edits right now: a single song (not a show) is playing, players are picking difficulties for a show about to start, the game is loading, or too many commands are queued. Retry later. |
| `failed` | Something went wrong inside YARG while applying it. Details are in the BepInEx log. |

A message whose `type` isn't a command gets `{"type":"error","code":"unsupported"}`, and a
line that isn't JSON gets `{"type":"error","code":"invalid"}`. Neither has an `id`.

**Side effects in the game.** When a client adds a song, YARG shows a toast naming it,
except during gameplay (setting `Game.ToastOnAdd`). If the music library is on screen, it
redraws the way it does after the host's own edits, including the button hints, so the
green button's label changes from "Play Song" to "Add to Setlist" as soon as the setlist
has a song. The host's search text is kept.

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
