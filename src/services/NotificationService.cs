// =============================================================================
// NotificationService.cs
// =============================================================================
// Purpose: Sends notifications for deployment events
// =============================================================================

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutomatedDeploymentCoordinator.Services
{
    /// <summary>
    /// Service for sending notifications about deployment events.
    /// Supports multiple notification channels including email, Slack, and Microsoft Teams.
    /// </summary>
    public class NotificationService : INotificationService
    {
        private readonly NotificationSettings _settings;
        private readonly ILogger<NotificationService> _logger;
        private readonly HttpClient _httpClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="NotificationService"/> class.
        /// </summary>
        /// <param name="settings">Notification configuration settings.</param>
        /// <param name="logger">Logger instance.</param>
        /// <param name="httpClient">HTTP client for sending notifications.</param>
        public NotificationService(
            IOptions<NotificationSettings> settings,
            ILogger<NotificationService> logger,
            HttpClient httpClient)
        {
            _settings = settings.Value ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

            if (_settings.TimeoutSeconds <= 0)
            {
                _settings.TimeoutSeconds = 30; // Default timeout
            }

            _httpClient.Timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds);
        }

        /// <summary>
        /// Sends a deployment notification.
        /// </summary>
        /// <param name="deploymentEvent">The deployment event to notify about.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task SendDeploymentNotificationAsync(DeploymentEvent deploymentEvent)
        {
            if (deploymentEvent == null)
            {
                throw new ArgumentNullException(nameof(deploymentEvent));
            }

            try
            {
                var notificationTasks = new List<Task>();

                // Send notifications to all configured channels
                if (_settings.EmailEnabled)
                {
                    notificationTasks.Add(SendEmailAsync(deploymentEvent));
                }

                if (_settings.SlackEnabled && !string.IsNullOrWhiteSpace(_settings.SlackWebhookUrl))
                {
                    notificationTasks.Add(SendSlackNotificationAsync(deploymentEvent));
                }

                if (_settings.TeamsEnabled && !string.IsNullOrWhiteSpace(_settings.TeamsWebhookUrl))
                {
                    notificationTasks.Add(SendTeamsNotificationAsync(deploymentEvent));
                }

                // Wait for all notifications to complete
                await Task.WhenAll(notificationTasks);

                _logger.LogInformation("Successfully sent deployment notifications for event: {EventId}",
                    deploymentEvent.EventId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send deployment notifications for event: {EventId}",
                    deploymentEvent?.EventId ?? "unknown");
                throw;
            }
        }

        /// <summary>
        /// Sends an email notification.
        /// </summary>
        /// <param name="deploymentEvent">The deployment event.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        private async Task SendEmailAsync(DeploymentEvent deploymentEvent)
        {
            try
            {
                var emailContent = CreateEmailContent(deploymentEvent);
                var emailRequest = new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    RequestUri = new Uri(_settings.EmailWebhookUrl),
                    Content = new StringContent(JsonSerializer.Serialize(emailContent), Encoding.UTF8, "application/json")
                };

                var response = await _httpClient.SendAsync(emailRequest);
                response.EnsureSuccessStatusCode();

                _logger.LogDebug("Email notification sent successfully for event: {EventId}", deploymentEvent.EventId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send email notification for event: {EventId}", deploymentEvent.EventId);
                throw;
            }
        }

        /// <summary>
        /// Sends a Slack notification.
        /// </summary>
        /// <param name="deploymentEvent">The deployment event.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        private async Task SendSlackNotificationAsync(DeploymentEvent deploymentEvent)
        {
            try
            {
                var slackMessage = CreateSlackMessage(deploymentEvent);
                var slackRequest = new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    RequestUri = new Uri(_settings.SlackWebhookUrl),
                    Content = new StringContent(JsonSerializer.Serialize(slackMessage), Encoding.UTF8, "application/json")
                };

                var response = await _httpClient.SendAsync(slackRequest);
                response.EnsureSuccessStatusCode();

                _logger.LogDebug("Slack notification sent successfully for event: {EventId}", deploymentEvent.EventId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send Slack notification for event: {EventId}", deploymentEvent.EventId);
                throw;
            }
        }

        /// <summary>
        /// Sends a Microsoft Teams notification.
        /// </summary>
        /// <param name="deploymentEvent">The deployment event.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        private async Task SendTeamsNotificationAsync(DeploymentEvent deploymentEvent)
        {
            try
            {
                var teamsMessage = CreateTeamsMessage(deploymentEvent);
                var teamsRequest = new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    RequestUri = new Uri(_settings.TeamsWebhookUrl),
                    Content = new StringContent(JsonSerializer.Serialize(teamsMessage), Encoding.UTF8, "application/json")
                };

                var response = await _httpClient.SendAsync(teamsRequest);
                response.EnsureSuccessStatusCode();

                _logger.LogDebug("Teams notification sent successfully for event: {EventId}", deploymentEvent.EventId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send Teams notification for event: {EventId}", deploymentEvent.EventId);
                throw;
            }
        }

        /// <summary>
        /// Creates the email content for a deployment event.
        /// </summary>
        /// <param name="deploymentEvent">The deployment event.</param>
        /// <returns>The email content.</returns>
        private EmailContent CreateEmailContent(DeploymentEvent deploymentEvent)
        {
            return new EmailContent
            {
                Subject = $"Deployment Event: {deploymentEvent.EventType}",
                Body = $"Deployment {deploymentEvent.DeploymentId} ({deploymentEvent.Application})\n\n" +
                       $"Status: {deploymentEvent.Status}\n" +
                       $"Environment: {deploymentEvent.Environment}\n" +
                       $"Triggered By: {deploymentEvent.TriggeredBy}\n\n" +
                       $"Timestamp: {deploymentEvent.Timestamp:yyyy-MM-dd HH:mm:ss}\n\n" +
                       $"Message: {deploymentEvent.Message}",
                Recipients = _settings.EmailRecipients ?? new List<string>(),
                IsHtml = false
            };
        }

        /// <summary>
        /// Creates the Slack message for a deployment event.
        /// </summary>
        /// <param name="deploymentEvent">The deployment event.</param>
        /// <returns>The Slack message.</returns>
        private SlackMessage CreateSlackMessage(DeploymentEvent deploymentEvent)
        {
            var color = deploymentEvent.Status switch
            {
                "Success" => "good",
                "Failed" => "danger",
                _ => "warning"
            };

            return new SlackMessage
            {
                Text = $"Deployment Event: {deploymentEvent.EventType}",
                Attachments = new List<SlackAttachment>
                {
                    new SlackAttachment
                    {
                        Color = color,
                        Title = $"{deploymentEvent.Application} - {deploymentEvent.DeploymentId}",
                        Text = $"Status: {deploymentEvent.Status}\nEnvironment: {deploymentEvent.Environment}",
                        Fields = new List<SlackField>
                        {
                            new SlackField
                            {
                                Title = "Triggered By",
                                Value = deploymentEvent.TriggeredBy,
                                Short = true
                            },
                            new SlackField
                            {
                                Title = "Timestamp",
                                Value = deploymentEvent.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                                Short = true
                            },
                            new SlackField
                            {
                                Title = "Message",
                                Value = deploymentEvent.Message,
                                Short = false
                            }
                        },
                        Footer = "Automated Deployment Coordinator",
                        Ts = (long)deploymentEvent.Timestamp.Subtract(new DateTime(1970, 1, 1)).TotalSeconds
                    }
                }
            };
        }

        /// <summary>
        /// Creates the Microsoft Teams message for a deployment event.
        /// </summary>
        /// <param name="deploymentEvent">The deployment event.</param>
        /// <returns>The Teams message.</returns>
        private TeamsMessage CreateTeamsMessage(DeploymentEvent deploymentEvent)
        {
            var themeColor = deploymentEvent.Status switch
            {
                "Success" => "00FF00",
                "Failed" => "FF0000",
                _ => "FFFF00"
            };

            return new TeamsMessage
            {
                Text = $"Deployment Event: {deploymentEvent.EventType}",
                Sections = new List<TeamsSection>
                {
                    new TeamsSection
                    {
                        ActivityTitle = $"{deploymentEvent.Application} - {deploymentEvent.DeploymentId}",
                        ActivitySubtitle = $"Status: {deploymentEvent.Status} | Environment: {deploymentEvent.Environment}",
                        ActivityText = deploymentEvent.Message,
                        ActivityImage = "https://example.com/deployment-icon.png",
                        Facts = new List<TeamsFact>
                        {
                            new TeamsFact { Name = "Triggered By", Value = deploymentEvent.TriggeredBy },
                            new TeamsFact { Name = "Timestamp", Value = deploymentEvent.Timestamp.ToString("yyyy-MM-dd HH:mm:ss") }
                        },
                        Markdown = true
                    }
                },
                PotentialAction = new List<TeamsPotentialAction>
                {
                    new TeamsPotentialAction
                    {
                        Type = "OpenUri",
                        Name = "View Deployment",
                        Targets = new List<TeamsTarget>
                        {
                            new TeamsTarget { Uri = deploymentEvent.DeploymentUrl }
                        }
                    }
                },
                ThemeColor = themeColor
            };
        }
    }

    /// <summary>
    /// Notification settings configuration.
    /// </summary>
    public class NotificationSettings
    {
        public bool EmailEnabled { get; set; } = false;
        public string EmailWebhookUrl { get; set; } = string.Empty;
        public List<string> EmailRecipients { get; set; } = new List<string>();
        public bool SlackEnabled { get; set; } = false;
        public string SlackWebhookUrl { get; set; } = string.Empty;
        public bool TeamsEnabled { get; set; } = false;
        public string TeamsWebhookUrl { get; set; } = string.Empty;
        public int TimeoutSeconds { get; set; } = 30;
    }

    /// <summary>
    /// Email content model.
    /// </summary>
    public class EmailContent
    {
        public string Subject { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public List<string> Recipients { get; set; } = new List<string>();
        public bool IsHtml { get; set; } = false;
    }

    /// <summary>
    /// Slack message model.
    /// </summary>
    public class SlackMessage
    {
        public string Text { get; set; } = string.Empty;
        public List<SlackAttachment> Attachments { get; set; } = new List<SlackAttachment>();
    }

    /// <summary>
    /// Slack attachment model.
    /// </summary>
    public class SlackAttachment
    {
        public string Color { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public List<SlackField> Fields { get; set; } = new List<SlackField>();
        public string Footer { get; set; } = string.Empty;
        public long Ts { get; set; }
    }

    /// <summary>
    /// Slack field model.
    /// </summary>
    public class SlackField
    {
        public string Title { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public bool Short { get; set; }
    }

    /// <summary>
    /// Microsoft Teams message model.
    /// </summary>
    public class TeamsMessage
    {
        public string Text { get; set; } = string.Empty;
        public List<TeamsSection> Sections { get; set; } = new List<TeamsSection>();
        public List<TeamsPotentialAction> PotentialAction { get; set; } = new List<TeamsPotentialAction>();
        public string ThemeColor { get; set; } = "0072C6";
    }

    /// <summary>
    /// Microsoft Teams section model.
    /// </summary>
    public class TeamsSection
    {
        public string ActivityTitle { get; set; } = string.Empty;
        public string ActivitySubtitle { get; set; } = string.Empty;
        public string ActivityText { get; set; } = string.Empty;
        public string ActivityImage { get; set; } = string.Empty;
        public List<TeamsFact> Facts { get; set; } = new List<TeamsFact>();
        public bool Markdown { get; set; }
    }

    /// <summary>
    /// Microsoft Teams fact model.
    /// </summary>
    public class TeamsFact
    {
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    /// <summary>
    /// Microsoft Teams potential action model.
    /// </summary>
    public class TeamsPotentialAction
    {
        public string Type { get; set; } = "OpenUri";
        public string Name { get; set; } = string.Empty;
        public List<TeamsTarget> Targets { get; set; } = new List<TeamsTarget>();
    }

    /// <summary>
    /// Microsoft Teams target model.
    /// </summary>
    public class TeamsTarget
    {
        public string Uri { get; set; } = string.Empty;
    }

    /// <summary>
    /// Deployment event model.
    /// </summary>
    public class DeploymentEvent
    {
        public string EventId { get; set; } = string.Empty;
        public string EventType { get; set; } = string.Empty;
        public string DeploymentId { get; set; } = string.Empty;
        public string Application { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Environment { get; set; } = string.Empty;
        public string TriggeredBy { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string Message { get; set; } = string.Empty;
        public string DeploymentUrl { get; set; } = string.Empty;
    }

    /// <summary>
    /// Notification service interface.
    /// </summary>
    public interface INotificationService
    {
        /// <summary>
        /// Sends a deployment notification.
        /// </summary>
        /// <param name="deploymentEvent">The deployment event to notify about.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task SendDeploymentNotificationAsync(DeploymentEvent deploymentEvent);
    }
}