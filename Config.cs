using System;

public class Config
{
    // 服务器地址
    public string Addr;
    // 服务器端口
    public int Port;
    // 序列化方式
    public ICodec Codec;
    // 启用tls
    public bool EnableTls;
    // 缓冲大小
    public int BufferSize;
    // 超时时间
    public TimeSpan Timeout;
    // 日志
    public ILogger Logger;

    public Config()
    {
        Addr = "127.0.0.1";
        Port = 22100;
        Codec = new NewtonsoftJsonCodec();
        Timeout = TimeSpan.FromSeconds(30);
        EnableTls = false;
        BufferSize = 8192;
        Logger = new UnityLogger();
    }
}
