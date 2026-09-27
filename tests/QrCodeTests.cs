using System.Linq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace YargSetlistBridge.Tests
{
    public class QrCodeTests
    {
        private static string Grid(int size, char fill = '0') => new string(fill, size * size);

        private static string Parse(string json, out bool[,] modules) =>
            QrCode.Parse(JObject.Parse(json), out modules);

        [Fact]
        public void ReadsTheGridRowByRow()
        {
            // Top-left module dark, the one to its right light, the first of row two dark.
            var text = "10" + new string('0', 19) + "1" + new string('0', 21 * 21 - 22);
            Assert.Null(Parse($"{{\"type\":\"qr\",\"size\":21,\"modules\":\"{text}\"}}", out var modules));

            Assert.True(modules[0, 0]);
            Assert.False(modules[0, 1]);
            Assert.True(modules[1, 0]);
            Assert.Equal(2, Enumerable.Range(0, 21 * 21).Count(i => modules[i / 21, i % 21]));
        }

        [Fact]
        public void NullModulesMeanShowNothing()
        {
            Assert.Null(Parse("{\"type\":\"qr\",\"modules\":null}", out var modules));
            Assert.Null(modules);
        }

        [Theory]
        [InlineData(20)] // below version 1
        [InlineData(23)] // not 21 + 4k
        [InlineData(61)] // past the cap
        public void RefusesSizesThatAreNotAQrVersionItTakes(int size)
        {
            Assert.Equal(SetlistCommands.Invalid,
                Parse($"{{\"type\":\"qr\",\"size\":{size},\"modules\":\"{Grid(size)}\"}}", out _));
        }

        [Fact]
        public void RefusesAGridOfTheWrongLengthOrWithOtherCharacters()
        {
            Assert.Equal(SetlistCommands.Invalid,
                Parse($"{{\"type\":\"qr\",\"size\":21,\"modules\":\"{Grid(21).Substring(1)}\"}}", out _));
            Assert.Equal(SetlistCommands.Invalid,
                Parse($"{{\"type\":\"qr\",\"size\":21,\"modules\":\"2{Grid(21).Substring(1)}\"}}", out _));
        }

        [Fact]
        public void RefusesAMissingOrMistypedField()
        {
            Assert.Equal(SetlistCommands.Invalid, Parse("{\"type\":\"qr\"}", out _));
            Assert.Equal(SetlistCommands.Invalid, Parse("{\"type\":\"qr\",\"modules\":17}", out _));
            Assert.Equal(SetlistCommands.Invalid,
                Parse($"{{\"type\":\"qr\",\"size\":\"21\",\"modules\":\"{Grid(21)}\"}}", out _));
        }

        [Fact]
        public void TheLargestGridFitsInOneProtocolLine()
        {
            // BridgeServer drops a client whose line passes 4096 bytes.
            var line = $"{{\"type\":\"qr\",\"id\":\"99999\",\"size\":{QrCode.MaxSize},\"modules\":\"{Grid(QrCode.MaxSize)}\"}}";
            Assert.True(line.Length < 4096);
        }
    }
}
