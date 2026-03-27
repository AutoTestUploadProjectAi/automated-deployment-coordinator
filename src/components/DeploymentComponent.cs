using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using src.models;
using src.services;

namespace src.components
{
    /// <summary>
    /// Reusable UI component for deployment visualization.
    /// Provides a clean, modern interface to display deployment status and progress.
    /// </summary>
    public class DeploymentComponent : ViewComponent
    {
        private readonly IDeploymentService _deploymentService;
        private readonly ILogger<DeploymentComponent> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="DeploymentComponent"/> class.
        /// </summary>
        /// <param name="deploymentService">Service for deployment operations</param>
        /// <param name="logger">Logger instance</param>
        public DeploymentComponent(IDeploymentService deploymentService, ILogger<DeploymentComponent> logger)
        {
            _deploymentService = deploymentService ?? throw new ArgumentNullException(nameof(deploymentService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Invokes the component to render deployment visualization.
        /// </summary>
        /// <param name="deploymentId">ID of the deployment to visualize</param>
        /// <returns>Component result with deployment view</returns>
        public async Task<IViewComponentResult> InvokeAsync(Guid deploymentId)
        {
            try
            {
                if (deploymentId == Guid.Empty)
                {
                    _logger.LogWarning("Attempted to invoke DeploymentComponent with empty deployment ID");
                    return View("Error", new ErrorViewModel { ErrorMessage = "Invalid deployment ID provided" });
                }

                var deployment = await _deploymentService.GetDeploymentAsync(deploymentId);
                
                if (deployment == null)
                {
                    _logger.LogWarning($"Deployment with ID {deploymentId} not found");
                    return View("Error", new ErrorViewModel { ErrorMessage = "Deployment not found" });
                }

                return View(deployment);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error rendering deployment component for ID {DeploymentId}", deploymentId);
                return View("Error", new ErrorViewModel { ErrorMessage = "An error occurred while loading deployment data" });
            }
        }

        /// <summary>
        /// Gets the deployment status badge class based on deployment state.
        /// </summary>
        /// <param name="status">Deployment status</param>
        /// <returns>CSS class for status badge</returns>
        public static string GetStatusBadgeClass(DeploymentStatus status)
        {
            return status switch
            {
                DeploymentStatus.Pending => "bg-blue-100 text-blue-800",
                DeploymentStatus.InProgress => "bg-yellow-100 text-yellow-800",
                DeploymentStatus.Succeeded => "bg-green-100 text-green-800",
                DeploymentStatus.Failed => "bg-red-100 text-red-800",
                DeploymentStatus.Cancelled => "bg-gray-100 text-gray-800",
                _ => "bg-gray-100 text-gray-800"
            };
        }

        /// <summary>
        /// Gets the percentage width for progress bar based on completion.
        /// </summary>
        /// <param name="completedSteps">Number of completed steps</param>
        /// <param name="totalSteps">Total number of steps</param>
        /// <returns>Percentage width as string</returns>
        public static string GetProgressBarWidth(int completedSteps, int totalSteps)
        {
            if (totalSteps <= 0) return "0%";
            
            var percentage = Math.Min((completedSteps / (double)totalSteps) * 100, 100);
            return $"{percentage:F1}%";
        }
    }

    /// <summary>
    /// View model for deployment component errors.
    /// </summary>
    public class ErrorViewModel
    {
        public string ErrorMessage { get; set; }
    }
}
