namespace SwiftRide.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMQ";

    public string HostName { get; set; } = "localhost";
    public ushort Port { get; set; } = 5672;
    public string VirtualHost { get; set; } = "/";
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    // Cấu hình retry mặc định cho mọi ReceiveEndpoint
    public int RetryLimit { get; set; } = 3;
    public int MinRetryIntervalMs { get; set; } = 1000;
    public int MaxRetryIntervalMs { get; set; } = 10000;
    public int RetryIntervalDeltaMs { get; set; } = 2000;
    public ushort PrefetchCount { get; set; } = 16;
}