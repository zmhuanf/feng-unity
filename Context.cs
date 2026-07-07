using System.Collections.Concurrent;

public interface IClientContext
{
    public Client Client { get; }
    public void Set(string key, object value);
    public bool Get<T>(string key, out T value);
}

public class ClientContext : IClientContext
{
    public Client Client { get; private set; }
    private readonly ConcurrentDictionary<string, object> _data = new();

    public ClientContext(Client client)
    {
        Client = client;
    }

    public void Set(string key, object value)
    {
        _data[key] = value;
    }

    public bool Get<T>(string key, out T value)
    {
        if (_data.TryGetValue(key, out var obj) && obj is T t)
        {
            value = t;
            return true;
        }
        value = default;
        return false;
    }
}
