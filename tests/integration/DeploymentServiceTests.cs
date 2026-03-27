using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Xunit;
using Moq;
using src.services;
using src.models;

namespace tests.integration
{
    /// <summary>
    /// Integration tests for the DeploymentService class.
    /// Tests verify end-to-end functionality with mocked dependencies.
    /// </summary>
    public class DeploymentServiceTests
    {
        private readonly Mock<ILogger<DeploymentService>> _loggerMock;
        private readonly Mock<IConfiguration> _configMock;
        private readonly DeploymentService _deploymentService;

        public DeploymentServiceTests()
        {
            _loggerMock = new Mock<ILogger<DeploymentService>>();
            _configMock = new Mock<IConfiguration>();
            
            // Setup configuration mock
            _configMock.Setup(c => c["Deployment:TimeoutSeconds"]).Returns("300");
            _configMock.Setup(c => c["Deployment:MaxRetries"]).Returns("3");
            
            _deploymentService = new DeploymentService(
                _loggerMock.Object,
                _configMock.Object
            );
        }

        [Fact]
        public async Task DeployApplication_ValidRequest_Success()
        {
            // Arrange
            var deploymentRequest = new DeploymentRequest
            {
                ApplicationId = "app-123",
                Environment = "production",
                Version = "1.0.0",
                TargetHosts = new List<string> { "host1", "host2", "host3" },
                DeploymentStrategy = "rolling",
                Timeout = TimeSpan.FromMinutes(5)
            };

            // Act
            var result = await _deploymentService.DeployApplication(deploymentRequest);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("app-123", result.ApplicationId);
            Assert.Equal("production", result.Environment);
            Assert.Equal(DeploymentStatus.Succeeded, result.Status);
            Assert.True(result.Duration.TotalSeconds > 0);
            Assert.Equal(3, result.TargetHosts.Count);
            Assert.Equal(3, result.DeploymentSteps.Count);
        }

        [Fact]
        public async Task DeployApplication_EmptyTargetHosts_ThrowsArgumentException()
        {
            // Arrange
            var deploymentRequest = new DeploymentRequest
            {
                ApplicationId = "app-123",
                Environment = "production",
                Version = "1.0.0",
                TargetHosts = new List<string>(),
                DeploymentStrategy = "rolling",
                Timeout = TimeSpan.FromMinutes(5)
            };

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await _deploymentService.DeployApplication(deploymentRequest)
            );
        }

        [Fact]
        public async Task DeployApplication_TimeoutExceeded_ReturnsFailedStatus()
        {
            // Arrange
            var deploymentRequest = new DeploymentRequest
            {
                ApplicationId = "app-123",
                Environment = "production",
                Version = "1.0.0",
                TargetHosts = new List<string> { "host1", "host2", "host3" },
                DeploymentStrategy = "rolling",
                Timeout = TimeSpan.FromSeconds(1) // Very short timeout
            };

            // Act
            var result = await _deploymentService.DeployApplication(deploymentRequest);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(DeploymentStatus.Failed, result.Status);
            Assert.Contains("timeout", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task DeployApplication_NullRequest_ThrowsArgumentNullException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await _deploymentService.DeployApplication(null)
            );
        }

