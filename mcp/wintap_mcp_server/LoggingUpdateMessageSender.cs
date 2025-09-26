using Microsoft.Extensions.Hosting;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json.Serialization;

// Add this record/class definition
public record NotificationMessage(string Level, string Data);

public class LoggingUpdateMessageSender(IMcpServer server, Func<LoggingLevel> getMinLevel) : BackgroundService
{
    readonly Dictionary<LoggingLevel, string> _loggingLevelMap = new()
    {
        { LoggingLevel.Debug, "Debug-level message" },
        { LoggingLevel.Info, "Info-level message" },
        { LoggingLevel.Notice, "Notice-level message" },
        { LoggingLevel.Warning, "Warning-level message" },
        { LoggingLevel.Error, "Error-level message" },
        { LoggingLevel.Critical, "Critical-level message" },
        { LoggingLevel.Alert, "Alert-level message" },
        { LoggingLevel.Emergency, "Emergency-level message" }
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var newLevel = (LoggingLevel)Random.Shared.Next(_loggingLevelMap.Count);

            // Use Dictionary instead of custom type
            var message = new Dictionary<string, object>
            {
                ["level"] = newLevel.ToString().ToLower(),
                ["data"] = _loggingLevelMap[newLevel]
            };

            if (newLevel > getMinLevel())
            {
                await server.SendNotificationAsync("notifications/message", message, cancellationToken: stoppingToken);
            }
            await Task.Delay(15000, stoppingToken);
        }
    }
}