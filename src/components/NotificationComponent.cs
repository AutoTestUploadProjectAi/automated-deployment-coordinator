using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;

namespace DeploymentCoordinator.Components
{
    /// <summary>
    /// Represents a reusable notification component for displaying deployment and system notifications.
    /// </summary>
    public partial class NotificationComponent : ComponentBase
    {
        // Notification severity levels
        public enum NotificationSeverity
        {
            Info,
            Success,
            Warning,
            Error
        }

        // Notification model
        public class Notification
        {
            public string Id { get; set; } = Guid.NewGuid().ToString();
            public string Title { get; set; } = string.Empty;
            public string Message { get; set; } = string.Empty;
            public NotificationSeverity Severity { get; set; } = NotificationSeverity.Info;
            public DateTime Timestamp { get; set; } = DateTime.UtcNow;
            public bool IsDismissible { get; set; } = true;
            public int TimeoutSeconds { get; set; } = 0; // 0 = no auto-dismiss
        }

        // CSS classes for different notification types
        private static readonly Dictionary<NotificationSeverity, string> SeverityClasses = new()
        {
            { NotificationSeverity.Info, "bg-blue-50 border-blue-200 text-blue-800" },
            { NotificationSeverity.Success, "bg-green-50 border-green-200 text-green-800" },
            { NotificationSeverity.Warning, "bg-yellow-50 border-yellow-200 text-yellow-800" },
            { NotificationSeverity.Error, "bg-red-50 border-red-200 text-red-800" }
        };

        private List<Notification> _notifications = new();
        private bool _isInitialized = false;

        /// <summary>
        /// Adds a new notification to the display queue.
        /// </summary>
        /// <param name="title">Notification title</param>
        /// <param name="message">Notification message</param>
        /// <param name="severity">Notification severity level</param>
        /// <param name="isDismissible">Whether the notification can be manually dismissed</param>
        /// <param name="timeoutSeconds">Auto-dismiss timeout in seconds (0 = no auto-dismiss)</param>
        public void AddNotification(
            string title,
            string message,
            NotificationSeverity severity = NotificationSeverity.Info,
            bool isDismissible = true,
            int timeoutSeconds = 0)
        {
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(message))
            {
                throw new ArgumentException("Notification title and message cannot be empty");
            }

            var notification = new Notification
            {
                Title = title,
                Message = message,
                Severity = severity,
                IsDismissible = isDismissible,
                TimeoutSeconds = timeoutSeconds
            };

            _notifications.Insert(0, notification);
            InvokeAsync(StateHasChanged);

            // Auto-dismiss if timeout is set
            if (timeoutSeconds > 0)
            {
                _ = DismissAfterDelay(notification.Id, timeoutSeconds);
            }
        }

        /// <summary>
        /// Dismisses a notification by ID.
        /// </summary>
        /// <param name="notificationId">The ID of the notification to dismiss</param>
        public void DismissNotification(string notificationId)
        {
            if (string.IsNullOrWhiteSpace(notificationId))
            {
                throw new ArgumentException("Notification ID cannot be empty");
            }

            var notification = _notifications.Find(n => n.Id == notificationId);
            if (notification != null)
            {
                _notifications.Remove(notification);
                InvokeAsync(StateHasChanged);
            }
        }

        /// <summary>
        /// Dismisses all notifications.
        /// </summary>
        public void DismissAllNotifications()
        {
            _notifications.Clear();
            InvokeAsync(StateHasChanged);
        }

        /// <summary>
        /// Dismisses a notification after a specified delay.
        /// </summary>
        /// <param name="notificationId">The ID of the notification to dismiss</param>
        /// <param name="delaySeconds">Delay in seconds before dismissal</param>
        private async Task DismissAfterDelay(string notificationId, int delaySeconds)
        {
            try
            {
                await Task.Delay(delaySeconds * 1000);
                DismissNotification(notificationId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error dismissing notification: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the CSS class for a notification based on its severity.
        /// </summary>
        /// <param name="severity">The notification severity</param>
        /// <returns>CSS class string</returns>
        private string GetSeverityClass(NotificationSeverity severity)
        {
            return SeverityClasses.TryGetValue(severity, out var cssClass) ? cssClass : SeverityClasses[NotificationSeverity.Info];
        }

        /// <summary>
        /// Gets the icon class for a notification based on its severity.
        /// </summary>
        /// <param name="severity">The notification severity</param>
        /// <returns>Icon class string</returns>
        private string GetIconClass(NotificationSeverity severity)
        {
            return severity switch
            {
                NotificationSeverity.Info => "fas fa-info-circle",
                NotificationSeverity.Success => "fas fa-check-circle",
                NotificationSeverity.Warning => "fas fa-exclamation-triangle",
                NotificationSeverity.Error => "fas fa-exclamation-circle",
                _ => "fas fa-info-circle"
            };
        }

        protected override void OnInitialized()
        {
            _isInitialized = true;
        }

        /// <summary>
        /// Renders the notification component.
        /// </summary>
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            if (!_isInitialized) return;

            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "class", "fixed top-4 right-4 z-50 w-96");

            foreach (var notification in _notifications)
            {
                builder.OpenElement(2, "div");
                builder.AddAttribute(3, "class", $"border-l-4 p-4 mb-4 {GetSeverityClass(notification.Severity)} rounded-lg shadow-sm transition-all duration-300 hover:shadow-md");
                
                // Icon
                builder.OpenElement(4, "div");
                builder.AddAttribute(5, "class", "flex");
                
                builder.OpenElement(6, "div");
                builder.AddAttribute(7, "class", "mr-3");
                builder.OpenElement(8, "i");
                builder.AddAttribute(9, "class", $"{GetIconClass(notification.Severity)} text-2xl");
                builder.CloseElement(); // i
                builder.CloseElement(); // div

                // Content
                builder.OpenElement(10, "div");
                builder.AddAttribute(11, "class", "flex-1");
                
                // Title
                builder.OpenElement(12, "p");
                builder.AddAttribute(13, "class", "font-medium mb-1");
                builder.AddContent(14, notification.Title);
                builder.CloseElement(); // p

                // Message
                builder.OpenElement(15, "p");
                builder.AddAttribute(16, "class", "text-sm text-gray-700");
                builder.AddContent(17, notification.Message);
                builder.CloseElement(); // p

                builder.CloseElement(); // div

                // Close button (if dismissible)
                if (notification.IsDismissible)
                {
                    builder.OpenElement(18, "button");
                    builder.AddAttribute(19, "class", "ml-2 bg-transparent hover:bg-gray-200 text-gray-600 font-semibold hover:text-gray-700 py-1 px-2 rounded-full transition-all duration-200");
                    builder.AddAttribute(20, "onclick", EventCallback.Factory.Create(this, () => DismissNotification(notification.Id)));
                    builder.AddContent(21, "×");
                    builder.CloseElement(); // button
                }

                builder.CloseElement(); // div
            }

            builder.CloseElement(); // outer div
        }
    }
}