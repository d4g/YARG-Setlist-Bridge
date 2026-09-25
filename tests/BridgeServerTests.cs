using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using Xunit;

namespace YargSetlistBridge.Tests
{
    /// <summary>
    /// The server over a real loopback socket, with the test standing in for Unity's main
    /// thread: it takes commands and replies the way Plugin.Update does.
    /// </summary>
    public sealed class BridgeServerTests : IDisposable
    {
        private const string Token = "secret";
        private readonly BridgeServer _server;

        public BridgeServerTests()
        {
            _server = new BridgeServer(0, Token, "{\"type\":\"hello\",\"protocol\":2,\"plugin\":\"test\"}", _ => { });
            _server.Start();
        }

        public void Dispose() => _server.Dispose();

        private sealed class Connection : IDisposable
        {
            private readonly TcpClient    _tcp;
            private readonly StreamReader _reader;
            private readonly Stream       _stream;

            public Connection(int port)
            {
                _tcp = new TcpClient("127.0.0.1", port) { ReceiveTimeout = 3000 };
                _stream = _tcp.GetStream();
                _reader = new StreamReader(_stream, Encoding.UTF8);
            }

            public void Send(string json)
            {
                var bytes = Encoding.UTF8.GetBytes(json + "\n");
                _stream.Write(bytes, 0, bytes.Length);
            }

            public JObject Read() => JObject.Parse(_reader.ReadLine() ?? throw new EndOfStreamException());

            public void Dispose() => _tcp.Dispose();
        }

        private Connection Connect()
        {
            var connection = new Connection(_server.Port);
            connection.Send($"{{\"type\":\"auth\",\"token\":\"{Token}\"}}");
            Assert.Equal("hello", (string) connection.Read()["type"]);
            return connection;
        }

        private BridgeCommand TakeCommand()
        {
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < deadline)
            {
                if (_server.TryTakeCommand(out var command)) return command;
                Thread.Sleep(5);
            }
            throw new TimeoutException("no command arrived");
        }

        [Fact]
        public void RejectsAWrongToken()
        {
            using var connection = new Connection(_server.Port);
            connection.Send("{\"type\":\"auth\",\"token\":\"wrong\"}");
            Assert.Equal("unauthorized", (string) connection.Read()["code"]);
        }

        [Fact]
        public void SendsTheLatestStateOnConnect()
        {
            _server.Publish("{\"type\":\"state\",\"version\":3}");
            using var connection = Connect();
            Assert.Equal(3, (int) connection.Read()["version"]);
        }

        [Fact]
        public void QueuesCommandsForTheMainThreadAndCarriesTheReplyBack()
        {
            using var connection = Connect();
            connection.Send("{\"type\":\"clear\",\"id\":\"c1\"}");

            var command = TakeCommand();
            Assert.Equal("clear", command.Type);
            Assert.Equal("c1", command.Id);

            _server.Reply(command, SetlistCommands.Busy);
            var reply = connection.Read();
            Assert.Equal("result", (string) reply["type"]);
            Assert.Equal("c1", (string) reply["id"]);
            Assert.False((bool) reply["ok"]);
            Assert.Equal("busy", (string) reply["code"]);
        }

        [Fact]
        public void ReportsSuccessWithoutACode()
        {
            using var connection = Connect();
            connection.Send("{\"type\":\"add\",\"id\":\"a1\",\"hash\":\"52302429C0ACBCD1612B144FCCB3565BB2C20109\"}");

            _server.Reply(TakeCommand(), null);
            var reply = connection.Read();
            Assert.True((bool) reply["ok"]);
            Assert.Null(reply["code"]);
        }

        [Fact]
        public void AnswersUnknownTypesAndBadJsonWithoutQueueingThem()
        {
            using var connection = Connect();
            connection.Send("{\"type\":\"dance\"}");
            Assert.Equal("unsupported", (string) connection.Read()["code"]);

            connection.Send("{not json");
            Assert.Equal("invalid", (string) connection.Read()["code"]);

            Assert.False(_server.TryTakeCommand(out _));
        }
    }
}
