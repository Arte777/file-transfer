using System;
using System.Diagnostics;
using System.Net.Security;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FileTransfer
{
    public class StreamClient
    {
        private readonly string _serverUrl;
        private readonly string _computerName;
        private readonly string _operatorName;
        private CancellationTokenSource _cts = new CancellationTokenSource();
        private Task? _loop;

        public StreamClient(string serverUrl, string computerName, string operatorName)
        {
            _serverUrl = serverUrl;
            _computerName = computerName;
            _operatorName = operatorName;
        }

        public void Start()
        {
            if (_loop != null) return;
            _cts = new CancellationTokenSource();
            _loop = RunLoopAsync();
        }

        public void Stop()
        {
            _cts.Cancel();
        }

        private async Task RunLoopAsync()
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    using var ws = new ClientWebSocket();
                    ws.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
                    var wsUrl = _serverUrl.Replace("https://", "wss://").Replace("http://", "ws://") + "/ws";
                    var uri = new Uri(wsUrl);
                MainWindow.Log("StreamClient connecting to " + uri);
                await ws.ConnectAsync(uri, _cts.Token);
                MainWindow.Log("StreamClient connected");

                // Отправляем идентификатор оператора + имя компьютера
                var idBytes = Encoding.UTF8.GetBytes($"{_operatorName}|{_computerName}");
                await ws.SendAsync(new ArraySegment<byte>(idBytes), WebSocketMessageType.Text, true, _cts.Token);
                MainWindow.Log("StreamClient sent id: " + _operatorName + "|" + _computerName);

                // Шлём кадры
                while (ws.State == WebSocketState.Open && !_cts.Token.IsCancellationRequested)
                {
                    byte[] frame = await Task.Run(() => MainWindow.CaptureDesktopJpeg(50, 40), _cts.Token);
                    if (frame.Length > 0)
                    {
                        await ws.SendAsync(new ArraySegment<byte>(frame), WebSocketMessageType.Binary, true, _cts.Token);
                    }
                    await Task.Delay(100, _cts.Token);
                }
                MainWindow.Log("StreamClient loop ended, state: " + ws.State);
            }
            catch (OperationCanceledException)
            {
                MainWindow.Log("StreamClient cancelled");
                break;
            }
            catch (Exception ex)
            {
                MainWindow.Log("StreamClient error: " + ex);
                Debug.WriteLine("StreamClient error: " + ex.Message);
                try { await Task.Delay(3000, _cts.Token); } catch (OperationCanceledException) { break; }
            }
            }
        }
    }
}
