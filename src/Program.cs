using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AutomatedDeploymentCoordinator
{
    /// <summary>
    /// Entry point for the Automated Deployment Coordinator application.
    /// Handles graceful shutdown via SIGTERM/SIGINT signals.
    /// </summary>
    public class Program
    {
        /// <summary>
        /// Main entry point for the application.
        /// </summary>
        /// <param name="args">Command line arguments.</param>
        public static async Task Main(string[] args)
        {
            try
            {
                var builder = CreateHostBuilder(args);
                var host = builder.Build();

                // Setup graceful shutdown handling
                using var cancellationTokenSource = new CancellationTokenSource();
                
                // Register for SIGTERM/SIGINT
                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true; // Prevent immediate termination
                    cancellationTokenSource.Cancel();
                };

                var logger = host.Services.GetRequiredService<ILogger<Program>>();
                
                // Start the application
                logger.LogInformation("Starting Automated Deployment Coordinator...");
                
                var appLifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
                
                // Hook into application lifetime
                appLifetime.ApplicationStarted.Register(() =>
                {
                    logger.LogInformation("Application started successfully");
                });

                appLifetime.ApplicationStopping.Register(() =>
                {
                    logger.LogInformation("Application is stopping...");
                });

                appLifetime.ApplicationStopped.Register(() =>
                {
                    logger.LogInformation("Application stopped");
                });

                // Run the host with cancellation support
                try
                {
                    await host.RunAsync(cancellationTokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                    logger.LogInformation("Application shutdown initiated");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Unhandled exception during application execution");
                    throw;
                }
                finally
                {
                    logger.LogInformation("Application exiting");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Fatal error: {ex.Message}");
                Environment.Exit(1);
            }
        }

        /// <summary>
        /// Creates the host builder for the application.
        /// </summary>
        /// <param name="args">Command line arguments.</param>
        /// <returns>Configured host builder.</returns>
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                    webBuilder.ConfigureKestrel(options =>
                    {
                        // Configure Kestrel server options if needed
                        options.AddServerHeader = false; // Remove server header for security
                    });
                })
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();
                    logging.AddDebug();
                    logging.SetMinimumLevel(LogLevel.Information);
                })
                .UseContentRoot(Directory.GetCurrentDirectory())
                .UseDefaultServiceProvider((context, options) =>
                {
                    options.ValidateScopes = true;
                    options.ValidateOnBuild = true;
                });
    }
}