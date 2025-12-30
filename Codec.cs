using System.Text;

public interface ICodec
{
    string Marshal(object value);
    T Unmarshal<T>(string value);
}

public class NewtonsoftJsonCodec : ICodec
{
    public string Marshal(object value)
    {
        // 特殊处理字符串和字节数组
        if (value == null)
        {
            return "";
        }
        else if (value is string s)
        {
            return s;
        }
        else if (value is byte[] b)
        {
            return Encoding.UTF8.GetString(b);
        }
        else
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(value);
        }
    }

    public T Unmarshal<T>(string value)
    {
        return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(value);
    }
}