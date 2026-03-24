using MyLog;
using Serilog;
using System;
using Microsoft.Extensions.DependencyInjection;

namespace BasicRegionNavigation.Services
{
    public class LoggerService : ILoggerService
    {
        private ILoggerService _logger => _serviceProvider.GetRequiredService<ILoggerService>();
        private readonly IServiceProvider _serviceProvider;
        // ---------------------------------------------------------------------
        // 1. 实现 IMyLogConfig 接口，提供系统级日志配置
        // ---------------------------------------------------------------------
        public  MyLogOptions Configure()
        {
            return new MyLogOptions
            {
                // 确保核心算法日志能被捕获
                MinimumLevel = Serilog.Events.LogEventLevel.Debug,
                EnableConsole = true,
                EnableFile = true,
                FilePath = "logs/SystemLog.log",
                // 输出模板：包含时间、等级、消息和异常堆栈
                OutputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
            };
        }

        public LoggerService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        // ---------------------------------------------------------------------
        // 2. 实现 ILoggerService 接口，封装 Serilog 底层方法
        // ---------------------------------------------------------------------
        public void testDebug()
        {
            Log.Debug("This is a test debug message from LoggerService.");
        }


        public void OnDebug(string message, Exception ex = null)
        {
            Log.Debug(ex, message);
        }

        public void OnInfo(string message, Exception ex = null)
        {
            Log.Information(ex, message);
        }

        public void OnWarn(string message, Exception ex = null)
        {
            Log.Warning(ex, message);
        }

        public void OnError(string message, Exception ex = null)
        {
            Log.Error(ex, message);
        }
    }
}
