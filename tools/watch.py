#!/usr/bin/env python3
"""Connect to a running bridge and print every message it sends.

Also the reference client for PROTOCOL.md: discovery file -> TCP connect -> auth line ->
stream of state lines. Standard library only, Python 3.8+.

    python tools/watch.py                       # release channel, default data folder
    python tools/watch.py nightly
    python tools/watch.py "D:\\some\\YARG\\data"  # any folder holding setlist-bridge.json
"""

import json
import os
import socket
import sys
from datetime import datetime
from pathlib import Path


def default_data_dir(channel):
    # Unity's persistentDataPath for company YARC / product YARG, plus YARG's channel folder.
    if os.name == "nt":
        return Path.home() / "AppData" / "LocalLow" / "YARC" / "YARG" / channel
    config = os.environ.get("XDG_CONFIG_HOME") or str(Path.home() / ".config")
    return Path(config) / "unity3d" / "YARC" / "YARG" / channel


def print_message(message):
    time = datetime.now().strftime("%H:%M:%S.%f")[:-3]
    if message.get("type") != "state":
        print(time, json.dumps(message), flush=True)
        return

    index = message["index"]
    songs = message["songs"]
    position = "" if index is None else f" {index + 1}/{len(songs)}"
    print(f"{time} v{message['version']} {message['scene']} {message['mode']}{position}")
    for i, song_hash in enumerate(songs):
        marker = ">" if index == i else " "
        current = "  (current)" if song_hash == message["current"] else ""
        print(f"    {marker} {song_hash}{current}")
    sys.stdout.flush()


def main():
    arg = sys.argv[1] if len(sys.argv) > 1 else "release"
    data_dir = Path(arg) if Path(arg).is_absolute() else default_data_dir(arg)
    discovery_path = data_dir / "setlist-bridge.json"

    try:
        discovery = json.loads(discovery_path.read_text(encoding="utf-8"))
    except OSError as err:
        sys.exit(f"No bridge found: cannot read {discovery_path} ({err.strerror}).\n"
                 "Is YARG running with the plugin installed?")

    if discovery.get("protocol") != 1:
        sys.exit(f"Unsupported protocol {discovery.get('protocol')}; this client speaks 1.")

    try:
        sock = socket.create_connection(("127.0.0.1", discovery["port"]), timeout=5)
    except OSError as err:
        sys.exit(f"Connection failed: {err}. A stale {discovery_path} "
                 f"(pid {discovery.get('pid')}) can cause this.")

    with sock:
        sock.sendall((json.dumps({"type": "auth", "token": discovery["token"]}) + "\n").encode("utf-8"))
        sock.settimeout(None)  # state only arrives when something changes; wait indefinitely

        with sock.makefile("r", encoding="utf-8", newline="\n") as lines:
            for line in lines:
                print_message(json.loads(line))

    print("Disconnected.")


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        pass
