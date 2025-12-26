public enum RequestType
{
    Request = 0,
    Push = 1,
    RequestBack = 2,
    PushBack = 3
}

public class Request
{
    public string route;
    public string id;
    public RequestType type;
    public string data;
    public bool success;
}
