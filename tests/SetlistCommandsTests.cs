using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace YargSetlistBridge.Tests
{
    public class SetlistCommandsTests
    {
        private const string A = "52302429C0ACBCD1612B144FCCB3565BB2C20109";
        private const string B = "33D3D0D05A9D7C1D2E9A2FE59C23EDCB370A6A29";
        private const string C = "B0A8886C85ABB42D4511F811C7D580CBEE161608";
        private const string D = "ECC9939D1094CCEDF12B78E9CF20A02D5C336741";
        private const string Missing = "0000000000000000000000000000000000000000";

        private static string Run(List<string> list, int firstEditable, string json)
        {
            var code = SetlistCommands.Parse(JObject.Parse(json), out var args);
            if (code != null) return code;

            return SetlistCommands.Apply(list, firstEditable, args, hash => hash,
                (string hash, out string entry) =>
                {
                    entry = hash;
                    return hash != Missing;
                });
        }

        [Fact]
        public void AddAppendsByDefault()
        {
            var list = new List<string> { A };
            Assert.Null(Run(list, 0, $"{{\"type\":\"add\",\"hash\":\"{B}\"}}"));
            Assert.Equal(new[] { A, B }, list);
        }

        [Fact]
        public void AddAcceptsLowercaseAndAPosition()
        {
            var list = new List<string> { A, B };
            Assert.Null(Run(list, 0, $"{{\"type\":\"add\",\"hash\":\"{C.ToLowerInvariant()}\",\"index\":1}}"));
            Assert.Equal(new[] { A, C, B }, list);
        }

        [Fact]
        public void AddRefusesDuplicatesAndUnknownSongs()
        {
            var list = new List<string> { A };
            Assert.Equal(SetlistCommands.Duplicate, Run(list, 0, $"{{\"type\":\"add\",\"hash\":\"{A}\"}}"));
            Assert.Equal(SetlistCommands.UnknownSong, Run(list, 0, $"{{\"type\":\"add\",\"hash\":\"{Missing}\"}}"));
            Assert.Equal(new[] { A }, list);
        }

        [Fact]
        public void AddStopsAtTheLimit()
        {
            var list = Enumerable.Range(0, SetlistCommands.MaxSongs).Select(i => i.ToString("X40")).ToList();
            Assert.Equal(SetlistCommands.Full, Run(list, 0, $"{{\"type\":\"add\",\"hash\":\"{A}\"}}"));
        }

        [Fact]
        public void DuringAShowNothingGoesBeforeTheNextSong()
        {
            // Song 2 of 3 is playing: A is played, B is current, C is next.
            var list = new List<string> { A, B, C };
            Assert.Equal(SetlistCommands.Invalid, Run(list, 2, $"{{\"type\":\"add\",\"hash\":\"{D}\",\"index\":1}}"));
            Assert.Null(Run(list, 2, $"{{\"type\":\"add\",\"hash\":\"{D}\",\"index\":2}}"));
            Assert.Equal(new[] { A, B, D, C }, list);
        }

        [Fact]
        public void RemoveAndMoveLeavePlayedAndCurrentSongsAlone()
        {
            var list = new List<string> { A, B, C, D };
            Assert.Equal(SetlistCommands.Locked, Run(list, 2, $"{{\"type\":\"remove\",\"hash\":\"{B}\"}}"));
            Assert.Equal(SetlistCommands.Locked, Run(list, 2, $"{{\"type\":\"move\",\"hash\":\"{A}\",\"index\":3}}"));
            Assert.Equal(SetlistCommands.Invalid, Run(list, 2, $"{{\"type\":\"move\",\"hash\":\"{D}\",\"index\":1}}"));
            Assert.Equal(new[] { A, B, C, D }, list);
        }

        [Fact]
        public void MovePutsTheSongAtTheGivenIndex()
        {
            var list = new List<string> { A, B, C, D };
            Assert.Null(Run(list, 0, $"{{\"type\":\"move\",\"hash\":\"{D}\",\"index\":0}}"));
            Assert.Equal(new[] { D, A, B, C }, list);

            Assert.Null(Run(list, 0, $"{{\"type\":\"move\",\"hash\":\"{D}\",\"index\":3}}"));
            Assert.Equal(new[] { A, B, C, D }, list);
        }

        [Fact]
        public void RemoveAndMoveReportMissingSongs()
        {
            var list = new List<string> { A };
            Assert.Equal(SetlistCommands.NotFound, Run(list, 0, $"{{\"type\":\"remove\",\"hash\":\"{B}\"}}"));
            Assert.Equal(SetlistCommands.NotFound, Run(list, 0, $"{{\"type\":\"move\",\"hash\":\"{B}\",\"index\":0}}"));
        }

        [Fact]
        public void ClearKeepsWhatHasBeenPlayed()
        {
            var list = new List<string> { A, B, C, D };
            Assert.Null(Run(list, 2, "{\"type\":\"clear\"}"));
            Assert.Equal(new[] { A, B }, list);

            var building = new List<string> { A, B };
            Assert.Null(Run(building, 0, "{\"type\":\"clear\"}"));
            Assert.Empty(building);
        }

        [Theory]
        [InlineData("{\"type\":\"add\"}")]
        [InlineData("{\"type\":\"add\",\"hash\":\"nope\"}")]
        [InlineData("{\"type\":\"add\",\"hash\":42}")]
        [InlineData("{\"type\":\"move\",\"hash\":\"52302429C0ACBCD1612B144FCCB3565BB2C20109\"}")]
        [InlineData("{\"type\":\"move\",\"hash\":\"52302429C0ACBCD1612B144FCCB3565BB2C20109\",\"index\":\"1\"}")]
        [InlineData("{\"type\":\"clear\",\"version\":\"7\"}")]
        public void MalformedCommandsAreInvalid(string json)
        {
            Assert.Equal(SetlistCommands.Invalid, SetlistCommands.Parse(JObject.Parse(json), out _));
        }

        [Fact]
        public void VersionIsParsedWhenPresent()
        {
            Assert.Null(SetlistCommands.Parse(JObject.Parse("{\"type\":\"clear\",\"version\":7}"), out var args));
            Assert.Equal(7, args.Version);
        }
    }
}
