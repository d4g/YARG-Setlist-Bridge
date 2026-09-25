using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace YargSetlistBridge
{
    /// <summary>
    /// The protocol-2 edit commands, as rules over a plain list. See PROTOCOL.md §5.
    ///
    /// YARG keeps the setlist in one of two lists depending on the phase — hashes in the
    /// music library before a show, song entries during one — and the rules are the same
    /// for both: which entries may be touched, where a song may go, what counts as a
    /// duplicate. So they are written once, generically, here, where they can be tested
    /// without Unity; <c>SetlistProbe.Edit</c> only chooses the list and supplies the
    /// YARG-specific bits.
    /// </summary>
    internal static class SetlistCommands
    {
        public const string Add    = "add";
        public const string Remove = "remove";
        public const string Move   = "move";
        public const string Clear  = "clear";

        // Result codes. PROTOCOL.md lists what each one means to a client.
        public const string Invalid     = "invalid";
        public const string UnknownSong = "unknown_song";
        public const string Duplicate   = "duplicate";
        public const string NotFound    = "not_found";
        public const string Locked      = "locked";
        public const string Full        = "full";
        public const string Busy        = "busy";
        public const string Conflict    = "conflict";
        public const string Failed      = "failed";

        /// <summary>
        /// Longest setlist this plugin will build. YARG has no limit of its own; this is a
        /// backstop against a runaway client, far above any real evening.
        /// </summary>
        public const int MaxSongs = 200;

        public static bool IsKnown(string type) => type == Add || type == Remove || type == Move || type == Clear;

        /// <summary>A command's arguments, checked for shape but not yet against any list.</summary>
        public struct Args
        {
            public string Type;
            public string Hash;   // uppercase hex; null for clear
            public int?   Index;  // required for move, optional for add
            public long?  Version;
        }

        /// <summary>Validates a command's fields. Returns an error code, or null when usable.</summary>
        public static string Parse(JObject message, out Args args)
        {
            args = new Args { Type = (string) message["type"] };

            var version = message["version"];
            if (version != null && version.Type != JTokenType.Null)
            {
                if (version.Type != JTokenType.Integer) return Invalid;
                args.Version = (long) version;
            }

            var index = message["index"];
            if (index != null && index.Type != JTokenType.Null)
            {
                if (index.Type != JTokenType.Integer) return Invalid;
                args.Index = (int) index;
            }

            if (args.Type == Clear) return null;

            var hash = message["hash"];
            if (hash == null || hash.Type != JTokenType.String) return Invalid;
            args.Hash = ((string) hash).ToUpperInvariant();
            if (!IsHash(args.Hash)) return Invalid;

            if (args.Type == Move && args.Index == null) return Invalid;
            return null;
        }

        private static bool IsHash(string value)
        {
            if (value.Length != 40) return false;
            foreach (var c in value)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F'))) return false;
            }
            return true;
        }

        /// <summary>
        /// Applies one command to <paramref name="list"/>.
        /// </summary>
        /// <param name="firstEditable">
        /// Index of the first entry that may be removed, moved, or have something placed
        /// before it: 0 before a show; the one after the current song during one, because
        /// the songs already played and the one playing are history.
        /// </param>
        /// <param name="keyOf">The entry's hash, as uppercase hex.</param>
        /// <param name="resolve">A new entry for a hash, or false if the library has no such song.</param>
        /// <returns>An error code, or null on success.</returns>
        public static string Apply<T>(
            List<T> list, int firstEditable, Args args, Func<T, string> keyOf, TryResolve<T> resolve)
        {
            switch (args.Type)
            {
                case Add:
                {
                    if (IndexOf(list, args.Hash, keyOf) >= 0) return Duplicate;
                    if (list.Count >= MaxSongs) return Full;

                    int at = args.Index ?? list.Count;
                    if (at < firstEditable || at > list.Count) return Invalid;
                    if (!resolve(args.Hash, out var entry)) return UnknownSong;

                    list.Insert(at, entry);
                    return null;
                }

                case Remove:
                {
                    int from = IndexOf(list, args.Hash, keyOf);
                    if (from < 0) return NotFound;
                    if (from < firstEditable) return Locked;

                    list.RemoveAt(from);
                    return null;
                }

                case Move:
                {
                    int from = IndexOf(list, args.Hash, keyOf);
                    if (from < 0) return NotFound;
                    if (from < firstEditable) return Locked;

                    int to = args.Index.Value;
                    if (to < firstEditable || to >= list.Count) return Invalid;

                    var entry = list[from];
                    list.RemoveAt(from);
                    list.Insert(to, entry);
                    return null;
                }

                case Clear:
                {
                    if (firstEditable < list.Count) list.RemoveRange(firstEditable, list.Count - firstEditable);
                    return null;
                }

                default:
                    return Invalid;
            }
        }

        public delegate bool TryResolve<T>(string hash, out T entry);

        private static int IndexOf<T>(List<T> list, string hash, Func<T, string> keyOf)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (keyOf(list[i]) == hash) return i;
            }
            return -1;
        }
    }
}
