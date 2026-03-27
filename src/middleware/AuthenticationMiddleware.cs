using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using System.Text;

namespace AutomatedDeploymentCoordinator.Middleware
{
    /// <summary>
    /// Authentication middleware that validates JWT tokens and enforces access control policies.
    /// </summary>
    public class AuthenticationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<AuthenticationMiddleware> _logger;
        private readonly IConfiguration _configuration;

        /// <summary>
        /// Initializes a new instance of the <see cref="AuthenticationMiddleware"/> class.
        /// </summary>
        /// <param name="next">The next middleware in the pipeline.</param>
        /// <param name="logger">The logger instance.</param>
        /// <param name="configuration">The configuration instance.</param>
        public AuthenticationMiddleware(RequestDelegate next, ILogger<AuthenticationMiddleware> logger, IConfiguration configuration)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        /// <summary>
        /// Invokes the authentication middleware.
        /// </summary>
        /// <param name="context">The HTTP context.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                if (context.Request.Path.StartsWithSegments("/api") && !IsWhitelistedEndpoint(context.Request.Path))
                {
                    string authHeader = context.Request.Headers["Authorization"];
                    
                    if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
                    {
                        await HandleUnauthorizedResponse(context, "Missing or malformed Authorization header");
                        return;
                    }

                    string token = authHeader.Substring("Bearer ".Length).Trim();
                    
                    if (!ValidateToken(token))
                    {
                        await HandleUnauthorizedResponse(context, "Invalid or expired token");
                        return;
                    }

                    // Set user context for downstream middleware/controllers
                    context.Items["AuthenticatedUser"] = ExtractUserIdFromToken(token);
                }

                await _next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Authentication middleware failed: {Message}", ex.Message);
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/json";
                
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    error = "Internal server error",
                    message = "An unexpected error occurred during authentication"
                }));
            }
        }

        /// <summary>
        /// Handles unauthorized response with appropriate headers and message.
        /// </summary>
        /// <param name="context">The HTTP context.</param>
        /// <param name="message">The error message.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        private async Task HandleUnauthorizedResponse(HttpContext context, string message)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers["WWW-Authenticate"] = "Bearer realm=\"automated-deployment-coordinator\", error=\"invalid_token\", error_description=\"Unauthorized\"";
            context.Response.ContentType = "application/json";
            
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error = "Unauthorized",
                message = message
            }));
        }

        /// <summary>
        /// Validates the JWT token.
        /// </summary>
        /// <param name="token">The JWT token.</param>
        /// <returns>True if the token is valid, false otherwise.</returns>
        private bool ValidateToken(string token)
        {
            try
            {
                // In a real implementation, this would validate the token signature,
                // expiration, and other claims using a JWT library
                string expectedSigningKey = _configuration["Authentication:SigningKey"] ?? "default-secure-key";
                
                // Simple validation - in production, use a proper JWT library
                if (token.Length < 10) return false;
                
                // Simulate token validation
                return token.StartsWith("valid-token-") && token.Contains("signature");
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Extracts user ID from the JWT token.
        /// </summary>
        /// <param name="token">The JWT token.</param>
        /// <returns>The user ID as a string.</returns>
        private string ExtractUserIdFromToken(string token)
        {
            try
            {
                // In a real implementation, this would parse the JWT claims
                // Here we simulate by extracting from the token
                if (token.StartsWith("valid-token-user"))
                {
                    return token.Split('-')[2]; // Simulated user ID
                }
                
                return "unknown";
            }
            catch
            {
                return "unknown";
            }
        }

        /// <summary>
        /// Checks if the endpoint is whitelisted for public access.
        /// </summary>
        /// <param name="path">The request path.</param>
        /// <returns>True if the endpoint is whitelisted, false otherwise.</returns>
        private bool IsWhitelistedEndpoint(PathString path)
        {
            string pathValue = path.Value?.ToLowerInvariant() ?? string.Empty;
            
            string[] whitelist = { "/api/health", "/api/docs", "/api/public" };
            
            return whitelist.Any(whitelistedPath => pathValue.StartsWith(whitelistedPath));
        }
    }

    /// <summary>
    /// Extension methods for adding the AuthenticationMiddleware to the HTTP request pipeline.
    /// </summary>
    public static class AuthenticationMiddlewareExtensions
    {
        /// <summary>
        /// Adds the authentication middleware to the specified IApplicationBuilder.
        /// </summary>
        /// <param name="builder">The IApplicationBuilder instance.</param>
        /// <returns>The IApplicationBuilder instance.</returns>
        public static IApplicationBuilder UseAuthenticationMiddleware(this IApplicationBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            return builder.UseMiddleware<AuthenticationMiddleware>();
        }
    }
}