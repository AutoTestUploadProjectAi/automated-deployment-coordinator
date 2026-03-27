using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using src.models;
using src.utils;
using src.config;
using src.services;

namespace src.services
{
    /// <summary>
    /// Provides business logic for deployment coordination, including validation, orchestration, and status tracking.
    /// </summary>
    public class DeploymentService : IDeploymentService
    {
        private readonly ILogger<DeploymentService> _logger;
        private readonly IConfigurationManager _configManager;
        private readonly INotificationService _notificationService;
        private readonly IDeploymentRepository _deploymentRepository;

        /// <summary>
        /// Initializes a new instance of the <see cref="DeploymentService"/> class.
        /// </summary>
        /// <param name="logger">The logger instance for logging operations.</param>
        /// <param name="configManager">The configuration manager for accessing app settings.</param>
        /// <param name="notificationService">The notification service for sending deployment alerts.</param>
        /// <param name="deploymentRepository">The repository for persisting deployment data.</param>
        public DeploymentService(
            ILogger<DeploymentService> logger,
            IConfigurationManager configManager,
            INotificationService notificationService,
            IDeploymentRepository deploymentRepository)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _deploymentRepository = deploymentRepository ?? throw new ArgumentNullException(nameof(deploymentRepository));
        }

        /// <summary>
        /// Validates deployment configuration before initiating the process.
        /// </summary>
        /// <param name="deploymentRequest">The deployment request containing configuration details.</param>
        /// <returns>True if validation passes, otherwise throws a ValidationException.</returns>
        /// <exception cref="ValidationException">Thrown when validation fails.</exception>
        public async Task<bool> ValidateDeploymentAsync(DeploymentRequest deploymentRequest)
        {
            try
            {
                if (deploymentRequest == null)
                {
                    throw new ValidationException("Deployment request cannot be null");
                }

                if (string.IsNullOrWhiteSpace(deploymentRequest.ApplicationName))
                {
                    throw new ValidationException("Application name is required");
                }

                if (deploymentRequest.TargetEnvironment == null)
                {
                    throw new ValidationException("Target environment must be specified");
                }

                if (deploymentRequest.DeploymentArtifacts == null || deploymentRequest.DeploymentArtifacts.Count == 0)
                {
                    throw new ValidationException("At least one deployment artifact is required");
                }

                foreach (var artifact in deploymentRequest.DeploymentArtifacts)
                {
                    if (string.IsNullOrWhiteSpace(artifact.FilePath))
                    {
                        throw new ValidationException($"Artifact file path cannot be empty for artifact: {artifact.Name}");
                    }
                }

                var allowedEnvironments = _configManager.GetSetting("AllowedEnvironments")?.Split(',');
                if (allowedEnvironments != null && !Array.Exists(allowedEnvironments, e => e == deploymentRequest.TargetEnvironment))
                {
                    throw new ValidationException($"Environment '{deploymentRequest.TargetEnvironment}' is not allowed");
                }

                _logger.LogInformation("Validation successful for deployment: {ApplicationName} to {Environment}", 
                    deploymentRequest.ApplicationName, deploymentRequest.TargetEnvironment);
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Validation failed for deployment request");
                throw;
            }
        }

