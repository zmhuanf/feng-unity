using System;

public class Middleware
{
    public string Route { get; }
    public Action<IClientContext, string> Handler { get; }

    public Middleware(string route, Action<IClientContext> handler)
    {
        Route = route;
        Handler = (ctx, _) => handler(ctx);
    }

    public Middleware(string route, Action<IClientContext, string> handler)
    {
        Route = route;
        Handler = handler;
    }

    public Middleware(string route, Action<IClientContext, byte[]> handler)
    {
        Route = route;
        Handler = (ctx, data) => handler(ctx, System.Text.Encoding.UTF8.GetBytes(data));
    }

    public bool Match(string route)
    {
        // 与 Go 端保持一致：中间件按路由前缀匹配。
        return route.StartsWith(Route, StringComparison.Ordinal);
    }
}

public class Middleware<T>
{
    public string Route { get; }
    public Action<IClientContext, string> Handler { get; }

    public Middleware(string route, Action<IClientContext, T> handler)
    {
        Route = route;
        Handler = (ctx, data) => handler(ctx, Client.Decode<T>(ctx.Client.Config.Codec, data));
    }

    public bool Match(string route)
    {
        return route.StartsWith(Route, StringComparison.Ordinal);
    }
}
