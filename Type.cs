public enum MessageType
{
    Request = 0,
    Push = 1,
    RequestBack = 2,
    PushBack = 3
}

public class Message
{
    public string route;
    public string id;
    public MessageType type;
    public string data;
    public bool success;
}
