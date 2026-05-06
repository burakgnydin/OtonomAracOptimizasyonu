namespace OtonomAracOptimizasyonu.Services;

public static class SimulationLogger
{
    public static event Action<string>? LogReceived;

    public static void Log(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        Console.WriteLine(line);
        System.Diagnostics.Debug.WriteLine(line);
        LogReceived?.Invoke(line);
    }
}
