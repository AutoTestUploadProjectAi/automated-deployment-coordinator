using System;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace AutomatedDeploymentCoordinator.Logging
{
    /// <summary>
    /// Custom logging utility for the deployment coordination system.
    /// Provides structured, leveled logging with configurable output targets.
    /// </summary>
    public class Logger
    {
        private readonly ILogger<Logger> _logger;
        private readonly LoggerConfiguration _config;
        private readonly ConcurrentQueue<LogEntry> _logQueue;
        private readonly object _fileLock = new object();
        private readonly object _consoleLock = new object();
        private bool _isDisposed = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="Logger"/> class.
        /// </summary>
        /// <param name="logger">The Microsoft.Extensions.Logging logger.</param>
        /// <param name="config">The logger configuration options.</param>
        public Logger(ILogger<Logger> logger, IOptions<LoggerConfiguration> config)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
            
            if (_config.MaxQueueSize <= 0)
                throw new ArgumentException("MaxQueueSize must be positive", nameof(config));
            
            _logQueue = new ConcurrentQueue<LogEntry>();
            
            if (_config.EnableAsyncLogging)
                StartAsyncLogging();
        }

        /// <summary>
        /// Logs an informational message.
        /// </summary>
        /// <param name="message">The message to log.</param>
        /// <param name="context">Optional context object for structured logging.</param>
        public void Info(string message, object context = null)
        {
            Log(LogLevel.Information, message, context);
        }

        /// <summary>
        /// Logs a warning message.
        /// </summary>
        /// <param name="message">The message to log.</param>
        /// <param name="context">Optional context object for structured logging.</param>
        public void Warning(string message, object context = null)
        {
            Log(LogLevel.Warning, message, context);
        }

        /// <summary>
        /// Logs an error message.
        /// </summary>
        /// <param name="message">The message to log.</param>
        /// <param name="exception">The exception to log.</param>
        /// <param name="context">Optional context object for structured logging.</param>
        public void Error(string message, Exception exception = null, object context = null)
        {
            Log(LogLevel.Error, message, context, exception);
        }

        /// <summary>
        /// Logs a debug message.
        /// </summary>
        /// <param name="message">The message to log.</param>
        /// <param name="context">Optional context object for structured logging.</param>
        public void Debug(string message, object context = null)
        {
            Log(LogLevel.Debug, message, context);
        }

        /// <summary>
        /// Logs a critical message.
        /// </summary>
        /// <param name="message">The message to log.</param>
        /// <param name="exception">The exception to log.</param>
        /// <param name="context">Optional context object for structured logging.</param>
        public void Critical(string message, Exception exception = null, object context = null)
        {
            Log(LogLevel.Critical, message, context, exception);
        }

        /// <summary>
        /// Logs a message with the specified level.
        /// </summary>
        /// <param name="level">The log level.</param>
        /// <param name="message">The message to log.</param>
        /// <param name="context">Optional context object for structured logging.</param>
        /// <param name="exception">Optional exception to log.</param>
        private void Log(LogLevel level, string message, object context = null, Exception exception = null)
        {
            if (string.IsNullOrWhiteSpace(message))
                throw new ArgumentException("Message cannot be null or whitespace", nameof(message));

            if (!IsLevelEnabled(level))
                return;

            try
            {
                var logEntry = new LogEntry
                {
                    Timestamp = DateTime.UtcNow,
                    Level = level,
                    Message = message,
                    Context = context,
                    Exception = exception,
                    ThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId,
                    ProcessId = System.Diagnostics.Process.GetCurrentProcess().Id
                };

                if (_config.EnableAsyncLogging)
                {
                    if (_logQueue.Count >= _config.MaxQueueSize)
                    {
                        _logger.LogWarning("Log queue is full. Dropping oldest log entry.");
                        _logQueue.TryDequeue(out _);
                    }
                    _logQueue.Enqueue(logEntry);
                }
                else
                {
                    ProcessLogEntry(logEntry);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to log message: {Message}", message);
            }
        }

        /// <summary>
        /// Processes a single log entry.
        /// </summary>
        /// <param name="logEntry">The log entry to process.</param>
        private void ProcessLogEntry(LogEntry logEntry)
        {
            try
            {
                string formattedLog = FormatLogEntry(logEntry);

                if (_config.EnableConsoleLogging)
                {
                    lock (_consoleLock)
                    {
                        Console.WriteLine(formattedLog);
                    }
                }

                if (_config.EnableFileLogging)
                {
                    lock (_fileLock)
                    {
                        File.AppendAllText(_config.LogFilePath, formattedLog + "\n", Encoding.UTF8);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process log entry: {LogEntry}", logEntry);
            }
        }

        /// <summary>
        /// Formats a log entry into a string.
        /// </summary>
        /// <param name="logEntry">The log entry to format.</param>
        /// <returns>The formatted log string.</returns>
        private string FormatLogEntry(LogEntry logEntry)
        {
            var sb = new StringBuilder();
            sb.Append(logEntry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            sb.Append($" [TID:{logEntry.ThreadId:000}]");
            sb.Append($" [{logEntry.Level}]:");
            sb.Append($" {logEntry.Message}");

            if (logEntry.Context != null)
            {
                sb.Append($" (Context: {System.Text.Json.JsonSerializer.Serialize(logEntry.Context)})");
            }

            if (logEntry.Exception != null)
            {
                sb.Append($"\nEXCEPTION: {logEntry.Exception.GetType().Name}: {logEntry.Exception.Message}");
                sb.Append($"\nSTACKTRACE: {logEntry.Exception.StackTrace}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Checks if the specified log level is enabled.
        /// </summary>
        /// <param name="level">The log level to check.</param>
        /// <returns>True if the level is enabled, false otherwise.</returns>
        private bool IsLevelEnabled(LogLevel level)
        {
            return level >= _config.MinimumLogLevel;
        }

        /// <summary>
        /// Starts the background logging task.
        /// </summary>
        private void StartAsyncLogging()
        {
            System.Threading.Tasks.Task.Run(async () =>
            {
                while (!_isDisposed)
                {
                    try
                    {
                        while (_logQueue.TryDequeue(out LogEntry logEntry))
                        {
                            ProcessLogEntry(logEntry);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Async logging task encountered an error");
                    }
                    finally
                    {
                        await System.Threading.Tasks.Task.Delay(_config.AsyncFlushInterval);
                    }
                }
            });
        }

        /// <summary>
        /// Flushes any pending log entries.
        /// </summary>
        public void Flush()
        {
            try
            {
                while (_logQueue.TryDequeue(out LogEntry logEntry))
                {
                    ProcessLogEntry(logEntry);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to flush log queue");
            }
        }

        /// <summary>
        /// Releases all resources used by the <see cref="Logger"/>.
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed) return;

            _isDisposed = true;
            Flush();
        }
    }

    /// <summary>
    /// Configuration options for the logger.
    /// </summary>
    public class LoggerConfiguration
    {
        /// <summary>
        /// Gets or sets the minimum log level to output.
        /// </summary>
        public LogLevel MinimumLogLevel { get; set; } = LogLevel.Information;

        /// <summary>
        /// Gets or sets whether to enable console logging.
        /// </summary>
        public bool EnableConsoleLogging { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to enable file logging.
        /// </summary>
        public bool EnableFileLogging { get; set; } = true;

        /// <summary>
        /// Gets or sets the file path for log output.
        /// </summary>
        public string LogFilePath { get; set; } = "logs/application.log";

        /// <summary>
        /// Gets or sets whether to enable asynchronous logging.
        /// </summary>
        public bool EnableAsyncLogging { get; set; } = true;

        /// <summary>
        /// Gets or sets the maximum size of the log queue.
        /// </summary>
        public int MaxQueueSize { get; set; } = 1000;

        /// <summary>
        /// Gets or sets the interval for flushing the log queue.
        /// </summary>
        public int AsyncFlushInterval { get; set; } = 1000;
    }

    /// <summary>
    /// Represents a single log entry.
    /// </summary>
    internal class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public LogLevel Level { get; set; }
        public string Message { get; set; }
        public object Context { get; set; }
        public Exception Exception { get; set; }
        public int ThreadId { get; set; }
        public int ProcessId { get; set; }
    }
}