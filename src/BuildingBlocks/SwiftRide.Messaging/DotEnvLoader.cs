namespace SwiftRide.Messaging;

public static class DotEnvLoader
{
    /// <summary>
    /// Tìm và nạp file .env từ thư mục hiện tại đi ngược lên gốc solution.
    /// Không ghi đè biến môi trường đã được set sẵn bởi Docker / CI / OS.
    /// </summary>
    public static void Load()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            var envFile = Path.Combine(dir.FullName, ".env");
            if (File.Exists(envFile))
            {
                foreach (var rawLine in File.ReadAllLines(envFile))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith('#')) continue;
                    var eqIndex = line.IndexOf('=');
                    if (eqIndex <= 0) continue;

                    var key = line[..eqIndex].Trim();
                    var val = line[(eqIndex + 1)..].Trim();
                    if ((val.StartsWith('\'') && val.EndsWith('\'')) ||
                        (val.StartsWith('"') && val.EndsWith('"')))
                    {
                        val = val[1..^1];
                    }

                    if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                    {
                        Environment.SetEnvironmentVariable(key, val);
                    }
                }
                break;
            }
            dir = dir.Parent;
        }
    }
}