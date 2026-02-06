using Serilog;
using System;
using System.IO;

namespace FlashHSI.Core.Logging
{
    public static class LoggingConfig
    {
        public static ILogger CreateLogger()
        {
            /// <ai>AI가 작성함</ai>
            string logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            if (!Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }

            var loggerConfiguration = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Console()
                .WriteTo.File(
                    Path.Combine(logDirectory, "log-.txt"),
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}");

            return loggerConfiguration.CreateLogger();
        }
    }
}
