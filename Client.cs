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
    public Config Config { get; } = new();

    private readonly IClientContext _ctx;
    private readonly ConcurrentDictionary<string, PendingRequest> _pending = new();
    private readonly ConcurrentDictionary<string, PendingRequest> _pendingSys = new();
    private readonly ConcurrentDictionary<string, Func<IClientContext, string, object>> _handlers = new();
    private readonly ConcurrentDictionary<string, Func<IClientContext, string, object>> _handlersSys = new();
    private readonly List<Middleware> _middlewares = new();
    private readonly List<Middleware> _middlewaresSys = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly SemaphoreSlim _sendSysLock = new(1, 1);

    private ClientWebSocket _conn;
    private ClientWebSocket _connSys;
    private CancellationTokenSource _cancel = new();
    private CancellationTokenSource _cancelSys = new();

    public Client()
    {
        _ctx = new ClientContext(this);
    }

    public Task Connect(CancellationToken cancellationToken = default)
    {
        var addr = $"{Config.Addr}:{Config.Port}";
        var needNew = !Config.DirectConnect;
        if (Config.Mode == ClientMode.Server)
        {
            needNew = false;
        }
        return Connect(addr, needNew, cancellationToken);
    }

    private async Task Connect(string addr, bool needNew, CancellationToken cancellationToken)
    {
        var proto = Config.EnableTls ? "wss" : "ws";
        if (Config.Mode == ClientMode.Client)
        {
            await ConnectSystem($"{proto}://{addr}/system", cancellationToken);

            var serverAddr = string.Empty;
            await Request("/get_low_load_server_addr", needNew, (IClientContext _, string data) =>
            {
                serverAddr = data;
            }, true, cancellationToken);

            if (string.IsNullOrEmpty(serverAddr))
            {
                await ConnectUser($"{proto}://{addr}/game", cancellationToken);
                return;
            }

            await Connect(serverAddr, false, cancellationToken);
            return;
        }

        if (Config.Mode == ClientMode.Server)
        {
            await ConnectSystem($"{proto}://{addr}/system", cancellationToken);
            return;
        }

        throw new InvalidOperationException($"unknown client mode: {Config.Mode}");
    }

    private async Task ConnectSystem(string url, CancellationToken cancellationToken)
    {
        _cancelSys?.Cancel();
        _connSys?.Dispose();
        _cancelSys = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _connSys = new ClientWebSocket();
        await _connSys.ConnectAsync(new Uri(url), _cancelSys.Token);
        _ = ReadLoop(true);
    }

    private async Task ConnectUser(string url, CancellationToken cancellationToken)
    {
        _cancel?.Cancel();
        _conn?.Dispose();
        _cancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _conn = new ClientWebSocket();
        await _conn.ConnectAsync(new Uri(url), _cancel.Token);
        _ = ReadLoop(false);
    }

    public void Use(Middleware middleware)
    {
        Use(middleware, false);
    }

    private void Use(Middleware middleware, bool isSys)
    {
        var middlewares = isSys ? _middlewaresSys : _middlewares;
        lock (middlewares)
        {
            middlewares.Add(middleware);
        }
    }

    public void Use<T>(Middleware<T> middleware)
    {
        Use(new Middleware(middleware.Route, middleware.Handler), false);
    }

    public void Handle(string route, Action<IClientContext> handler)
    {
        Handle(route, (ctx, _) =>
        {
            handler(ctx);
            return null;
        }, false);
    }

    public void Handle<T>(string route, Action<IClientContext, T> handler)
    {
        Handle(route, (ctx, data) =>
        {
            handler(ctx, Decode<T>(Config.Codec, data));
            return null;
        }, false);
    }

    public void Handle(string route, Func<IClientContext, object> handler)
    {
        Handle(route, (ctx, _) => handler(ctx), false);
    }

    public void Handle<T>(string route, Func<IClientContext, T, object> handler)
    {
        Handle(route, (ctx, data) => handler(ctx, Decode<T>(Config.Codec, data)), false);
    }

    private void Handle(string route, Func<IClientContext, string, object> handler, bool isSys)
    {
        var handlers = isSys ? _handlersSys : _handlers;
        handlers[route] = handler;
    }

    public Task Push(string route)
    {
        return Push(route, string.Empty);
    }

    public Task Push(string route, object data)
    {
        return Send(new Message
        {
            route = route,
            id = Guid.NewGuid().ToString(),
            type = MessageType.Push,
            data = Config.Codec.Marshal(data)
        }, false);
    }

    public Task Request<T>(string route, Action<IClientContext, T> callback, CancellationToken cancellationToken = default)
    {
        return Request(route, string.Empty, callback, false, cancellationToken);
    }

    public Task Request<T>(string route, object data, Action<IClientContext, T> callback, CancellationToken cancellationToken = default)
    {
        return Request(route, data, callback, false, cancellationToken);
    }

    private Task Request<T>(string route, object data, Action<IClientContext, T> callback, bool isSys, CancellationToken cancellationToken)
    {
        return Request(route, data, (ctx, payload) => callback(ctx, Decode<T>(Config.Codec, payload)), isSys, cancellationToken);
    }

    public Task Request(string route, Action<IClientContext> callback, CancellationToken cancellationToken = default)
    {
        return Request(route, string.Empty, callback, cancellationToken);
    }

    public Task Request(string route, object data, Action<IClientContext> callback, CancellationToken cancellationToken = default)
    {
        return Request(route, data, (ctx, _) => callback(ctx), false, cancellationToken);
    }

    private async Task Request(string route, object data, Action<IClientContext, string> callback, bool isSys, CancellationToken cancellationToken)
    {
        var pending = isSys ? _pendingSys : _pending;
        var req = new Message
        {
            route = route,
            id = Guid.NewGuid().ToString(),
            type = MessageType.Request,
            data = Config.Codec.Marshal(data)
        };

        var item = new PendingRequest(callback);
        pending[req.id] = item;

        try
        {
            await Send(req, isSys);
            await item.Wait(Config.Timeout, cancellationToken);
        }
        finally
        {
            pending.TryRemove(req.id, out _);
        }
    }

    private async Task Send(Message message, bool isSys)
    {
        var conn = isSys ? _connSys : _conn;
        var cancel = isSys ? _cancelSys : _cancel;
        var sendLock = isSys ? _sendSysLock : _sendLock;

        if (conn == null || conn.State != WebSocketState.Open)
        {
            throw new InvalidOperationException("connection is not open");
        }

        var payload = Encoding.UTF8.GetBytes(Config.Codec.Marshal(message));
        await sendLock.WaitAsync(cancel.Token);
        try
        {
            await conn.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, true, cancel.Token);
        }
        finally
        {
            sendLock.Release();
        }
    }

    private async Task ReadLoop(bool isSys)
    {
        var conn = isSys ? _connSys : _conn;
        var cancel = isSys ? _cancelSys : _cancel;
        var buffer = new byte[Config.BufferSize];

        while (conn.State == WebSocketState.Open && !cancel.IsCancellationRequested)
        {
            try
            {
                var message = await ReceiveText(conn, buffer, cancel.Token);
                if (message == null)
                {
                    break;
                }
                await Dispatch(Config.Codec.Unmarshal<Message>(message), isSys);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Config.Logger.Error($"Error occurred while processing the message: {ex.Message}");
            }
        }
    }

    private static async Task<string> ReceiveText(ClientWebSocket conn, byte[] buffer, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await conn.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await conn.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, cancellationToken);
                return null;
            }
            stream.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private async Task Dispatch(Message req, bool isSys)
    {
        switch (req.type)
        {
            case MessageType.PushBack:
                return;
            case MessageType.RequestBack:
                ResolvePending(req, isSys);
                return;
            case MessageType.Push:
            case MessageType.Request:
                await HandleIncoming(req, isSys);
                return;
            default:
                throw new InvalidOperationException($"unknown message type: {req.type}");
        }
    }

    private void ResolvePending(Message req, bool isSys)
    {
        var pending = isSys ? _pendingSys : _pending;
        if (!pending.TryRemove(req.id, out var item))
        {
            return;
        }

        if (!req.success)
        {
            item.Fail(new Exception(req.data));
            return;
        }

        try
        {
            item.Callback?.Invoke(_ctx, req.data);
            item.Succeed();
        }
        catch (Exception ex)
        {
            item.Fail(ex);
        }
    }

    private async Task HandleIncoming(Message req, bool isSys)
    {
        var resType = req.type == MessageType.Request ? MessageType.RequestBack : MessageType.PushBack;
        var res = new Message
        {
            id = req.id,
            type = resType,
            data = string.Empty,
            success = true
        };

        try
        {
            foreach (var middleware in SnapshotMiddlewares(isSys))
            {
                if (middleware.Match(req.route))
                {
                    middleware.Handler(_ctx, req.data);
                }
            }

            var handlers = isSys ? _handlersSys : _handlers;
            if (!handlers.TryGetValue(req.route, out var handler))
            {
                throw new Exception($"route not found: {req.route}");
            }

            var result = handler(_ctx, req.data);
            if (result != null)
            {
                res.data = Config.Codec.Marshal(result);
            }
        }
        catch (Exception ex)
        {
            res.success = false;
            res.data = ex.Message;
        }

        await Send(res, isSys);
    }

    private List<Middleware> SnapshotMiddlewares(bool isSys)
    {
        var middlewares = isSys ? _middlewaresSys : _middlewares;
        lock (middlewares)
        {
            // 复制后再执行用户代码，避免注册中间件时和消息处理互相阻塞。
            return new List<Middleware>(middlewares);
        }
    }

    public async Task Close()
    {
        await CloseSocket(_conn, _cancel);
        await CloseSocket(_connSys, _cancelSys);
        _pending.Clear();
        _pendingSys.Clear();
    }

    private static async Task CloseSocket(ClientWebSocket conn, CancellationTokenSource cancel)
    {
        if (conn == null)
        {
            return;
        }

        cancel.Cancel();
        if (conn.State == WebSocketState.Open)
        {
            await conn.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
        }
        conn.Dispose();
    }

    public bool IsConnected()
    {
        var userConnected = _conn != null && _conn.State == WebSocketState.Open;
        var systemConnected = _connSys != null && _connSys.State == WebSocketState.Open;
        return Config.Mode == ClientMode.Server ? systemConnected : userConnected && systemConnected;
    }

    public static T Decode<T>(ICodec codec, string data)
    {
        return typeof(T) switch
        {
            Type t when t == typeof(string) => (T)(object)data,
            Type t when t == typeof(byte[]) => (T)(object)Encoding.UTF8.GetBytes(data),
            _ => codec.Unmarshal<T>(data)
        };
    }

    private class PendingRequest
    {
        public Action<IClientContext, string> Callback { get; }
        private readonly TaskCompletionSource<object> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public PendingRequest(Action<IClientContext, string> callback)
        {
            Callback = callback;
        }

        public Task Wait(TimeSpan timeout, CancellationToken cancellationToken)
        {
            return WaitInternal(timeout, cancellationToken);
        }

        public void Succeed()
        {
            _tcs.TrySetResult(null);
        }

        public void Fail(Exception ex)
        {
            _tcs.TrySetException(ex);
        }

        private async Task WaitInternal(TimeSpan timeout, CancellationToken cancellationToken)
        {
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            var delayTask = Task.Delay(Timeout.InfiniteTimeSpan, linkedCts.Token);
            var completed = await Task.WhenAny(_tcs.Task, delayTask);
            if (completed == _tcs.Task)
            {
                await _tcs.Task;
                return;
            }

            if (timeoutCts.IsCancellationRequested)
            {
                throw new TimeoutException("request timeout");
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
