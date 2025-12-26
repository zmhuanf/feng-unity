using System;

public class Middleware<T>
{
    public string Route;
    public Action<IContext, string> Handler;

    public Middleware(string route, Action<IContext> handler)
    {
        Route = route;
        void wrappedHandler(IContext ctx, string data)
        {
            handler(ctx);
        }
        Handler = wrappedHandler;
    }

    public Middleware(string route, Action<IContext, T> handler)
    {
        Route = route;
        void wrappedHandler(IContext ctx, string data)
        {
            T callData = typeof(T) switch
            {
                Type t when t == typeof(string) => (T)(object)data,
                Type t when t == typeof(byte[]) => (T)(object)System.Text.Encoding.UTF8.GetBytes(data),
                _ => ctx.Client.Config.Codec.Unmarshal<T>(data)
            };
            handler(ctx, callData);
        }
        Handler = wrappedHandler;
    }

    // 判断路由是否匹配
    public bool Match(string route)
    {
        // 前缀匹配
        return route.StartsWith(Route);
    }
}
