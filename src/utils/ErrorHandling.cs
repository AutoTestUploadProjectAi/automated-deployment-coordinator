using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.Extensions.Logging;

namespace AutomatedDeploymentCoordinator.Utils
{
    /// <summary>
    /// Custom error handling utilities for the deployment coordinator application.
    /// Provides structured error types, validation helpers, and logging capabilities.
    /// </summary>
    public static class ErrorHandling
    {
        /// <summary>
        /// Represents a standardized API error response.
        /// </summary>
        public class ApiError
        {
            public int StatusCode { get; set; }
            public string ErrorCode { get; set; }
            public string Message { get; set; }
            public string Details { get; set; }
            public DateTime Timestamp { get; set; }
            public string TraceId { get; set; }

            public ApiError(int statusCode, string errorCode, string message, string details = null)
            {
                StatusCode = statusCode;
                ErrorCode = errorCode;
                Message = message;
                Details = details;
                Timestamp = DateTime.UtcNow;
                TraceId = Guid.NewGuid().ToString();
            }
        }

        /// <summary>
        /// Custom exception for deployment validation errors.
        /// </summary>
        public class DeploymentValidationException : Exception
        {
            public string ErrorCode { get; }
            public IDictionary<string, string> ValidationErrors { get; }

            public DeploymentValidationException(string errorCode, string message, IDictionary<string, string> validationErrors = null)
                : base(message)
            {
                ErrorCode = errorCode;
                ValidationErrors = validationErrors ?? new Dictionary<string, string>();
            }

            public DeploymentValidationException(string errorCode, string message, Exception innerException)
                : base(message, innerException)
            {
                ErrorCode = errorCode;
            }
        }

        /// <summary>
        /// Validates deployment model and throws structured exception if invalid.
        /// </summary>
        /// <param name="deployment">The deployment model to validate.</param>
        /// <exception cref="DeploymentValidationException">Thrown when validation fails.</exception>
        public static void ValidateDeploymentModel(DeploymentModel deployment)
        {
            var errors = new Dictionary<string, string>();

            if (deployment == null)
            {
                throw new ArgumentNullException(nameof(deployment), "Deployment model cannot be null");
            }

            if (string.IsNullOrWhiteSpace(deployment.Name))
            {
                errors["Name"] = "Deployment name is required";
            }

            if (deployment.TargetEnvironment == null)
            {
                errors["TargetEnvironment"] = "Target environment is required";
            }

            if (deployment.DeploymentDate == default)
            {
                errors["DeploymentDate"] = "Deployment date is required";
            }

            if (errors.Count > 0)
            {
                throw new DeploymentValidationException(
                    "DEPLOYMENT_VALIDATION_ERROR",
                    "Deployment model validation failed",
                    errors
                );
            }
        }

        /// <summary>
        /// Handles exceptions and returns a standardized API error response.
        /// </summary>
        /// <param name="exception">The exception to handle.</param>
        /// <param name="logger">ILogger instance for logging.</param>
        /// <param name="includeDetails">Whether to include detailed error information.</param>
        /// <returns>Standardized ApiError object.</returns>
        public static ApiError HandleException(Exception exception, ILogger logger, bool includeDetails = false)
        {
            ApiError error;

            switch (exception)
            {
                case DeploymentValidationException validationEx:
                    error = new ApiError(
                        (int)HttpStatusCode.BadRequest,
                        validationEx.ErrorCode,
                        validationEx.Message,
                        includeDetails ? FormatValidationErrors(validationEx.ValidationErrors) : null
                    );
                    logger.LogWarning(exception, "Deployment validation error: {Message}", exception.Message);
                    break;

                case ArgumentNullException argEx:
                    error = new ApiError(
                        (int)HttpStatusCode.BadRequest,
                        "ARGUMENT_NULL_ERROR",
                        argEx.Message
                    );
                    logger.LogWarning(exception, "Argument null error: {Message}", exception.Message);
                    break;

                case ArgumentException argEx:
                    error = new ApiError(
                        (int)HttpStatusCode.BadRequest,
                        "ARGUMENT_ERROR",
                        argEx.Message
                    );
                    logger.LogWarning(exception, "Argument error: {Message}", exception.Message);
                    break;

                default:
                    error = new ApiError(
                        (int)HttpStatusCode.InternalServerError,
                        "INTERNAL_SERVER_ERROR",
                        "An unexpected error occurred"
                    );
                    logger.LogError(exception, "Unhandled exception: {Message}", exception.Message);
                    break;
            }

            return error;
        }

