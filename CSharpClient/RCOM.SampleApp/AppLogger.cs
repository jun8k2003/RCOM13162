using System;
using System.IO;

namespace RCOM.SampleApp
{
    /// <summary>
    /// 画面表示とファイル出力の両方にログを書き出すロガー。
    /// </summary>
    public sealed class AppLogger : IDisposable
    {
        private readonly StreamWriter _writer;
        private readonly Action<string> _uiCallback;
        private readonly object _lock = new object();

        /// <summary>
        /// ログファイルのフルパス。
        /// </summary>
        public string LogFilePath { get; }

        /// <param name="uiCallback">UI にログ行を追加するコールバック（Dispatcher 経由を想定）</param>
        public AppLogger(Action<string> uiCallback)
        {
            _uiCallback = uiCallback;

            var instanceId = Guid.NewGuid().ToString().Substring(0, 7);
            var fileName = string.Format("{0}_{1}.log", DateTime.Now.ToString("yyyyMMdd"), instanceId);
            LogFilePath = Path.Combine(Path.GetTempPath(), fileName);

            _writer = new StreamWriter(LogFilePath, append: true) { AutoFlush = true };
        }

        /// <summary>
        /// ログを出力する。
        /// </summary>
        /// <param name="category">ログ種別（CONN, CALL, RECV, RESP, NOTIFY, ERR）</param>
        /// <param name="message">メッセージ</param>
        public void Log(string category, string message)
        {
            var line = string.Format("{0} [{1}] {2}",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                category,
                message);

            lock (_lock)
            {
                _writer.WriteLine(line);
            }

            _uiCallback?.Invoke(line);
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _writer?.Dispose();
            }
        }
    }
}
