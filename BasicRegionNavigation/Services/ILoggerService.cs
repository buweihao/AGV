using MyLog;
using Serilog;
using System;

namespace BasicRegionNavigation.Services
{
    public interface ILoggerService
    {
        void OnDebug(string message, Exception ex = null);
        void OnInfo(string message, Exception ex = null);
        void OnWarn(string message, Exception ex = null);
        void OnError(string message, Exception ex = null);
    }

    public class LoggerService : ILoggerService
    {
        public void OnDebug(string message, Exception ex = null)
        {
            if (ex == null) Log.Debug(message);
            else Log.Debug(ex, message);
        }

        public void OnInfo(string message, Exception ex = null)
        {
            if (ex == null) Log.Information(message);
            else Log.Information(ex, message);
        }

        public void OnWarn(string message, Exception ex = null)
        {
            if (ex == null) Log.Warning(message);
            else Log.Warning(ex, message);
        }

        public void OnError(string message, Exception ex = null)
        {
            if (ex == null) Log.Error(message);
            else Log.Error(ex, message);
        }
    }
}
