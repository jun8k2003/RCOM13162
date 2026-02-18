using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RCOM.Channel;
using RCOM.Rpc;

namespace RCOM.SampleApp
{
    public partial class MainWindow : Window
    {
        private RemotePeer _peer;
        private AppLogger _logger;

        public MainWindow()
        {
            InitializeComponent();

            _logger = new AppLogger(line =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    LstLog.Items.Add(line);
                    LstLog.ScrollIntoView(line);
                }));
            });

            _logger.Log("INFO", string.Format("ログファイル: {0}", _logger.LogFilePath));
        }

        // ──────────────────────────────
        // トランスポート切り替え
        // ──────────────────────────────

        private void RbTransport_Checked(object sender, RoutedEventArgs e)
        {
            if (PnlGrpc == null || PnlIpc == null) return;

            if (RbGrpc.IsChecked == true)
            {
                PnlGrpc.Visibility = Visibility.Visible;
                PnlIpc.Visibility = Visibility.Collapsed;
            }
            else
            {
                PnlGrpc.Visibility = Visibility.Collapsed;
                PnlIpc.Visibility = Visibility.Visible;
            }
        }

        // ──────────────────────────────
        // 接続 / 切断
        // ──────────────────────────────

        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            SetStatus("接続中...");
            BtnConnect.IsEnabled = false;

            try
            {
                IRoomChannel channel;

                if (RbGrpc.IsChecked == true)
                {
                    channel = await ConnectGrpcAsync();
                }
                else
                {
                    channel = await ConnectIpcAsync();
                }

                _peer = new RemotePeer(channel);
                _peer.OnRequest = HandleRequest;
                _peer.OnNotify = HandleNotify;
                _peer.OnPeerLeave = HandlePeerLeave;

                SetStatus("接続済");
                BtnDisconnect.IsEnabled = true;
                BtnCall.IsEnabled = true;
                BtnNotify.IsEnabled = true;
                _logger.Log("CONN", "接続完了");
            }
            catch (Exception ex)
            {
                SetStatus("エラー");
                BtnConnect.IsEnabled = true;
                _logger.Log("ERR", string.Format("接続失敗: {0}", ex.Message));
                MessageBox.Show(ex.Message, "接続エラー");
            }
        }

        private async Task<IRoomChannel> ConnectGrpcAsync()
        {
            var url = TxtUrl.Text.Trim();
            var matchingKey = TxtMatchingKey.Text.Trim();

            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(matchingKey))
                throw new InvalidOperationException("URL とマッチングキーを入力してください。");

            string host;
            int port;
            try
            {
                var parts = url.Split(':');
                host = parts[0];
                port = int.Parse(parts[1]);
            }
            catch
            {
                throw new InvalidOperationException("URL は host:port 形式で入力してください。");
            }

            var useTls = ChkTls.IsChecked == true;
            _logger.Log("CONN", string.Format("gRPC 接続開始 host={0} port={1} key={2} tls={3}", host, port, matchingKey, useTls));
            return await GrpcRoomChannel.CreateAsync(matchingKey, host, port, useTls: useTls);
        }

        private async Task<IRoomChannel> ConnectIpcAsync()
        {
            var pipeName = TxtPipeName.Text.Trim();

            if (string.IsNullOrEmpty(pipeName))
                throw new InvalidOperationException("パイプ名を入力してください。");

            _logger.Log("CONN", string.Format("IPC Adaptive Establishment 開始 pipe={0}", pipeName));
            return await IpcRoomChannel.CreateAdaptiveAsync(pipeName);
        }

        private void BtnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            Disconnect();
        }

        private void Disconnect()
        {
            if (_peer != null)
            {
                _peer.Dispose();
                _peer = null;
                _logger.Log("CONN", "切断");
            }

            SetStatus("未接続");
            BtnConnect.IsEnabled = true;
            BtnDisconnect.IsEnabled = false;
            BtnCall.IsEnabled = false;
            BtnNotify.IsEnabled = false;
        }

        private void SetStatus(string status)
        {
            TxtStatus.Text = status;
        }

        // ──────────────────────────────
        // CallAsync 送信
        // ──────────────────────────────

        private async void BtnCall_Click(object sender, RoutedEventArgs e)
        {
            var method = TxtCallMethod.Text.Trim();
            if (string.IsNullOrEmpty(method))
            {
                MessageBox.Show("メソッド名を入力してください。", "入力エラー");
                return;
            }

            object @params = null;
            var paramsText = TxtCallParams.Text.Trim();
            if (!string.IsNullOrEmpty(paramsText))
            {
                try
                {
                    @params = JToken.Parse(paramsText);
                }
                catch
                {
                    MessageBox.Show("パラメータが正しい JSON ではありません。", "入力エラー");
                    return;
                }
            }

            BtnCall.IsEnabled = false;
            TxtCallResult.Text = "";
            TxtCallElapsed.Text = "所要時間: --- ms";

            _logger.Log("CALL", string.Format("CallAsync \"{0}\" 送信 params={1}", method, paramsText));

            var sw = Stopwatch.StartNew();
            try
            {
                var response = await _peer.CallAsync(method, @params);
                sw.Stop();

                var resultJson = response.Result != null
                    ? response.Result.ToString(Formatting.None)
                    : "(null)";

                TxtCallResult.Text = resultJson;
                TxtCallElapsed.Text = string.Format("所要時間: {0} ms", sw.ElapsedMilliseconds);
                _logger.Log("CALL", string.Format("CallAsync \"{0}\" 応答: {1} ({2}ms)", method, resultJson, sw.ElapsedMilliseconds));
            }
            catch (RpcException ex)
            {
                sw.Stop();
                var errorMsg = string.Format("[{0}] {1}", ex.RpcError.Code, ex.RpcError.Message);
                TxtCallResult.Text = string.Format("ERROR: {0}", errorMsg);
                TxtCallElapsed.Text = string.Format("所要時間: {0} ms", sw.ElapsedMilliseconds);
                _logger.Log("ERR", string.Format("CallAsync \"{0}\" エラー: {1} ({2}ms)", method, errorMsg, sw.ElapsedMilliseconds));
            }
            catch (TimeoutException)
            {
                sw.Stop();
                TxtCallResult.Text = "ERROR: タイムアウト";
                TxtCallElapsed.Text = string.Format("所要時間: {0} ms", sw.ElapsedMilliseconds);
                _logger.Log("ERR", string.Format("CallAsync \"{0}\" タイムアウト ({1}ms)", method, sw.ElapsedMilliseconds));
            }
            catch (Exception ex)
            {
                sw.Stop();
                TxtCallResult.Text = string.Format("ERROR: {0}", ex.Message);
                TxtCallElapsed.Text = string.Format("所要時間: {0} ms", sw.ElapsedMilliseconds);
                _logger.Log("ERR", string.Format("CallAsync \"{0}\" 例外: {1} ({2}ms)", method, ex.Message, sw.ElapsedMilliseconds));
            }
            finally
            {
                BtnCall.IsEnabled = true;
            }
        }

        // ──────────────────────────────
        // OnRequest 受信ハンドラ
        // ──────────────────────────────

        private Task<object> HandleRequest(string method, JToken @params)
        {
            var paramsStr = @params != null ? @params.ToString(Formatting.None) : "(null)";

            // UI スレッドでログ出力
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _logger.Log("RECV", string.Format("OnRequest \"{0}\" params={1}", method, paramsStr));
            }));

            // UI の値を取得（UI スレッドから読む必要がある）
            string waitMethod = null;
            bool isSuccess = true;
            string responseMessage = null;

            Dispatcher.Invoke(new Action(() =>
            {
                waitMethod = TxtWaitMethod.Text.Trim();
                isSuccess = RbSuccess.IsChecked == true;
                responseMessage = TxtResponseMessage.Text;
            }));

            if (!string.IsNullOrEmpty(waitMethod) && method == waitMethod)
            {
                if (isSuccess)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _logger.Log("RESP", string.Format("OnRequest \"{0}\" → Success: {1}", method, responseMessage));
                    }));
                    return Task.FromResult<object>(new { message = responseMessage });
                }
                else
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _logger.Log("RESP", string.Format("OnRequest \"{0}\" → Error: {1}", method, responseMessage));
                    }));
                    throw new RpcException(-32000, responseMessage);
                }
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                _logger.Log("RESP", string.Format("OnRequest \"{0}\" → Method not found", method));
            }));
            throw new RpcException(-32601, "Method not found");
        }

        // ──────────────────────────────
        // NotifyAsync 送信
        // ──────────────────────────────

        private async void BtnNotify_Click(object sender, RoutedEventArgs e)
        {
            var method = TxtNotifyMethod.Text.Trim();
            if (string.IsNullOrEmpty(method))
            {
                MessageBox.Show("メソッド名を入力してください。", "入力エラー");
                return;
            }

            object @params = null;
            var paramsText = TxtNotifyParams.Text.Trim();
            if (!string.IsNullOrEmpty(paramsText))
            {
                try
                {
                    @params = JToken.Parse(paramsText);
                }
                catch
                {
                    MessageBox.Show("パラメータが正しい JSON ではありません。", "入力エラー");
                    return;
                }
            }

            try
            {
                await _peer.NotifyAsync(method, @params);
                _logger.Log("NOTIFY", string.Format("NotifyAsync \"{0}\" 送信: {1}", method, paramsText));
            }
            catch (Exception ex)
            {
                _logger.Log("ERR", string.Format("NotifyAsync \"{0}\" 失敗: {1}", method, ex.Message));
            }
        }

        // ──────────────────────────────
        // OnNotify 受信ハンドラ
        // ──────────────────────────────

        private void HandleNotify(string method, JToken @params)
        {
            var paramsStr = @params != null ? @params.ToString(Formatting.None) : "(null)";

            Dispatcher.BeginInvoke(new Action(() =>
            {
                var display = string.Format("\"{0}\"  {1}", method, paramsStr);
                LstNotifyReceived.Items.Add(display);
                LstNotifyReceived.ScrollIntoView(display);
                _logger.Log("NOTIFY", string.Format("OnNotify \"{0}\" 受信: {1}", method, paramsStr));
            }));
        }

        // ──────────────────────────────
        // OnPeerLeave 受信ハンドラ
        // ──────────────────────────────

        private void HandlePeerLeave()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _logger.Log("LEAVE", "相手が切断しました");
                Disconnect();
            }));
        }

        // ──────────────────────────────
        // ウィンドウ終了
        // ──────────────────────────────

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            Disconnect();
            _logger?.Dispose();
        }
    }
}
