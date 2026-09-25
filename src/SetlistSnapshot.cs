using System;
using System.Collections.Generic;
using System.Text;

namespace YargSetlistBridge
{
    /// <summary>
    /// What the setlist looks like at one moment, in plain types only, so equality and
    /// serialization never touch YARG's own classes.
    /// </summary>
    internal sealed class SetlistSnapshot : IEquatable<SetlistSnapshot>
    {
        public const string ModeIdle     = "idle";
        public const string ModeBuilding = "building";
        public const string ModePlaying  = "playing";

        public string   Mode;
        public int      Index;   // meaningful only when Mode == playing
        public string[] Songs;   // uppercase hex hashes, in setlist order
        public string   Current; // hash of the song in Gameplay/Score, else null
        public string   Scene;

        public bool Equals(SetlistSnapshot other)
        {
            if (other == null) return false;
            if (Mode != other.Mode || Index != other.Index || Current != other.Current || Scene != other.Scene)
            {
                return false;
            }
            if (Songs.Length != other.Songs.Length) return false;
            for (int i = 0; i < Songs.Length; i++)
            {
                if (Songs[i] != other.Songs[i]) return false;
            }
            return true;
        }

        public override bool Equals(object obj) => Equals(obj as SetlistSnapshot);

        public override int GetHashCode() => (Mode, Index, Songs.Length, Current, Scene).GetHashCode();

        public string ToJson(long version)
        {
            var sb = new StringBuilder(128 + Songs.Length * 44);
            sb.Append("{\"type\":\"state\",\"version\":").Append(version);
            sb.Append(",\"mode\":").Append(Quote(Mode));
            sb.Append(",\"index\":").Append(Mode == ModePlaying ? Index.ToString() : "null");
            sb.Append(",\"songs\":[");
            for (int i = 0; i < Songs.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Quote(Songs[i]));
            }
            sb.Append("],\"current\":").Append(Current == null ? "null" : Quote(Current));
            sb.Append(",\"scene\":").Append(Quote(Scene));
            sb.Append('}');
            return sb.ToString();
        }

        internal static string Quote(string value)
        {
            var sb = new StringBuilder(value.Length + 2).Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int) c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        internal static readonly string[] NoSongs = Array.Empty<string>();

        internal static string[] ToArray(List<string> list) => list.Count == 0 ? NoSongs : list.ToArray();
    }
}
