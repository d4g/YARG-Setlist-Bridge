using System;
using Newtonsoft.Json.Linq;

namespace YargSetlistBridge
{
    /// <summary>
    /// The <c>caption</c> command: what YARG shows under the QR code on its score screens.
    ///
    /// Who added the next song and the song itself, as the client knows them. The player
    /// comes with a picture of their emoji, because YARG cannot draw colour emoji from text:
    /// TextMeshPro reads glyph outlines, and no font YARG has carries emoji. See PROTOCOL.md §5.
    ///
    /// Unity-free, like <see cref="QrCode"/>, so the tests can reach it.
    /// </summary>
    public static class QrCaption
    {
        public const string Command = "caption";

        public const int MaxNameLength = 64;
        public const int MaxSongLength = 200;
        /// <summary>A 72×72 emoji PNG is 1–2 KB; this leaves room and still fits one protocol line.</summary>
        public const int MaxImageBytes = 2400;

        public static bool IsCommand(string type) => type == Command;

        public sealed class Caption
        {
            public string PlayerName;
            /// <summary><c>#rrggbb</c>.</summary>
            public string PlayerColor;
            /// <summary>PNG bytes, or null when the client sent no picture.</summary>
            public byte[] PlayerImage;
            public string Song;

            public bool HasPlayer => PlayerName != null;
            public bool IsEmpty => PlayerName == null && Song == null;
        }

        /// <summary>
        /// Checks a <c>caption</c> command. <paramref name="caption"/> comes back null when
        /// there is nothing to show — both fields missing or null.
        /// </summary>
        /// <returns>An error code, or null when usable.</returns>
        public static string Parse(JObject message, out Caption caption)
        {
            caption = null;
            var parsed = new Caption();

            var song = message["song"];
            if (song != null && song.Type != JTokenType.Null)
            {
                if (song.Type != JTokenType.String) return SetlistCommands.Invalid;
                var text = ((string) song).Trim();
                if (text.Length == 0 || text.Length > MaxSongLength) return SetlistCommands.Invalid;
                parsed.Song = text;
            }

            var player = message["player"];
            if (player != null && player.Type != JTokenType.Null)
            {
                if (player.Type != JTokenType.Object) return SetlistCommands.Invalid;

                var name = player["name"];
                if (name == null || name.Type != JTokenType.String) return SetlistCommands.Invalid;
                var nameText = ((string) name).Trim();
                if (nameText.Length == 0 || nameText.Length > MaxNameLength) return SetlistCommands.Invalid;

                var color = player["color"];
                if (color == null || color.Type != JTokenType.String || !IsHexColor((string) color))
                {
                    return SetlistCommands.Invalid;
                }

                var image = player["image"];
                if (image != null && image.Type != JTokenType.Null)
                {
                    if (image.Type != JTokenType.String) return SetlistCommands.Invalid;
                    byte[] bytes;
                    try
                    {
                        bytes = Convert.FromBase64String((string) image);
                    }
                    catch (FormatException)
                    {
                        return SetlistCommands.Invalid;
                    }
                    if (bytes.Length > MaxImageBytes || !IsPng(bytes)) return SetlistCommands.Invalid;
                    parsed.PlayerImage = bytes;
                }

                parsed.PlayerName = nameText;
                parsed.PlayerColor = (string) color;
            }

            caption = parsed.IsEmpty ? null : parsed;
            return null;
        }

        private static bool IsHexColor(string value)
        {
            if (value.Length != 7 || value[0] != '#') return false;
            for (int i = 1; i < 7; i++)
            {
                var c = value[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return false;
            }
            return true;
        }

        private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        private static bool IsPng(byte[] bytes)
        {
            if (bytes.Length < PngSignature.Length) return false;
            for (int i = 0; i < PngSignature.Length; i++)
            {
                if (bytes[i] != PngSignature[i]) return false;
            }
            return true;
        }
    }
}
