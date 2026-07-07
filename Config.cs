using System;

public enum ClientMode
{
    Client = 0,
    Server = 1
}

public class Config
{
    // 服务器地址。
    public string Addr = "127.0.0.1";
    // 服务器端口。
    public int Port = 22100;
    // 序列化方式。
    public ICodec Codec = new NewtonsoftJsonCodec();
    // 是否启用 TLS。
    public bool EnableTls = false;
    // 是否直接连接游戏链路。
    public bool DirectConnect = true;
    // 连接模式。
    public ClientMode Mode = ClientMode.Client;
    // 接收缓冲区大小。
    public int BufferSize = 8192;
    // 请求超时时间。
    public TimeSpan Timeout = TimeSpan.FromSeconds(30);
    // 日志记录器。
    public ILogger Logger = new UnityLogger();
}
