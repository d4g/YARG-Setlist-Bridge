#!/usr/bin/env python3
"""Send one edit command to a running bridge and print its result.

For trying protocol version 2 against the game without YASS. Standard library only.

    python tools/edit.py nightly add 52302429C0ACBCD1612B144FCCB3565BB2C20109
    python tools/edit.py nightly add <hash> 0          # insert at position 0
    python tools/edit.py nightly remove <hash>
    python tools/edit.py nightly move <hash> 2
    python tools/edit.py nightly clear

The first argument is `release`, `nightly` or a data folder, as for watch.py. Run watch.py
alongside to see the state change.
"""

import json
import socket
import sys

from watch import default_data_dir
from pathlib import Path


def main():
    if len(sys.argv) < 3:
        sys.exit(__doc__)

    where, command, *rest = sys.argv[1:]
    data_dir = Path(where) if Path(where).is_absolute() else default_data_dir(where)
    discovery = json.loads((data_dir / "setlist-bridge.json").read_text(encoding="utf-8"))
    if discovery.get("protocol", 0) < 2:
        sys.exit("This plugin speaks protocol 1, which is read-only. Update it to 0.2 or later.")

    message = {"type": command, "id": "edit.py"}
    if command != "clear":
        if not rest:
            sys.exit(f"{command} needs a song hash.")
        message["hash"] = rest[0]
    if len(rest) > 1:
        message["index"] = int(rest[1])

    with socket.create_connection(("127.0.0.1", discovery["port"]), timeout=5) as sock:
        sock.sendall((json.dumps({"type": "auth", "token": discovery["token"]}) + "\n").encode())
        sock.sendall((json.dumps(message) + "\n").encode())
        with sock.makefile("r", encoding="utf-8", newline="\n") as lines:
            for line in lines:
                reply = json.loads(line)
                if reply.get("type") in ("result", "error"):
                    print(json.dumps(reply))
                    return


if __name__ == "__main__":
    main()