        /// <summary>
        /// Formats validation errors for display in API response.
        /// </summary>
        /// <param name="errors">Dictionary of validation errors.</param>
        /// <returns>Formatted error string.</returns>
        private static string FormatValidationErrors(IDictionary<string, string> errors)
        {
            if (errors == null || errors.Count == 0)
            {
                return null;
            }

            var formattedErrors = new List<string>();
            foreach (var kvp in errors)
            {
                formattedErrors.Add($"{kvp.Key}: {kvp.Value}");
            }

            return string.Join("; ", formattedErrors);
        }

        /// <summary>
        /// Validates that a string is not null or whitespace.
        /// </summary>
        /// <param name="value">The string value to validate.</param>
        /// <param name="paramName">The name of the parameter.</param>
        /// <param name="errorMessage">Custom error message.</param>
        /// <exception cref="ArgumentException">Thrown when validation fails.</exception>
        public static void ValidateString(string value, string paramName, string errorMessage = null)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    errorMessage ?? $"{paramName} cannot be null or whitespace",
                    paramName
                );
            }
        }

        /// <summary>
        /// Validates that an object is not null.
        /// </summary>
        /// <param name="value">The object to validate.</param>
        /// <param name="paramName">The name of the parameter.</param>
        /// <param name="errorMessage">Custom error message.</param>
        /// <exception cref="ArgumentNullException">Thrown when validation fails.</exception>
        public static void ValidateNotNull(object value, string paramName, string errorMessage = null)
        {
            if (value == null)
            {
                throw new ArgumentNullException(
                    paramName,
                    errorMessage ?? $"{paramName} cannot be null"
                );
            }
        }

        /// <summary>
        /// Validates that a date is not in the future.
        /// </summary>
        /// <param name="date">The date to validate.</param>
        /// <param name="paramName">The name of the parameter.</param>
        /// <param name="errorMessage">Custom error message.</param>
        /// <exception cref="ArgumentException">Thrown when validation fails.</exception>
        public static void ValidateDateNotFuture(DateTime date, string paramName, string errorMessage = null)
        {
            if (date > DateTime.UtcNow)
            {
                throw new ArgumentException(
                    errorMessage ?? $"{paramName} cannot be in the future",
                    paramName
                );
            }
        }

        /// <summary>
        /// Creates a standardized error response for HTTP controllers.
        /// </summary>
        /// <param name="error">The ApiError to convert.</param>
        /// <returns>ProblemDetails object for ASP.NET Core.</returns>
        public static Microsoft.AspNetCore.Mvc.ProblemDetails CreateProblemDetails(ApiError error)
        {
            return new Microsoft.AspNetCore.Mvc.ProblemDetails
            {
                Status = error.StatusCode,
                Title = error.Message,
                Detail = error.Details,
                Type = $"https://api.example.com/errors/{error.ErrorCode}",
                Instance = error.TraceId
            };
        }

        /// <summary>
        /// Logs an error with structured properties.
        /// </summary>
        /// <param name="logger">ILogger instance.</param>
        /// <param name="error">The ApiError to log.</param>
        /// <param name="category">The category of the error.</param>
        public static void LogError(ILogger logger, ApiError error, string category)
        {
            var logLevel = error.StatusCode >= 500 
                ? LogLevel.Error 
                : error.StatusCode >= 400 
                    ? LogLevel.Warning 
                    : LogLevel.Information;

            logger.Log(
                logLevel,
                new EventId(error.StatusCode, error.ErrorCode),
                "[{Category}] {Message} (TraceId: {TraceId})",
                category,
                error.Message,
                error.TraceId
            );
        }

        /// <summary>
        /// Wraps a function call with error handling and logging.
        /// </summary>
        /// <typeparam name="TResult">The result type.</typeparam>
        /// <param name="func">The function to execute.</param>
        /// <param name="logger">ILogger instance.</param>
        /// <param name="operationName">Name of the operation for logging.</param>
        /// <returns>The result of the function or default value on error.</returns>
        public static TResult ExecuteWithHandling<TResult>(Func<TResult> func, ILogger logger, string operationName)
        {
            try
            {
                return func();
            }
            catch (Exception ex)
            {
                var error = HandleException(ex, logger);
                LogError(logger, error, operationName);
                return default;
            }
        }

        /// <summary>
        /// Wraps an action call with error handling and logging.
        /// </summary>
        /// <param name="action">The action to execute.</param>
        /// <param name="logger">ILogger instance.</param>
        /// <param name="operationName">Name of the operation for logging.</param>
        public static void ExecuteWithHandling(Action action, ILogger logger, string operationName)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                var error = HandleException(ex, logger);
                LogError(logger, error, operationName);
            }
        }
    }

    /// <summary>
    /// Example deployment model used for validation.
    /// This would typically be in DeploymentModel.cs but included here for completeness.
    /// </summary>
    public class DeploymentModel
    {
        public string Name { get; set; }
        public string TargetEnvironment { get; set; }
        public DateTime DeploymentDate { get; set; }
        public string Version { get; set; }
    }
}