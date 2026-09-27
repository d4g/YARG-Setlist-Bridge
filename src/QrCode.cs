using Newtonsoft.Json.Linq;

namespace YargSetlistBridge
{
    /// <summary>
    /// The <c>qr</c> command: a QR code for YARG to show, already encoded by the client.
    ///
    /// The client encodes rather than the plugin because the client is where the address
    /// is known — YASS decides between its LAN address and its tunnel, and when either
    /// changes — and because it keeps a QR encoder out of a plugin that would otherwise
    /// only ever need one to draw squares. What arrives is the finished grid, one character
    /// per module, row by row: <c>1</c> dark, <c>0</c> light. See PROTOCOL.md §5.
    ///
    /// Unity-free, like <see cref="SetlistCommands"/>, so the tests can reach it.
    /// </summary>
    public static class QrCode
    {
        public const string Command = "qr";

        /// <summary>QR version 1 is 21 modules a side; every version adds 4, up to 40.</summary>
        public const int MinSize = 21;
        public const int MaxSize = 57;

        public static bool IsCommand(string type) => type == Command;

        /// <summary>
        /// Checks a <c>qr</c> command. <paramref name="modules"/> comes back null for
        /// <c>"modules":null</c>, which means "show nothing".
        /// </summary>
        /// <returns>An error code, or null when usable.</returns>
        /// <remarks>
        /// Capped at version 10 (57 modules): an address with a key in it needs version 5
        /// or 6, and a grid any bigger would not be readable at the size YARG can spare for
        /// it. The cap also keeps a command well inside the server's line limit.
        /// </remarks>
        public static string Parse(JObject message, out bool[,] modules)
        {
            modules = null;

            var raw = message["modules"];
            if (raw == null) return SetlistCommands.Invalid;
            if (raw.Type == JTokenType.Null) return null;
            if (raw.Type != JTokenType.String) return SetlistCommands.Invalid;

            var size = message["size"];
            if (size == null || size.Type != JTokenType.Integer) return SetlistCommands.Invalid;

            var n = (int) size;
            if (n < MinSize || n > MaxSize || (n - MinSize) % 4 != 0) return SetlistCommands.Invalid;

            var text = (string) raw;
            if (text.Length != n * n) return SetlistCommands.Invalid;

            var grid = new bool[n, n];
            for (int i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c != '0' && c != '1') return SetlistCommands.Invalid;
                grid[i / n, i % n] = c == '1';
            }

            modules = grid;
            return null;
        }
    }
}
