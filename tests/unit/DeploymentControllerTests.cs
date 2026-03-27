using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;
using src.controllers;
using src.services;
using src.models;

namespace tests.unit
{
    public class DeploymentControllerTests
    {
        private readonly Mock<IDeploymentService> _deploymentServiceMock;
        private readonly DeploymentController _controller;

        public DeploymentControllerTests()
        {
            _deploymentServiceMock = new Mock<IDeploymentService>();
            _controller = new DeploymentController(_deploymentServiceMock.Object);
        }

        [Fact]
        public async Task GetDeploymentById_WithValidId_ReturnsDeployment()
        {
            // Arrange
            var testDeployment = new DeploymentModel
            {
                Id = "deployment-123",
                Name = "Test Deployment",
                Status = DeploymentStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            _deploymentServiceMock
                .Setup(s => s.GetDeploymentById("deployment-123"))
                .ReturnsAsync(testDeployment);

            // Act
            var result = await _controller.GetDeploymentById("deployment-123");
            var okResult = result as OkObjectResult;

            // Assert
            Assert.NotNull(okResult);
            Assert.Equal(200, okResult.StatusCode);
            
            var returnedDeployment = okResult.Value as DeploymentModel;
            Assert.Equal(testDeployment.Id, returnedDeployment.Id);
            Assert.Equal(testDeployment.Name, returnedDeployment.Name);
        }

        [Fact]
        public async Task GetDeploymentById_WithInvalidId_ReturnsNotFound()
        {
            // Arrange
            _deploymentServiceMock
                .Setup(s => s.GetDeploymentById("invalid-id"))
                .ReturnsAsync((DeploymentModel)null);

            // Act
            var result = await _controller.GetDeploymentById("invalid-id");

            // Assert
            Assert.IsType<NotFoundResult>(result);
        }

        [Fact]
        public async Task CreateDeployment_WithValidModel_ReturnsCreatedDeployment()
        {
            // Arrange
            var deploymentRequest = new DeploymentModel
            {
                Name = "New Deployment",
                Environment = "production",
                Configuration = new Dictionary<string, string> { { "key", "value" } }
            };

            var createdDeployment = new DeploymentModel
            {
                Id = "deployment-456",
                Name = deploymentRequest.Name,
                Status = DeploymentStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            _deploymentServiceMock
                .Setup(s => s.CreateDeployment(deploymentRequest))
                .ReturnsAsync(createdDeployment);

            // Act
            var result = await _controller.CreateDeployment(deploymentRequest);
            var createdResult = result as CreatedAtActionResult;

            // Assert
            Assert.NotNull(createdResult);
            Assert.Equal(201, createdResult.StatusCode);
            
            var returnedDeployment = createdResult.Value as DeploymentModel;
            Assert.Equal(createdDeployment.Id, returnedDeployment.Id);
            Assert.Equal(createdDeployment.Name, returnedDeployment.Name);
        }

        [Fact]
        public async Task CreateDeployment_WithInvalidModel_ReturnsBadRequest()
        {
            // Arrange
            var deploymentRequest = new DeploymentModel();
            _controller.ModelState.AddModelError("Name", "Required");

            // Act
            var result = await _controller.CreateDeployment(deploymentRequest);

            // Assert
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task UpdateDeploymentStatus_WithValidIdAndStatus_ReturnsUpdatedDeployment()
        {
            // Arrange
            var deploymentId = "deployment-123";
            var newStatus = DeploymentStatus.InProgress;

            var existingDeployment = new DeploymentModel
            {
                Id = deploymentId,
                Name = "Test Deployment",
                Status = DeploymentStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            var updatedDeployment = new DeploymentModel
            {
                Id = deploymentId,
                Name = existingDeployment.Name,
                Status = newStatus,
                CreatedAt = existingDeployment.CreatedAt
            };

            _deploymentServiceMock
                .Setup(s => s.UpdateDeploymentStatus(deploymentId, newStatus))
                .ReturnsAsync(updatedDeployment);

            // Act
            var result = await _controller.UpdateDeploymentStatus(deploymentId, newStatus);
            var okResult = result as OkObjectResult;

            // Assert
            Assert.NotNull(okResult);
            Assert.Equal(200, okResult.StatusCode);
            
            var returnedDeployment = okResult.Value as DeploymentModel;
            Assert.Equal(updatedDeployment.Status, returnedDeployment.Status);
        }

        [Fact]
        public async Task UpdateDeploymentStatus_WithInvalidId_ReturnsNotFound()
        {
            // Arrange
            var deploymentId = "invalid-id";
            var newStatus = DeploymentStatus.Failed;

            _deploymentServiceMock
                .Setup(s => s.UpdateDeploymentStatus(deploymentId, newStatus))
                .ReturnsAsync((DeploymentModel)null);

            // Act
            var result = await _controller.UpdateDeploymentStatus(deploymentId, newStatus);

            // Assert
            Assert.IsType<NotFoundResult>(result);
        }

        [Fact]
        public async Task DeleteDeployment_WithValidId_ReturnsNoContent()
        {
            // Arrange
            var deploymentId = "deployment-789";
            _deploymentServiceMock
                .Setup(s => s.DeleteDeployment(deploymentId))
                .ReturnsAsync(true);

            // Act
            var result = await _controller.DeleteDeployment(deploymentId);

            // Assert
            Assert.IsType<NoContentResult>(result);
        }

        [Fact]
        public async Task DeleteDeployment_WithInvalidId_ReturnsNotFound()
        {
            // Arrange
            var deploymentId = "invalid-id";
            _deploymentServiceMock
                .Setup(s => s.DeleteDeployment(deploymentId))
                .ReturnsAsync(false);

            // Act
            var result = await _controller.DeleteDeployment(deploymentId);

            // Assert
            Assert.IsType<NotFoundResult>(result);
        }

        [Fact]
        public async Task GetDeployments_WithValidParameters_ReturnsPaginatedList()
        {
            // Arrange
            var page = 1;
            var pageSize = 10;
            var totalDeployments = 25;

            var deployments = new List<DeploymentModel>
            {
                new DeploymentModel { Id = "1", Name = "Deployment 1", Status = DeploymentStatus.Completed },
                new DeploymentModel { Id = "2", Name = "Deployment 2", Status = DeploymentStatus.Failed }
            };

            _deploymentServiceMock
                .Setup(s => s.GetDeployments(page, pageSize))
                .ReturnsAsync(new PaginatedResult<DeploymentModel>
                {
                    Items = deployments,
                    Page = page,
                    PageSize = pageSize,
                    TotalItems = totalDeployments,
                    TotalPages = (int)Math.Ceiling((double)totalDeployments / pageSize)
                });

            // Act
            var result = await _controller.GetDeployments(page, pageSize);
            var okResult = result as OkObjectResult;

            // Assert
            Assert.NotNull(okResult);
            Assert.Equal(200, okResult.StatusCode);
            
            var paginatedResult = okResult.Value as PaginatedResult<DeploymentModel>;
            Assert.Equal(deployments.Count, paginatedResult.Items.Count);
            Assert.Equal(totalDeployments, paginatedResult.TotalItems);
        }

        [Fact]
        public async Task GetDeployments_WithInvalidPageParameters_ReturnsBadRequest()
        {
            // Arrange
            var page = -1;
            var pageSize = 0;

            // Act
            var result = await _controller.GetDeployments(page, pageSize);

            // Assert
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task GetDeploymentLogs_WithValidId_ReturnsLogs()
        {
            // Arrange
            var deploymentId = "deployment-123";
            var logs = new List<DeploymentLog>
            {
                new DeploymentLog { Timestamp = DateTime.UtcNow, Level = "INFO", Message = "Deployment started" },
                new DeploymentLog { Timestamp = DateTime.UtcNow.AddSeconds(10), Level = "INFO", Message = "Deployment completed" }
            };

            _deploymentServiceMock
                .Setup(s => s.GetDeploymentLogs(deploymentId))
                .ReturnsAsync(logs);

            // Act
            var result = await _controller.GetDeploymentLogs(deploymentId);
            var okResult = result as OkObjectResult;

            // Assert
            Assert.NotNull(okResult);
            Assert.Equal(200, okResult.StatusCode);
            
            var returnedLogs = okResult.Value as List<DeploymentLog>;
            Assert.Equal(logs.Count, returnedLogs.Count);
        }

        [Fact]
        public async Task GetDeploymentLogs_WithInvalidId_ReturnsNotFound()
        {
            // Arrange
            var deploymentId = "invalid-id";
            _deploymentServiceMock
                .Setup(s => s.GetDeploymentLogs(deploymentId))
                .ReturnsAsync((List<DeploymentLog>)null);

            // Act
            var result = await _controller.GetDeploymentLogs(deploymentId);

            // Assert
            Assert.IsType<NotFoundResult>(result);
        }

        [Fact]
        public async Task CancelDeployment_WithValidId_ReturnsCancelledDeployment()
        {
            // Arrange
            var deploymentId = "deployment-123";
            var cancelledDeployment = new DeploymentModel
            {
                Id = deploymentId,
                Name = "Test Deployment",
                Status = DeploymentStatus.Cancelled,
                CreatedAt = DateTime.UtcNow
            };

            _deploymentServiceMock
                .Setup(s => s.CancelDeployment(deploymentId))
                .ReturnsAsync(cancelledDeployment);

            // Act
            var result = await _controller.CancelDeployment(deploymentId);
            var okResult = result as OkObjectResult;

            // Assert
            Assert.NotNull(okResult);
            Assert.Equal(200, okResult.StatusCode);
            
            var returnedDeployment = okResult.Value as DeploymentModel;
            Assert.Equal(DeploymentStatus.Cancelled, returnedDeployment.Status);
        }

        [Fact]
        public async Task CancelDeployment_WithInvalidId_ReturnsNotFound()
        {
            // Arrange
            var deploymentId = "invalid-id";
            _deploymentServiceMock
                .Setup(s => s.CancelDeployment(deploymentId))
                .ReturnsAsync((DeploymentModel)null);

            // Act
            var result = await _controller.CancelDeployment(deploymentId);

            // Assert
            Assert.IsType<NotFoundResult>(result);
        }

        [Fact]
        public async Task RetryDeployment_WithValidId_ReturnsRetriedDeployment()
        {
            // Arrange
            var deploymentId = "deployment-123";
            var retriedDeployment = new DeploymentModel
            {
                Id = deploymentId,
                Name = "Test Deployment",
                Status = DeploymentStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            _deploymentServiceMock
                .Setup(s => s.RetryDeployment(deploymentId))
                .ReturnsAsync(retriedDeployment);

            // Act
            var result = await _controller.RetryDeployment(deploymentId);
            var okResult = result as OkObjectResult;

            // Assert
            Assert.NotNull(okResult);
            Assert.Equal(200, okResult.StatusCode);
            
            var returnedDeployment = okResult.Value as DeploymentModel;
            Assert.Equal(DeploymentStatus.Pending, returnedDeployment.Status);
        }

        [Fact]
        public async Task RetryDeployment_WithInvalidId_ReturnsNotFound()
        {
            // Arrange
            var deploymentId = "invalid-id";
            _deploymentServiceMock
                .Setup(s => s.RetryDeployment(deploymentId))
                .ReturnsAsync((DeploymentModel)null);

            // Act
            var result = await _controller.RetryDeployment(deploymentId);

            // Assert
            Assert.IsType<NotFoundResult>(result);
        }
    }
}