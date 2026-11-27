using System;
using System.Collections;
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

        public Task<object> EvaluateAsync(string expression, CancellationToken ct)
        {
            return EvaluateAsync(expression, null, ct);
        }

        public async Task<object> EvaluateAsync(string expression, int? contextId, CancellationToken ct)
        {
            var evalParams = new Dictionary<string, object>
            {
                { "expression", expression },
                { "returnByValue", true },
                { "awaitPromise", true }
            };
            if (contextId.HasValue)
            {
                evalParams["contextId"] = contextId.Value;
            }

            Dictionary<string, object> resultWrapper = await SendCommandAsync("Runtime.evaluate", evalParams, ct).ConfigureAwait(false);
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

        public async Task<List<int>> GetChildFrameContextIdsAsync(CancellationToken ct)
        {
            var contexts = new List<int>();
            try
            {
                Dictionary<string, object> treeRes = await SendCommandAsync("Page.getFrameTree", new Dictionary<string, object>(), ct).ConfigureAwait(false);
                if (treeRes == null || !treeRes.ContainsKey("frameTree"))
                {
                    return contexts;
                }

                var frameIds = new List<string>();
                CollectChildFrameIds(treeRes["frameTree"] as Dictionary<string, object>, true, frameIds);
                foreach (string frameId in frameIds)
                {
                    try
                    {
                        var isoParams = new Dictionary<string, object>
                        {
                            { "frameId", frameId },
                            { "worldName", "ys_iso" },
                            { "grantUniveralAccess", true }
                        };
                        Dictionary<string, object> isoRes = await SendCommandAsync("Page.createIsolatedWorld", isoParams, ct).ConfigureAwait(false);
                        if (isoRes != null && isoRes.ContainsKey("executionContextId"))
                        {
                            contexts.Add(Convert.ToInt32(isoRes["executionContextId"]));
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
            return contexts;
        }

        private static void CollectChildFrameIds(Dictionary<string, object> node, bool isRoot, List<string> outIds)
        {
            if (node == null) return;
            if (!isRoot && node.ContainsKey("frame"))
            {
                var frame = node["frame"] as Dictionary<string, object>;
                if (frame != null && frame.ContainsKey("id"))
                {
                    outIds.Add(Convert.ToString(frame["id"]));
                }
            }
            if (node.ContainsKey("childFrames") && node["childFrames"] is ArrayList)
            {
                foreach (object child in (ArrayList)node["childFrames"])
                {
                    CollectChildFrameIds(child as Dictionary<string, object>, false, outIds);
                }
            }
        }

        public async Task<string> CaptureScreenshotBase64Async(CancellationToken ct)
        {
            var p = new Dictionary<string, object>
            {
                { "format", "jpeg" },
                { "quality", 75 }
            };
            Dictionary<string, object> res = await SendCommandAsync("Page.captureScreenshot", p, ct).ConfigureAwait(false);
            if (res != null && res.ContainsKey("data") && res["data"] != null)
            {
                return Convert.ToString(res["data"]);
            }
            return string.Empty;
        }

        public async Task DragAsync(int fromX, int fromY, int toX, int toY, CancellationToken ct)
        {
            await DispatchMouseAsync("mouseMoved", fromX, fromY, "none", 0, ct).ConfigureAwait(false);
            await Task.Delay(30, ct).ConfigureAwait(false);
            await DispatchMouseAsync("mousePressed", fromX, fromY, "left", 1, ct).ConfigureAwait(false);
            await Task.Delay(40, ct).ConfigureAwait(false);
            const int steps = 10;
            for (int i = 1; i <= steps; i++)
            {
                int x = fromX + (toX - fromX) * i / steps;
                int y = fromY + (toY - fromY) * i / steps;
                await DispatchMouseAsync("mouseMoved", x, y, "left", 1, ct).ConfigureAwait(false);
                await Task.Delay(25, ct).ConfigureAwait(false);
            }
            await Task.Delay(40, ct).ConfigureAwait(false);
            await DispatchMouseAsync("mouseReleased", toX, toY, "left", 1, ct).ConfigureAwait(false);
        }

        private Task DispatchMouseAsync(string type, int x, int y, string button, int clickCount, CancellationToken ct)
        {
            int buttons = (button == "left" && type != "mouseReleased") ? 1 : 0;
            var p = new Dictionary<string, object>
            {
                { "type", type },
                { "x", x },
                { "y", y },
                { "button", button },
                { "buttons", buttons },
                { "clickCount", clickCount }
            };
            return SendCommandAsync("Input.dispatchMouseEvent", p, ct);
        }

        public async Task<Dictionary<string, object>> SendCommandAsync(string method, Dictionary<string, object> parameters, CancellationToken ct)
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Нет подключения к браузеру.");
            }

            int id = Interlocked.Increment(ref _nextId);
            var payload = new Dictionary<string, object>
            {
                { "id", id },
                { "method", method },
                { "params", parameters ?? new Dictionary<string, object>() }
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

                return doc.ContainsKey("result") ? doc["result"] as Dictionary<string, object> : null;
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
