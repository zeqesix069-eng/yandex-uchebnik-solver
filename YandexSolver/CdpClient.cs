using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace YandexSolver
{
    public sealed class CdpClient : IDisposable
    {
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
        private ClientWebSocket _socket;
        private int _nextId;

        public bool IsConnected
        {
            get { return _socket != null && _socket.State == WebSocketState.Open; }
        }

        public static string FindTargetWebSocketUrl(int port)
        {
            string raw = NativeSolverBridge.HttpGet(string.Format("http://127.0.0.1:{0}/json", port));
            var serializer = new JavaScriptSerializer();
            object[] targets = serializer.Deserialize<object[]>(raw);
            if (targets == null || targets.Length == 0)
            {
                throw new InvalidOperationException("В браузере не найдено ни одной вкладки.");
            }

            string fallbackWs = null;
            foreach (object item in targets)
            {
                var dict = item as Dictionary<string, object>;
                if (dict == null)
                {
                    continue;
                }

                string type = dict.ContainsKey("type") ? Convert.ToString(dict["type"]) : string.Empty;
                if (!string.Equals(type, "page", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string url = dict.ContainsKey("url") ? Convert.ToString(dict["url"]) : string.Empty;
                string ws = dict.ContainsKey("webSocketDebuggerUrl") ? Convert.ToString(dict["webSocketDebuggerUrl"]) : string.Empty;
                if (string.IsNullOrEmpty(ws))
                {
                    continue;
                }

                if (url.IndexOf("education.yandex.ru", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return ws;
                }

                if (fallbackWs == null)
                {
                    fallbackWs = ws;
                }
            }

            if (fallbackWs == null)
            {
                throw new InvalidOperationException("Не найдена доступная вкладка CDP.");
            }

            return fallbackWs;
        }

        public async Task ConnectAsync(string wsUrl, CancellationToken ct)
        {
            Disconnect();
            _socket = new ClientWebSocket();
            await _socket.ConnectAsync(new Uri(wsUrl), ct).ConfigureAwait(false);
        }

        public void Disconnect()
        {
            if (_socket != null)
            {
                try
                {
                    _socket.Dispose();
                }
                catch
                {
                }
                _socket = null;
            }
        }

        public async Task<object> EvaluateAsync(string expression, CancellationToken ct)
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Нет подключения к браузеру.");
            }

            int id = Interlocked.Increment(ref _nextId);
            var payload = new Dictionary<string, object>
            {
                { "id", id },
                { "method", "Runtime.evaluate" },
                {
                    "params", new Dictionary<string, object>
                    {
                        { "expression", expression },
                        { "returnByValue", true },
                        { "awaitPromise", true }
                    }
                }
            };

            string reqText = _json.Serialize(payload);
            byte[] reqBytes = Encoding.UTF8.GetBytes(reqText);
            await _socket.SendAsync(new ArraySegment<byte>(reqBytes), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);

            while (true)
            {
                string msg = await ReceiveTextAsync(ct).ConfigureAwait(false);
                var doc = _json.Deserialize<Dictionary<string, object>>(msg);
                if (doc == null || !doc.ContainsKey("id"))
                {
                    continue;
                }

                if (Convert.ToInt32(doc["id"]) != id)
                {
                    continue;
                }

                if (doc.ContainsKey("error"))
                {
                    throw new InvalidOperationException("CDP error: " + _json.Serialize(doc["error"]));
                }

                var resultWrapper = doc.ContainsKey("result") ? doc["result"] as Dictionary<string, object> : null;
                if (resultWrapper == null)
                {
                    return null;
                }

                if (resultWrapper.ContainsKey("exceptionDetails"))
                {
                    throw new InvalidOperationException("JS error: " + _json.Serialize(resultWrapper["exceptionDetails"]));
                }

                var inner = resultWrapper.ContainsKey("result") ? resultWrapper["result"] as Dictionary<string, object> : null;
                if (inner == null || !inner.ContainsKey("value"))
                {
                    return null;
                }

                return inner["value"];
            }
        }

        private async Task<string> ReceiveTextAsync(CancellationToken ct)
        {
            var buffer = new byte[16384];
            using (var ms = new MemoryStream())
            {
                while (true)
                {
                    WebSocketReceiveResult res = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                    if (res.MessageType == WebSocketMessageType.Close)
                    {
                        throw new InvalidOperationException("Соединение с браузером закрыто.");
                    }
                    ms.Write(buffer, 0, res.Count);
                    if (res.EndOfMessage)
                    {
                        break;
                    }
                }
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