        [Fact]
        public async Task DeployApplication_InvalidEnvironment_ThrowsArgumentException()
        {
            // Arrange
            var deploymentRequest = new DeploymentRequest
            {
                ApplicationId = "app-123",
                Environment = "invalid-env",
                Version = "1.0.0",
                TargetHosts = new List<string> { "host1" },
                DeploymentStrategy = "rolling",
                Timeout = TimeSpan.FromMinutes(5)
            };

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await _deploymentService.DeployApplication(deploymentRequest)
            );
        }

        [Fact]
        public async Task DeployApplication_MaxRetriesExceeded_ReturnsFailedStatus()
        {
            // Arrange
            var deploymentRequest = new DeploymentRequest
            {
                ApplicationId = "app-123",
                Environment = "production",
                Version = "1.0.0",
                TargetHosts = new List<string> { "host1", "host2", "host3" },
                DeploymentStrategy = "rolling",
                Timeout = TimeSpan.FromMinutes(5)
            };

            // Mock configuration to return very low max retries
            _configMock.Setup(c => c["Deployment:MaxRetries"]).Returns("0");

            // Act
            var result = await _deploymentService.DeployApplication(deploymentRequest);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(DeploymentStatus.Failed, result.Status);
            Assert.Contains("retries", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task DeployApplication_ConcurrentDeployments_Success()
        {
            // Arrange
            var deploymentRequest = new DeploymentRequest
            {
                ApplicationId = "app-123",
                Environment = "production",
                Version = "1.0.0",
                TargetHosts = new List<string> { "host1", "host2", "host3" },
                DeploymentStrategy = "rolling",
                Timeout = TimeSpan.FromMinutes(5)
            };

            // Act - Run multiple deployments concurrently
            var tasks = new List<Task<DeploymentResult>>();
            for (int i = 0; i < 5; i++)
            {
                tasks.Add(_deploymentService.DeployApplication(deploymentRequest));
            }

            var results = await Task.WhenAll(tasks);

            // Assert
            Assert.Equal(5, results.Length);
            foreach (var result in results)
            {
                Assert.NotNull(result);
                Assert.Equal("app-123", result.ApplicationId);
                Assert.Equal(DeploymentStatus.Succeeded, result.Status);
            }
        }

        [Fact]
        public async Task GetDeploymentStatus_ValidDeploymentId_ReturnsStatus()
        {
            // Arrange
            var deploymentRequest = new DeploymentRequest
            {
                ApplicationId = "app-123",
                Environment = "production",
                Version = "1.0.0",
                TargetHosts = new List<string> { "host1" },
                DeploymentStrategy = "rolling",
                Timeout = TimeSpan.FromMinutes(5)
            };

            var deploymentResult = await _deploymentService.DeployApplication(deploymentRequest);

            // Act
            var status = await _deploymentService.GetDeploymentStatus(deploymentResult.DeploymentId);

            // Assert
            Assert.NotNull(status);
            Assert.Equal(deploymentResult.DeploymentId, status.DeploymentId);
            Assert.Equal("app-123", status.ApplicationId);
            Assert.Equal(DeploymentStatus.Succeeded, status.Status);
        }

        [Fact]
        public async Task GetDeploymentStatus_InvalidDeploymentId_ReturnsNull()
        {
            // Act
            var status = await _deploymentService.GetDeploymentStatus("invalid-id");

            // Assert
            Assert.Null(status);
        }

        [Fact]
        public async Task CancelDeployment_ValidDeploymentId_CancelsDeployment()
        {
            // Arrange
            var deploymentRequest = new DeploymentRequest
            {
                ApplicationId = "app-123",
                Environment = "production",
                Version = "1.0.0",
                TargetHosts = new List<string> { "host1", "host2", "host3" },
                DeploymentStrategy = "rolling",
                Timeout = TimeSpan.FromMinutes(5)
            };

            var deploymentResult = await _deploymentService.DeployApplication(deploymentRequest);

            // Act
            var cancelResult = await _deploymentService.CancelDeployment(deploymentResult.DeploymentId);

            // Assert
            Assert.True(cancelResult);
            var status = await _deploymentService.GetDeploymentStatus(deploymentResult.DeploymentId);
            Assert.Equal(DeploymentStatus.Canceled, status.Status);
        }

        [Fact]
        public async Task CancelDeployment_InvalidDeploymentId_ReturnsFalse()
        {
            // Act
            var cancelResult = await _deploymentService.CancelDeployment("invalid-id");

            // Assert
            Assert.False(cancelResult);
        }

        [Fact]
        public async Task ValidateDeploymentRequest_ValidRequest_ReturnsTrue()
        {
            // Arrange
            var deploymentRequest = new DeploymentRequest
            {
                ApplicationId = "app-123",
                Environment = "production",
                Version = "1.0.0",
                TargetHosts = new List<string> { "host1", "host2", "host3" },
                DeploymentStrategy = "rolling",
                Timeout = TimeSpan.FromMinutes(5)
            };

            // Act
            var isValid = await _deploymentService.ValidateDeploymentRequest(deploymentRequest);

            // Assert
            Assert.True(isValid);
        }

        [Fact]
        public async Task ValidateDeploymentRequest_InvalidRequest_ReturnsFalse()
        {
            // Arrange
            var deploymentRequest = new DeploymentRequest
            {
                ApplicationId = "app-123",
                Environment = "production",
                Version = "1.0.0",
                TargetHosts = new List<string>(),
                DeploymentStrategy = "rolling",
                Timeout = TimeSpan.FromMinutes(5)
            };

            // Act
            var isValid = await _deploymentService.ValidateDeploymentRequest(deploymentRequest);

            // Assert
            Assert.False(isValid);
        }

        [Fact]
        public async Task GetDeploymentHistory_ValidApplicationId_ReturnsHistory()
        {
            // Arrange
            var deploymentRequest = new DeploymentRequest
            {
                ApplicationId = "app-123",
                Environment = "production",
                Version = "1.0.0",
                TargetHosts = new List<string> { "host1", "host2", "host3" },
                DeploymentStrategy = "rolling",
                Timeout = TimeSpan.FromMinutes(5)
            };

            await _deploymentService.DeployApplication(deploymentRequest);

            // Act
            var history = await _deploymentService.GetDeploymentHistory("app-123");

            // Assert
            Assert.NotNull(history);
            Assert.NotEmpty(history);
            Assert.True(history.Count >= 1);
        }

        [Fact]
        public async Task GetDeploymentHistory_InvalidApplicationId_ReturnsEmptyList()
        {
            // Act
            var history = await _deploymentService.GetDeploymentHistory("non-existent-app");

            // Assert
            Assert.NotNull(history);
            Assert.Empty(history);
        }

        [Fact]
        public async Task RollbackDeployment_ValidDeploymentId_RollbacksSuccessfully()
        {
            // Arrange
            var deploymentRequest = new DeploymentRequest
            {
                ApplicationId = "app-123",
                Environment = "production",
                Version = "1.0.0",
                TargetHosts = new List<string> { "host1", "host2", "host3" },
                DeploymentStrategy = "rolling",
                Timeout = TimeSpan.FromMinutes(5)
            };

            var deploymentResult = await _deploymentService.DeployApplication(deploymentRequest);

            // Act
            var rollbackResult = await _deploymentService.RollbackDeployment(deploymentResult.DeploymentId);

            // Assert
            Assert.NotNull(rollbackResult);
            Assert.Equal("app-123", rollbackResult.ApplicationId);
            Assert.Equal(DeploymentStatus.Succeeded, rollbackResult.Status);
        }

        [Fact]
        public async Task RollbackDeployment_InvalidDeploymentId_ThrowsArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await _deploymentService.RollbackDeployment("invalid-id")
            );
        }

        [Fact]
        public async Task GetDeploymentMetrics_ValidApplicationId_ReturnsMetrics()
        {
            // Arrange
            var deploymentRequest = new DeploymentRequest
            {
                ApplicationId = "app-123",
                Environment = "production",
                Version = "1.0.0",
                TargetHosts = new List<string> { "host1", "host2", "host3" },
                DeploymentStrategy = "rolling",
                Timeout = TimeSpan.FromMinutes(5)
            };

            await _deploymentService.DeployApplication(deploymentRequest);

            // Act
            var metrics = await _deploymentService.GetDeploymentMetrics("app-123");

            // Assert
            Assert.NotNull(metrics);
            Assert.True(metrics.TotalDeployments >= 1);
            Assert.True(metrics.AverageDuration.TotalSeconds >= 0);
            Assert.True(metrics.SuccessRate >= 0 && metrics.SuccessRate <= 100);
        }

        [Fact]
        public async Task GetDeploymentMetrics_InvalidApplicationId_ReturnsDefaultMetrics()
        {
            // Act
            var metrics = await _deploymentService.GetDeploymentMetrics("non-existent-app");

            // Assert
            Assert.NotNull(metrics);
            Assert.Equal(0, metrics.TotalDeployments);
            Assert.Equal(TimeSpan.Zero, metrics.AverageDuration);
            Assert.Equal(0, metrics.SuccessRate);
        }
    }
}