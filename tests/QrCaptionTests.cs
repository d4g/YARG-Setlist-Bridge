using System;
using Newtonsoft.Json.Linq;
using Xunit;

namespace YargSetlistBridge.Tests
{
    public class QrCaptionTests
    {
        /// <summary>The PNG signature and a little more: enough to pass as a picture here.</summary>
        private static readonly string Png =
            Convert.ToBase64String(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13 });

        private static string Parse(string json, out QrCaption.Caption caption) =>
            QrCaption.Parse(JObject.Parse(json), out caption);

        [Fact]
        public void ReadsPlayerAndSong()
        {
            Assert.Null(Parse(
                $"{{\"type\":\"caption\",\"player\":{{\"name\":\" Anna \",\"color\":\"#ff7ad9\",\"image\":\"{Png}\"}},\"song\":\"Queen – Bohemian Rhapsody\"}}",
                out var caption));

            Assert.Equal("Anna", caption.PlayerName);
            Assert.Equal("#ff7ad9", caption.PlayerColor);
            Assert.Equal(12, caption.PlayerImage.Length);
            Assert.Equal("Queen – Bohemian Rhapsody", caption.Song);
        }

        [Fact]
        public void TakesEitherHalfAlone()
        {
            Assert.Null(Parse("{\"type\":\"caption\",\"player\":null,\"song\":\"Queen – Bohemian Rhapsody\"}", out var songOnly));
            Assert.False(songOnly.HasPlayer);

            Assert.Null(Parse("{\"type\":\"caption\",\"player\":{\"name\":\"Fox\",\"color\":\"#45d8fe\"}}", out var playerOnly));
            Assert.True(playerOnly.HasPlayer);
            Assert.Null(playerOnly.PlayerImage);
            Assert.Null(playerOnly.Song);
        }

        [Fact]
        public void NothingToShowMeansNoCaption()
        {
            Assert.Null(Parse("{\"type\":\"caption\",\"player\":null,\"song\":null}", out var caption));
            Assert.Null(caption);
            Assert.Null(Parse("{\"type\":\"caption\"}", out caption));
            Assert.Null(caption);
        }

        [Theory]
        [InlineData("{\"type\":\"caption\",\"song\":17}")]
        [InlineData("{\"type\":\"caption\",\"song\":\"  \"}")]
        [InlineData("{\"type\":\"caption\",\"player\":\"Anna\"}")]
        [InlineData("{\"type\":\"caption\",\"player\":{\"color\":\"#ff7ad9\"}}")]
        [InlineData("{\"type\":\"caption\",\"player\":{\"name\":\"Anna\",\"color\":\"pink\"}}")]
        [InlineData("{\"type\":\"caption\",\"player\":{\"name\":\"Anna\",\"color\":\"#ff7ad9\",\"image\":\"not base64!\"}}")]
        [InlineData("{\"type\":\"caption\",\"player\":{\"name\":\"Anna\",\"color\":\"#ff7ad9\",\"image\":\"AAAA\"}}")]
        public void RefusesWhatItCannotDraw(string json)
        {
            Assert.Equal(SetlistCommands.Invalid, Parse(json, out _));
        }

        [Fact]
        public void RefusesAnOversizedPicture()
        {
            var big = new byte[QrCaption.MaxImageBytes + 1];
            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(big, 0);
            var json = $"{{\"type\":\"caption\",\"player\":{{\"name\":\"Anna\",\"color\":\"#ff7ad9\",\"image\":\"{Convert.ToBase64String(big)}\"}}}}";
            Assert.Equal(SetlistCommands.Invalid, Parse(json, out _));
        }

        [Fact]
        public void TheLargestCaptionFitsInOneProtocolLine()
        {
            // BridgeServer drops a client whose line passes its limit, counted in UTF-8 bytes.
            // The worst case is text of characters that take four bytes each.
            var image = Convert.ToBase64String(new byte[QrCaption.MaxImageBytes]);
            var name = string.Concat(System.Linq.Enumerable.Repeat("\U0001F98A", QrCaption.MaxNameLength / 2));
            var song = string.Concat(System.Linq.Enumerable.Repeat("\U0001F98A", QrCaption.MaxSongLength / 2));
            var line = $"{{\"type\":\"caption\",\"id\":\"99999\",\"player\":{{\"name\":\"{name}\",\"color\":\"#ff7ad9\",\"image\":\"{image}\"}},\"song\":\"{song}\"}}";
            var bytes = System.Text.Encoding.UTF8.GetByteCount(line);
            Assert.True(bytes < BridgeServer.MaxLineBytes, $"{bytes} bytes");
        }
    }
}
