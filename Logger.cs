public interface ILogger
{
    void Debug(string message);
    void Info(string message);
    void Warn(string message);
    void Error(string message);
}

public class UnityLogger : ILogger
{
    public void Debug(string message)
    {
        UnityEngine.Debug.Log("[DEBUG] " + message);
    }

    public void Info(string message)
    {
        UnityEngine.Debug.Log("[INFO] " + message);
    }

    public void Warn(string message)
    {
        UnityEngine.Debug.LogWarning("[WARN] " + message);
    }

    public void Error(string message)
    {
        UnityEngine.Debug.LogError("[ERROR] " + message);
    }
}
