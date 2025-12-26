public interface ICodec
{
    string Marshal(object value);
    T Unmarshal<T>(string value);
}

public class NewtonsoftJsonCodec : ICodec
{
    public string Marshal(object value)
    {
        return Newtonsoft.Json.JsonConvert.SerializeObject(value);
    }

    public T Unmarshal<T>(string value)
    {
        return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(value);
    }
}