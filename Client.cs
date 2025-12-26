using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public Config Config = new();
    private ClientWebSocket _conn = new(), _connSys = new();
    private CancellationTokenSource _cancel = new(), _cancelSys = new();
    private ConcurrentDictionary<string, Action<IContext, string, bool>> _callbacks = new(), _callbacksSys = new();
    private List<Middleware<object>> _middlewares = new(), _middlewaresSys = new();
    private ConcurrentDictionary<string, Func<IContext, string, object>> _handlers = new(), _handlersSys = new();

    public async Task Connect()
    {
        await connect($"{Config.Addr}:{Config.Port}", true);
    }

    private async Task connect(string ipp, bool needNew)
    {
        await connectSys(ipp);
        string serverIpp = "";
        await request<string>("/get_low_load_server_addr", needNew, (ctx, addr) =>
        {
            serverIpp = addr;
        }, true);
        if (string.IsNullOrEmpty(serverIpp))
        {
            // 没有新的地址，直接连接
            await connectUser(ipp);
            return;
        }
        await connect(serverIpp, false);
    }

    private async Task connectSys(string ipp)
    {
        var uri = new Uri($"ws{(Config.EnableTls ? "s" : "")}://{ipp}/system");
        await _connSys.ConnectAsync(uri, _cancelSys.Token);
        handle(true);
    }

    private async Task connectUser(string ipp)
    {
        var uri = new Uri($"ws{(Config.EnableTls ? "s" : "")}://{ipp}/game");
        await _conn.ConnectAsync(uri, _cancel.Token);
        handle(false);
    }

    public async Task Request<T>(string route, object data, Action<IContext, T> callback)
    {
        await request<T>(route, data, callback, false);
    }

    private async Task request<T>(string route, object data, Action<IContext, T> callback, bool isSys)
    {
        var dic = isSys ? _callbacksSys : _callbacks;
        var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var req = new Message
        {
            route = route,
            id = Guid.NewGuid().ToString(),
            type = MessageType.Request,
            data = Marshal(data)
        };
        void wrappedCallback(IContext ctx, string data, bool success)
        {
            if (success)
            {
                T callData = typeof(T) switch
                {
                    Type t when t == typeof(string) => (T)(object)data,
                    Type t when t == typeof(byte[]) => (T)(object)Encoding.UTF8.GetBytes(data),
                    _ => Config.Codec.Unmarshal<T>(data)
                };
                callback(ctx, callData);
                tcs.SetResult(null);
                return;
            }
            throw new Exception(data);
        }
        dic.TryAdd(req.id, wrappedCallback);
        await send(req, isSys);
        // 超时
        var delayTask = Task.Delay(Config.Timeout);
        var completedTask = await Task.WhenAny(tcs.Task, delayTask);
        if (completedTask == delayTask)
        {
            // 清理回调
            dic.TryRemove(req.id, out _);
            throw new TimeoutException($"request to {route} timed out");
        }
        // 正常完成
        await tcs.Task;
    }

    public async Task Request(string route, object data, Action<IContext> callback)
    {
        await request(route, data, callback, false);
    }

    private async Task request(string route, object data, Action<IContext> callback, bool isSys)
    {
        var dic = isSys ? _callbacksSys : _callbacks;
        var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var req = new Message
        {
            route = route,
            id = Guid.NewGuid().ToString(),
            type = MessageType.Push,
            data = Marshal(data)
        };
        void wrappedCallback(IContext context, string data, bool success)
        {
            if (success)
            {
                callback(context);
                tcs.SetResult(null);
                return;
            }
            throw new Exception(data);
        }
        dic.TryAdd(req.id, wrappedCallback);
        await send(req, isSys);
        // 超时
        var delayTask = Task.Delay(Config.Timeout);
        var completedTask = await Task.WhenAny(tcs.Task, delayTask);
        if (completedTask == delayTask)
        {
            // 清理回调
            dic.TryRemove(req.id, out _);
            throw new TimeoutException($"request to {route} timed out");
        }
        // 正常完成
        await tcs.Task;
    }

    public void AddMiddleware(Middleware<object> middleware)
    {
        addMiddleware(middleware, false);
    }

    private void addMiddleware(Middleware<object> middleware, bool isSys)
    {
        var dic = isSys ? _middlewaresSys : _middlewares;
        dic.Add(middleware);
    }

    public void AddHandler(string route, Action<IContext> handler)
    {
        addHandler(route, handler, false);
    }

    private void addHandler(string route, Action<IContext> handler, bool isSys)
    {
        var dic = isSys ? _handlersSys : _handlers;
        object wrappedHandler(IContext ctx, string data)
        {
            handler(ctx);
            return null;
        }
        dic.TryAdd(route, wrappedHandler);
    }

    public void AddHandler<T>(string route, Action<IContext, T> handler)
    {
        addHandler<T>(route, handler, false);
    }

    private void addHandler<T>(string route, Action<IContext, T> handler, bool isSys)
    {
        var dic = isSys ? _handlersSys : _handlers;
        object wrappedHandler(IContext ctx, string data)
        {
            T callData = typeof(T) switch
            {
                Type t when t == typeof(string) => (T)(object)data,
                Type t when t == typeof(byte[]) => (T)(object)Encoding.UTF8.GetBytes(data),
                _ => ctx.Client.Config.Codec.Unmarshal<T>(data)
            };
            handler(ctx, callData);
            return null;
        }
        dic.TryAdd(route, wrappedHandler);
    }

    public void AddHandler(string route, Func<IContext, object> handler)
    {
        addHandler(route, handler, false);
    }

    private void addHandler(string route, Func<IContext, object> handler, bool isSys)
    {
        var dic = isSys ? _handlersSys : _handlers;
        object wrappedHandler(IContext ctx, string data)
        {
            return handler(ctx);
        }
        dic.TryAdd(route, wrappedHandler);
    }

    public void AddHandler<T>(string route, Func<IContext, T, object> handler)
    {
        addHandler<T>(route, handler, false);
    }

    private void addHandler<T>(string route, Func<IContext, T, object> handler, bool isSys)
    {
        var dic = isSys ? _handlersSys : _handlers;
        object wrappedHandler(IContext ctx, string data)
        {
            T callData = typeof(T) switch
            {
                Type t when t == typeof(string) => (T)(object)data,
                Type t when t == typeof(byte[]) => (T)(object)Encoding.UTF8.GetBytes(data),
                _ => ctx.Client.Config.Codec.Unmarshal<T>(data)
            };
            return handler(ctx, callData);
        }
        dic.TryAdd(route, wrappedHandler);
    }

    public async Task Push(string route, object data)
    {
        await push(route, data, false);
    }

    private async Task push(string route, object data, bool isSys)
    {
        var req = new Message
        {
            route = route,
            id = Guid.NewGuid().ToString(),
            type = MessageType.Push,
            data = Marshal(data)
        };
        await send(req, isSys);
    }

    private async Task send(Message req, bool isSys)
    {
        var conn = isSys ? _connSys : _conn;
        if (conn.State != WebSocketState.Open)
        {
            throw new Exception("connection is not open");
        }
        var cancel = isSys ? _cancelSys : _cancel;
        var strReq = Config.Codec.Marshal(req);
        var bytes = Encoding.UTF8.GetBytes(strReq);
        await conn.SendAsync(new System.ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancel.Token);
    }

    private string Marshal(object data)
    {
        // 特殊处理字符串和字节数组
        string strData = "";
        if (data is string s)
        {
            strData = s;
        }
        else if (data is byte[] b)
        {
            strData = Encoding.UTF8.GetString(b);
        }
        else
        {
            strData = Config.Codec.Marshal(data);
        }
        return strData;
    }

    private async void handle(bool isSys)
    {
        var conn = isSys ? _connSys : _conn;
        var cancel = isSys ? _cancelSys : _cancel;
        var dic = isSys ? _callbacksSys : _callbacks;
        var buffer = new byte[Config.BufferSize];
        while (conn.State == WebSocketState.Open)
        {
            try
            {
                using var messageStream = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await conn.ReceiveAsync(new ArraySegment<byte>(buffer), cancel.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await conn.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, cancel.Token);
                        break;
                    }
                    messageStream.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
                // 读取的信息
                string message = Encoding.UTF8.GetString(messageStream.ToArray());
                var req = Config.Codec.Unmarshal<Message>(message);
                // 推送的回复，不处理
                if (req.type == MessageType.PushBack)
                {
                    continue;
                }
                // 请求的回复
                if (req.type == MessageType.RequestBack)
                {
                    if (dic.TryRemove(req.id, out var callback))
                    {
                        var context = new Context(this);
                        callback(context, req.data, req.success);
                    }
                    continue;
                }
                // 请求
                MessageType resType = req.type == MessageType.Request ? MessageType.RequestBack : MessageType.PushBack;
                var midDic = isSys ? _middlewaresSys : _middlewares;
                var ctx = new Context(this);
                var res = new Message
                {
                    route = "",
                    id = req.id,
                    type = resType,
                    data = "",
                    success = true
                };
                try
                {
                    // 中间件处理
                    foreach (var mid in midDic)
                    {
                        if (mid.Match(req.route))
                        {
                            mid.Handler(ctx, req.data);
                        }
                    }
                    // 路由处理
                    var handlerDic = isSys ? _handlersSys : _handlers;
                    if (handlerDic.TryGetValue(req.route, out var handler))
                    {
                        var ret = handler(ctx, req.data);
                        if (ret != null)
                        {
                            res.data = Marshal(ret);
                        }
                    }
                    else
                    {
                        throw new Exception($"no handler for route {req.route}");
                    }
                }
                catch (Exception ex)
                {
                    res.success = false;
                    res.data = ex.Message;
                }
                await send(res, isSys);
            }
            catch (Exception ex)
            {
                Config.Logger.Error($"Error occurred while processing the message: {ex.Message}");
            }
        }
    }

    public bool IsConnected()
    {
        return _conn != null && _conn.State == WebSocketState.Open && _connSys != null && _connSys.State == WebSocketState.Open;
    }
}
