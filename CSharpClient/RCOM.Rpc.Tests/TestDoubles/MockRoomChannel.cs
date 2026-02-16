using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RCOM.Channel;

namespace RCOM.Rpc.Tests.TestDoubles
{
    /// <summary>
    /// IRoomChannel のテストダブル。
    /// 送信されたメッセージを記録し、任意のタイミングで受信をシミュレートできる。
    /// </summary>
    public class MockRoomChannel : IRoomChannel
    {
        private readonly List<string> _sentMessages = new List<string>();

        /// <summary>
        /// SendAsync で送信されたメッセージの一覧。
        /// </summary>
        public IReadOnlyList<string> SentMessages => _sentMessages;

        /// <summary>
        /// メッセージ受信ハンドラ。
        /// </summary>
        public Action<string> OnReceived { get; set; }

        /// <summary>
        /// SendAsync が呼ばれたときにスローする例外（null なら正常動作）。
        /// </summary>
        public Exception? SendException { get; set; }

        public Task SendAsync(string payload)
        {
            if (SendException != null)
                return Task.FromException(SendException);

            _sentMessages.Add(payload);
            return Task.FromResult(0);
        }

        /// <summary>
        /// 受信をシミュレートする。OnReceived イベントを発火させる。
        /// </summary>
        public void SimulateReceive(string json)
        {
            OnReceived?.Invoke(json);
        }

        public void Dispose()
        {
        }
    }
}