        /// <summary>
        /// Initiates the deployment process with proper orchestration.
        /// </summary>
        /// <param name="deploymentRequest">The deployment request to process.</param>
        /// <returns>The deployment result with status and metadata.</returns>
        /// <exception cref="DeploymentException">Thrown when deployment fails.</exception>
        public async Task<DeploymentResult> InitiateDeploymentAsync(DeploymentRequest deploymentRequest)
        {
            try
            {
                _logger.LogInformation("Starting deployment for {ApplicationName} to {Environment}", 
                    deploymentRequest.ApplicationName, deploymentRequest.TargetEnvironment);

                var deploymentId = Guid.NewGuid().ToString();
                var deploymentStatus = new DeploymentStatus
                {
                    DeploymentId = deploymentId,
                    ApplicationName = deploymentRequest.ApplicationName,
                    TargetEnvironment = deploymentRequest.TargetEnvironment,
                    Status = DeploymentStatusType.InProgress,
                    StartTime = DateTime.UtcNow,
                    Artifacts = deploymentRequest.DeploymentArtifacts
                };

                await _deploymentRepository.SaveStatusAsync(deploymentStatus);

                var result = await ExecuteDeploymentStagesAsync(deploymentId, deploymentRequest);

                if (result.IsSuccess)
                {
                    await _notificationService.SendNotificationAsync(new DeploymentNotification
                    {
                        DeploymentId = deploymentId,
                        ApplicationName = deploymentRequest.ApplicationName,
                        Environment = deploymentRequest.TargetEnvironment,
                        Status = "SUCCESS",
                        Message = "Deployment completed successfully"
                    });
                }
                else
                {
                    await _notificationService.SendNotificationAsync(new DeploymentNotification
                    {
                        DeploymentId = deploymentId,
                        ApplicationName = deploymentRequest.ApplicationName,
                        Environment = deploymentRequest.TargetEnvironment,
                        Status = "FAILED",
                        Message = result.ErrorMessage
                    });
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Deployment initiation failed for {ApplicationName}", deploymentRequest?.ApplicationName);
                throw new DeploymentException("Failed to initiate deployment", ex);
            }
        }

        /// <summary>
        /// Executes the deployment stages in sequence with proper error handling.
        /// </summary>
        /// <param name="deploymentId">The unique deployment identifier.</param>
        /// <param name="deploymentRequest">The deployment request containing configuration.</param>
        /// <returns>The deployment result with status and metadata.</returns>
        private async Task<DeploymentResult> ExecuteDeploymentStagesAsync(string deploymentId, DeploymentRequest deploymentRequest)
        {
            var stages = new List<Func<Task<bool>>
            {
                () => PreDeploymentChecksAsync(deploymentId, deploymentRequest),
                () => DownloadArtifactsAsync(deploymentId, deploymentRequest),
                () => DeployToEnvironmentAsync(deploymentId, deploymentRequest),
                () => PostDeploymentValidationAsync(deploymentId, deploymentRequest)
            };

            foreach (var stage in stages)
            {
                try
                {
                    var stageSuccess = await stage();
                    if (!stageSuccess)
                    {
                        return new DeploymentResult
                        {
                            DeploymentId = deploymentId,
                            IsSuccess = false,
                            ErrorMessage = $"Stage failed: {stage.Method.Name}",
                            EndTime = DateTime.UtcNow
                        };
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Stage execution failed: {StageName}", stage.Method.Name);
                    await UpdateDeploymentStatusAsync(deploymentId, DeploymentStatusType.Failed, ex.Message);
                    return new DeploymentResult
                    {
                        DeploymentId = deploymentId,
                        IsSuccess = false,
                        ErrorMessage = ex.Message,
                        EndTime = DateTime.UtcNow
                    };
                }
            }

            await UpdateDeploymentStatusAsync(deploymentId, DeploymentStatusType.Completed, "Deployment completed successfully");
            return new DeploymentResult
            {
                DeploymentId = deploymentId,
                IsSuccess = true,
                EndTime = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Performs pre-deployment checks and validations.
        /// </summary>
        /// <param name="deploymentId">The deployment identifier.</param>
        /// <param name="deploymentRequest">The deployment request.</param>
        /// <returns>True if checks pass, otherwise false.</returns>
        private async Task<bool> PreDeploymentChecksAsync(string deploymentId, DeploymentRequest deploymentRequest)
        {
            _logger.LogInformation("Starting pre-deployment checks for {DeploymentId}", deploymentId);
            
            // Check if target environment is reachable
            var environmentConfig = _configManager.GetEnvironmentConfig(deploymentRequest.TargetEnvironment);
            if (environmentConfig == null)
            {
                throw new DeploymentException($"Environment configuration not found for {deploymentRequest.TargetEnvironment}");
            }

            // Check resource availability
            var availableResources = await CheckResourceAvailabilityAsync(deploymentRequest);
            if (!availableResources)
            {
                throw new DeploymentException("Insufficient resources available for deployment");
            }

            // Check for existing deployments
            var activeDeployments = await _deploymentRepository.GetActiveDeploymentsAsync(
                deploymentRequest.ApplicationName, deploymentRequest.TargetEnvironment);
            
            if (activeDeployments.Count > 0)
            {
                throw new DeploymentException("Another deployment is currently active for this application/environment");
            }

            await UpdateDeploymentStatusAsync(deploymentId, DeploymentStatusType.Validated, "Pre-deployment checks completed");
            return true;
        }

        /// <summary>
        /// Downloads deployment artifacts to the target environment.
        /// </summary>
        /// <param name="deploymentId">The deployment identifier.</param>
        /// <param name="deploymentRequest">The deployment request.</param>
        /// <returns>True if download succeeds, otherwise false.</returns>
        private async Task<bool> DownloadArtifactsAsync(string deploymentId, DeploymentRequest deploymentRequest)
        {
            _logger.LogInformation("Starting artifact download for {DeploymentId}", deploymentId);
            
            foreach (var artifact in deploymentRequest.DeploymentArtifacts)
            {
                try
                {
                    await DownloadArtifactAsync(artifact);
                    _logger.LogInformation("Downloaded artifact: {ArtifactName}", artifact.Name);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to download artifact: {ArtifactName}", artifact.Name);
                    throw new DeploymentException($"Artifact download failed: {artifact.Name}", ex);
                }
            }

            await UpdateDeploymentStatusAsync(deploymentId, DeploymentStatusType.ArtifactsDownloaded, "Artifacts downloaded successfully");
            return true;
        }

        /// <summary>
        /// Deploys artifacts to the target environment.
        /// </summary>
        /// <param name="deploymentId">The deployment identifier.</param>
        /// <param name="deploymentRequest">The deployment request.</param>
        /// <returns>True if deployment succeeds, otherwise false.</returns>
        private async Task<bool> DeployToEnvironmentAsync(string deploymentId, DeploymentRequest deploymentRequest)
        {
            _logger.LogInformation("Starting deployment to {Environment} for {DeploymentId}", 
                deploymentRequest.TargetEnvironment, deploymentId);
            
            var environmentConfig = _configManager.GetEnvironmentConfig(deploymentRequest.TargetEnvironment);
            if (environmentConfig == null)
            {
                throw new DeploymentException($"Environment configuration not found for {deploymentRequest.TargetEnvironment}");
            }

            // Execute deployment steps based on environment type
            switch (deploymentRequest.TargetEnvironment.ToLower())
            {
                case "production":
                    await ExecuteProductionDeploymentAsync(deploymentRequest, environmentConfig);
                    break;
                case "staging":
                    await ExecuteStagingDeploymentAsync(deploymentRequest, environmentConfig);
                    break;
                default:
                    await ExecuteDefaultDeploymentAsync(deploymentRequest, environmentConfig);
                    break;
            }

            await UpdateDeploymentStatusAsync(deploymentId, DeploymentStatusType.Deployed, "Deployment to environment completed");
            return true;
        }

        /// <summary>
        /// Performs post-deployment validation and health checks.
        /// </summary>
        /// <param name="deploymentId">The deployment identifier.</param>
        /// <param name="deploymentRequest">The deployment request.</param>
        /// <returns>True if validation succeeds, otherwise false.</returns>
        private async Task<bool> PostDeploymentValidationAsync(string deploymentId, DeploymentRequest deploymentRequest)
        {
            _logger.LogInformation("Starting post-deployment validation for {DeploymentId}", deploymentId);
            
            // Health check
            var healthCheckResult = await PerformHealthCheckAsync(deploymentRequest.TargetEnvironment);
            if (!healthCheckResult.IsHealthy)
            {
                throw new DeploymentException($"Post-deployment health check failed: {healthCheckResult.ErrorMessage}");
            }

            // Functional validation
            var validationResult = await ValidateDeploymentFunctionalityAsync(deploymentRequest);
            if (!validationResult.IsSuccess)
            {
                throw new DeploymentException($"Functional validation failed: {validationResult.ErrorMessage}");
            }

            await UpdateDeploymentStatusAsync(deploymentId, DeploymentStatusType.Validated, "Post-deployment validation completed");
            return true;
        }

        /// <summary>
        /// Updates the deployment status in the repository.
        /// </summary>
        /// <param name="deploymentId">The deployment identifier.</param>
        /// <param name="status">The new status.</param>
        /// <param name="message">The status message.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        private async Task UpdateDeploymentStatusAsync(string deploymentId, DeploymentStatusType status, string message)
        {
            var currentStatus = await _deploymentRepository.GetStatusAsync(deploymentId);
            if (currentStatus != null)
            {
                currentStatus.Status = status;
                currentStatus.Message = message;
                currentStatus.LastUpdated = DateTime.UtcNow;
                await _deploymentRepository.SaveStatusAsync(currentStatus);
            }
        }

        // Helper methods for specific deployment scenarios
        private async Task ExecuteProductionDeploymentAsync(DeploymentRequest request, EnvironmentConfig config) { /* Production-specific logic */ }
        private async Task ExecuteStagingDeploymentAsync(DeploymentRequest request, EnvironmentConfig config) { /* Staging-specific logic */ }
        private async Task ExecuteDefaultDeploymentAsync(DeploymentRequest request, EnvironmentConfig config) { /* Default logic */ }
        private async Task DownloadArtifactAsync(DeploymentArtifact artifact) { /* Artifact download logic */ }
        private async Task<bool> CheckResourceAvailabilityAsync(DeploymentRequest request) { /* Resource check logic */ }
        private async Task<HealthCheckResult> PerformHealthCheckAsync(string environment) { /* Health check logic */ }
        private async Task<ValidationResult> ValidateDeploymentFunctionalityAsync(DeploymentRequest request) { /* Functional validation logic */ }
    }

    /// <summary>
    /// Represents the result of a deployment operation.
    /// </summary>
    public class DeploymentResult
    {
        public string DeploymentId { get; set; }
        public bool IsSuccess { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
    }

    /// <summary>
    /// Represents the result of a health check.
    /// </summary>
    public class HealthCheckResult
    {
        public bool IsHealthy { get; set; }
        public string ErrorMessage { get; set; }
        public int StatusCode { get; set; }
    }

    /// <summary>
    /// Represents the result of a validation operation.
    /// </summary>
    public class ValidationResult
    {
        public bool IsSuccess { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Exception thrown during deployment operations.
    /// </summary>
    public class DeploymentException : Exception
    {
        public DeploymentException() { }
        public DeploymentException(string message) : base(message) { }
        public DeploymentException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Exception thrown during validation operations.
    /// </summary>
    public class ValidationException : Exception
    {
        public ValidationException() { }
        public ValidationException(string message) : base(message) { }
        public ValidationException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Interface for deployment service.
    /// </summary>
    public interface IDeploymentService
    {
        Task<bool> ValidateDeploymentAsync(DeploymentRequest deploymentRequest);
        Task<DeploymentResult> InitiateDeploymentAsync(DeploymentRequest deploymentRequest);
    }

    /// <summary>
    /// Interface for deployment repository.
    /// </summary>
    public interface IDeploymentRepository
    {
        Task SaveStatusAsync(DeploymentStatus status);
        Task<DeploymentStatus> GetStatusAsync(string deploymentId);
        Task<List<DeploymentStatus>> GetActiveDeploymentsAsync(string applicationName, string environment);
    }

    /// <summary>
    /// Represents deployment status information.
    /// </summary>
    public class DeploymentStatus
    {
        public string DeploymentId { get; set; }
        public string ApplicationName { get; set; }
        public string TargetEnvironment { get; set; }
        public DeploymentStatusType Status { get; set; }
        public string Message { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public DateTime LastUpdated { get; set; }
        public List<DeploymentArtifact> Artifacts { get; set; }
    }

    /// <summary>
    /// Represents different deployment status types.
    /// </summary>
    public enum DeploymentStatusType
    {
        InProgress,
        Validated,
        ArtifactsDownloaded,
        Deployed,
        Completed,
        Failed
    }

    /// <summary>
    /// Represents a deployment notification.
    /// </summary>
    public class DeploymentNotification
    {
        public string DeploymentId { get; set; }
        public string ApplicationName { get; set; }
        public string Environment { get; set; }
        public string Status { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// Represents a deployment request.
    /// </summary>
    public class DeploymentRequest
    {
        public string ApplicationName { get; set; }
        public string TargetEnvironment { get; set; }
        public List<DeploymentArtifact> DeploymentArtifacts { get; set; }
        public Dictionary<string, string> Configuration { get; set; }
    }

    /// <summary>
    /// Represents a deployment artifact.
    /// </summary>
    public class DeploymentArtifact
    {
        public string Name { get; set; }
        public string FilePath { get; set; }
        public string Type { get; set; }
        public long Size { get; set; }
    }

    /// <summary>
    /// Represents environment configuration.
    /// </summary>
    public class EnvironmentConfig
    {
        public string Name { get; set; }
        public string Endpoint { get; set; }
        public string Credentials { get; set; }
        public Dictionary<string, string> Variables { get; set; }
    }
}
