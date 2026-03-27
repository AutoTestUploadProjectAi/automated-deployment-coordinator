using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using src.models;
using src.services;
using src.utils;
using src.logging;

namespace src.controllers
{
    /// <summary>
    /// REST API controller for managing deployment operations.
    /// Handles deployment lifecycle including creation, status updates, and retrieval.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class DeploymentController : ControllerBase
    {
        private readonly IDeploymentService _deploymentService;
        private readonly ILogger<DeploymentController> _logger;
        private readonly IConfiguration _configuration;
        private readonly IErrorHandling _errorHandling;

        /// <summary>
        /// Initializes a new instance of the <see cref="DeploymentController"/> class.
        /// </summary>
        /// <param name="deploymentService">Service for deployment operations</param>
        /// <param name="logger">Logger for logging operations</param>
        /// <param name="configuration">Configuration settings</param>
        /// <param name="errorHandling">Error handling utilities</param>
        public DeploymentController(
            IDeploymentService deploymentService,
            ILogger<DeploymentController> logger,
            IConfiguration configuration,
            IErrorHandling errorHandling)
        {
            _deploymentService = deploymentService ?? throw new ArgumentNullException(nameof(deploymentService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _errorHandling = errorHandling ?? throw new ArgumentNullException(nameof(errorHandling));
        }

        /// <summary>
        /// Creates a new deployment request.
        /// </summary>
        /// <param name="deploymentRequest">Deployment request data</param>
        /// <returns>Created deployment with status</returns>
        [HttpPost]
        [ProducesResponseType(typeof(DeploymentModel), 201)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 500)]
        public async Task<IActionResult> CreateDeployment([FromBody] DeploymentRequestModel deploymentRequest)
        {
            try
            {
                _logger.LogInformation("Creating new deployment request");
                
                if (deploymentRequest == null)
                {
                    var error = _errorHandling.CreateValidationError("Deployment request cannot be null");
                    return BadRequest(error);
                }

                var validationResult = ValidateDeploymentRequest(deploymentRequest);
                if (!validationResult.IsValid)
                {
                    var error = _errorHandling.CreateValidationError(validationResult.Errors);
                    return BadRequest(error);
                }

                var deployment = await _deploymentService.CreateDeploymentAsync(deploymentRequest);
                _logger.LogInformation("Deployment created successfully with ID: {DeploymentId}", deployment.Id);
                
                return CreatedAtAction(
                    nameof(GetDeploymentById),
                    new { id = deployment.Id },
                    deployment
                );
            }
            catch (ValidationException ex)
            {
                _logger.LogWarning(ex, "Validation failed for deployment creation");
                var error = _errorHandling.CreateValidationError(ex.Message);
                return BadRequest(error);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create deployment");
                var error = _errorHandling.CreateInternalServerError("Failed to create deployment");
                return StatusCode(500, error);
            }
        }

        /// <summary>
        /// Retrieves a specific deployment by ID.
        /// </summary>
        /// <param name="id">Deployment ID</param>
        /// <returns>Deployment details</returns>
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(DeploymentModel), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        [ProducesResponseType(typeof(ErrorResponse), 500)]
        public async Task<IActionResult> GetDeploymentById(string id)
        {
            try
            {
                _logger.LogInformation("Retrieving deployment with ID: {DeploymentId}", id);
                
                if (string.IsNullOrWhiteSpace(id))
                {
                    var error = _errorHandling.CreateValidationError("Deployment ID is required");
                    return BadRequest(error);
                }

                var deployment = await _deploymentService.GetDeploymentByIdAsync(id);
                
                if (deployment == null)
                {
                    var error = _errorHandling.CreateNotFoundError($"Deployment with ID {id} not found");
                    return NotFound(error);
                }

                return Ok(deployment);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve deployment with ID: {DeploymentId}", id);
                var error = _errorHandling.CreateInternalServerError("Failed to retrieve deployment");
                return StatusCode(500, error);
            }
        }

        /// <summary>
        /// Updates the status of a deployment.
        /// </summary>
        /// <param name="id">Deployment ID</param>
        /// <param name="statusUpdate">Status update data</param>
        /// <returns>Updated deployment</returns>
        [HttpPatch("{id}/status")]
        [ProducesResponseType(typeof(DeploymentModel), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        [ProducesResponseType(typeof(ErrorResponse), 500)]
        public async Task<IActionResult> UpdateDeploymentStatus(string id, [FromBody] DeploymentStatusUpdateModel statusUpdate)
        {
            try
            {
                _logger.LogInformation("Updating status for deployment ID: {DeploymentId}", id);
                
                if (string.IsNullOrWhiteSpace(id))
                {
                    var error = _errorHandling.CreateValidationError("Deployment ID is required");
                    return BadRequest(error);
                }

                if (statusUpdate == null || string.IsNullOrWhiteSpace(statusUpdate.Status))
                {
                    var error = _errorHandling.CreateValidationError("Status update data is invalid");
                    return BadRequest(error);
                }

                var deployment = await _deploymentService.UpdateDeploymentStatusAsync(id, statusUpdate.Status);
                
                if (deployment == null)
                {
                    var error = _errorHandling.CreateNotFoundError($"Deployment with ID {id} not found");
                    return NotFound(error);
                }

                return Ok(deployment);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update deployment status for ID: {DeploymentId}", id);
                var error = _errorHandling.CreateInternalServerError("Failed to update deployment status");
                return StatusCode(500, error);
            }
        }

        /// <summary>
        /// Retrieves all deployments with optional filtering.
        /// </summary>
        /// <param name="status">Optional status filter</param>
        /// <param name="limit">Optional result limit</param>
        /// <returns>List of deployments</returns>
        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<DeploymentModel>), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 500)]
        public async Task<IActionResult> GetDeployments(
            [FromQuery] string status = null,
            [FromQuery] int limit = 100)
        {
            try
            {
                _logger.LogInformation("Retrieving deployments with status: {Status}, limit: {Limit}", status, limit);
                
                var deployments = await _deploymentService.GetDeploymentsAsync(status, limit);
                return Ok(deployments);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve deployments");
                var error = _errorHandling.CreateInternalServerError("Failed to retrieve deployments");
                return StatusCode(500, error);
            }
        }

        /// <summary>
        /// Cancels an active deployment.
        /// </summary>
        /// <param name="id">Deployment ID</param>
        /// <returns>Canceled deployment</returns>
        [HttpDelete("{id}/cancel")]
        [ProducesResponseType(typeof(DeploymentModel), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        [ProducesResponseType(typeof(ErrorResponse), 500)]
        public async Task<IActionResult> CancelDeployment(string id)
        {
            try
            {
                _logger.LogInformation("Canceling deployment with ID: {DeploymentId}", id);
                
                if (string.IsNullOrWhiteSpace(id))
                {
                    var error = _errorHandling.CreateValidationError("Deployment ID is required");
                    return BadRequest(error);
                }

                var deployment = await _deploymentService.CancelDeploymentAsync(id);
                
                if (deployment == null)
                {
                    var error = _errorHandling.CreateNotFoundError($"Deployment with ID {id} not found");
                    return NotFound(error);
                }

                return Ok(deployment);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cancel deployment with ID: {DeploymentId}", id);
                var error = _errorHandling.CreateInternalServerError("Failed to cancel deployment");
                return StatusCode(500, error);
            }
        }

        /// <summary>
        /// Validates deployment request data.
        /// </summary>
        /// <param name="request">Deployment request to validate</param>
        /// <returns>Validation result</returns>
        private ValidationResult ValidateDeploymentRequest(DeploymentRequestModel request)
        {
            var errors = new List<string>();
            
            if (string.IsNullOrWhiteSpace(request.ApplicationName))
            {
                errors.Add("Application name is required");
            }
            
            if (string.IsNullOrWhiteSpace(request.Environment))
            {
                errors.Add("Environment is required");
            }
            
            if (request.TargetVersion == null || string.IsNullOrWhiteSpace(request.TargetVersion.Version))
            {
                errors.Add("Target version is required");
            }
            
            if (request.DeploymentStrategy == null || string.IsNullOrWhiteSpace(request.DeploymentStrategy.Name))
            {
                errors.Add("Deployment strategy is required");
            }
            
            return new ValidationResult
            {
                IsValid = !errors.Any(),
                Errors = errors
            };
        }
    }

    /// <summary>
    /// Represents the result of a validation operation.
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }

    /// <summary>
    /// Represents an error response for API operations.
    /// </summary>
    public class ErrorResponse
    {
        public string ErrorCode { get; set; }
        public string Message { get; set; }
        public string Timestamp { get; set; }
        public string Path { get; set; }

        public ErrorResponse()
        {
            Timestamp = DateTime.UtcNow.ToString("o");
        }
    }
}