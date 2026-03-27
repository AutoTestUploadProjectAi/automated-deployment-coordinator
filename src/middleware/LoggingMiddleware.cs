using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace AutomatedDeploymentCoordinator.Middleware
{
    /// <summary>
    /// Middleware for logging HTTP requests and responses with structured logging.
    /// Captures request details, timing, and response information for audit and debugging.
    /// </summary>
    public class LoggingMiddleware
    {
        private const int MaxRequestBodyLength = 2048;
        private const int MaxResponseBodyLength = 2048;
        private const string RequestIdHeader = "X-Request-ID";

        private readonly RequestDelegate _next;
        private readonly ILogger<LoggingMiddleware> _logger;

        public LoggingMiddleware(RequestDelegate next, ILogger<LoggingMiddleware> logger)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var startTime = Stopwatch.GetTimestamp();
            var requestTime = DateTime.UtcNow;
            var requestBody = "";
            var originalBodyStream = context.Response.Body;
            
            try
            {
                // Read request body if it's not too large
                if (context.Request.ContentLength <= MaxRequestBodyLength)
                {
                    context.Request.EnableBuffering();
                    requestBody = await ReadRequestBody(context.Request);
                    context.Request.Body.Position = 0;
                }

                using (var responseBody = new MemoryStream())
                {
                    context.Response.Body = responseBody;

                    await _next(context);

                    var endTime = Stopwatch.GetTimestamp();
                    var duration = GetDuration(startTime, endTime);

                    var responseBodyString = await ReadResponseBody(context.Response);
                    
                    await LogRequestResponse(context, requestTime, requestBody, responseBodyString, duration);
                    
                    await responseBody.CopyToAsync(originalBodyStream);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing request: {Message}", ex.Message);
                throw;
            }
            finally
            {
                context.Response.Body = originalBodyStream;
            }
        }

        private static async Task<string> ReadRequestBody(HttpRequest request)
        {
            using (var reader = new StreamReader(request.Body))
            {
                var body = await reader.ReadToEndAsync();
                return body.Length > MaxRequestBodyLength ? body.Substring(0, MaxRequestBodyLength) + "..." : body;
            }
        }

        private static async Task<string> ReadResponseBody(HttpResponse response)
        {
            response.Body.Seek(0, SeekOrigin.Begin);
            using (var reader = new StreamReader(response.Body))
            {
                var body = await reader.ReadToEndAsync();
                response.Body.Seek(0, SeekOrigin.Begin);
                return body.Length > MaxResponseBodyLength ? body.Substring(0, MaxResponseBodyLength) + "..." : body;
            }
        }

        private static double GetDuration(long start, long end)
        {
            return (end - start) * 1000.0 / Stopwatch.Frequency;
        }

        private async Task LogRequestResponse(HttpContext context, DateTime requestTime, string requestBody, string responseBody, double duration)
        {
            var logEntry = new
            {
                Timestamp = requestTime.ToString("o"),
                RequestId = context.Request.Headers[RequestIdHeader].FirstOrDefault() ?? Guid.NewGuid().ToString(),
                Method = context.Request.Method,
                Path = context.Request.Path,
                QueryString = context.Request.QueryString.ToString(),
                Scheme = context.Request.Scheme,
                Host = context.Request.Host.ToString(),
                ClientIp = context.Connection.RemoteIpAddress?.ToString(),
                UserAgent = context.Request.Headers["User-Agent"].ToString(),
                StatusCode = context.Response.StatusCode,
                DurationMs = Math.Round(duration, 2),
                RequestBody = requestBody,
                ResponseBody = responseBody
            };

            var logLevel = context.Response.StatusCode >= 400 ? LogLevel.Warning : LogLevel.Information;
            
            try
            {
                _logger.Log(logLevel, "Request completed: {LogEntry}", JsonConvert.SerializeObject(logEntry, Formatting.None));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to log request: {Message}", ex.Message);
            }
        }
    }

    public static class LoggingMiddlewareExtensions
    {
        /// <summary>
        /// Adds the logging middleware to the specified IApplicationBuilder.
        /// </summary>
        /// <param name="builder">The IApplicationBuilder instance.</param>
        /// <returns>The IApplicationBuilder instance.</returns>
        public static IApplicationBuilder UseLoggingMiddleware(this IApplicationBuilder builder)
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));
            return builder.UseMiddleware<LoggingMiddleware>();
        }
    }
}