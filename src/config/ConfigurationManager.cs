using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AutomatedDeploymentCoordinator.Config
{
    /// <summary>
    /// Manages application configuration settings with validation, caching, and hot-reload capabilities.
    /// </summary>
    public class ConfigurationManager : IConfigurationManager
    {
        private readonly IConfigurationRoot _configuration;
        private readonly ILogger<ConfigurationManager> _logger;
        private readonly Dictionary<string, object> _cache = new();
        private readonly object _cacheLock = new();
        private readonly HashSet<string> _reloadableSections;

        private const string DefaultConfigFileName = "appsettings.json";
        private const string DefaultEnvironmentVariableName = "ASPNETCORE_ENVIRONMENT";
        private const int CacheExpirationMinutes = 5;

        public ConfigurationManager(ILogger<ConfigurationManager> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            try
            {
                var builder = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile(DefaultConfigFileName, optional: false, reloadOnChange: true)
                    .AddEnvironmentVariables();

                _configuration = builder.Build();
                _reloadableSections = new HashSet<string>(_configuration.GetReloadableSections());

                _logger.LogInformation("ConfigurationManager initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize ConfigurationManager");
                throw new ConfigurationException("Failed to initialize configuration", ex);
            }
        }

        /// <summary>
        /// Gets a configuration value as a strongly-typed object.
        /// </summary>
        /// <typeparam name="T">The expected type of the configuration value.</typeparam>
        /// <param name="key">The configuration key.</param>
        /// <param name="required">Whether the configuration value is required.</param>
        /// <returns>The deserialized configuration value.</returns>
        /// <exception cref="ConfigurationException">Thrown when the value is missing or cannot be deserialized.</exception>
        public T Get<T>(string key, bool required = true)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Configuration key cannot be null or whitespace", nameof(key));

            try
            {
                var cachedValue = GetFromCache<T>(key);
                if (cachedValue != null)
                    return cachedValue;

                var section = _configuration.GetSection(key);
                if (section.Exists())
                {
                    var value = section.Get<T>();
                    if (value != null)
                    {
                        AddToCache(key, value);
                        return value;
                    }
                }

                if (required)
                {
                    throw new ConfigurationException($"Required configuration key '{key}' not found or invalid");
                }

                return default;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get configuration value for key '{Key}'", key);
                if (required)
                    throw new ConfigurationException($"Failed to get configuration value for key '{key}'", ex);
                return default;
            }
        }

        /// <summary>
        /// Gets a configuration value as a string.
        /// </summary>
        /// <param name="key">The configuration key.</param>
        /// <param name="required">Whether the configuration value is required.</param>
        /// <returns>The configuration value as a string.</returns>
        /// <exception cref="ConfigurationException">Thrown when the value is missing and required.</exception>
        public string GetString(string key, bool required = true)
        {
            try
            {
                var cachedValue = GetFromCache<string>(key);
                if (cachedValue != null)
                    return cachedValue;

                var value = _configuration[key];
                if (!string.IsNullOrWhiteSpace(value))
                {
                    AddToCache(key, value);
                    return value;
                }

                if (required)
                {
                    throw new ConfigurationException($"Required configuration key '{key}' not found");
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get string configuration value for key '{Key}'", key);
                if (required)
                    throw new ConfigurationException($"Failed to get string configuration value for key '{key}'", ex);
                return null;
            }
        }

        /// <summary>
        /// Gets a configuration section as a strongly-typed object.
        /// </summary>
        /// <typeparam name="T">The expected type of the configuration section.</typeparam>
        /// <param name="sectionName">The configuration section name.</param>
        /// <param name="required">Whether the configuration section is required.</param>
        /// <returns>The deserialized configuration section.</returns>
        /// <exception cref="ConfigurationException">Thrown when the section is missing or cannot be deserialized.</exception>
        public T GetSection<T>(string sectionName, bool required = true)
        {
            if (string.IsNullOrWhiteSpace(sectionName))
                throw new ArgumentException("Section name cannot be null or whitespace", nameof(sectionName));

            try
            {
                var cachedValue = GetFromCache<T>(sectionName);
                if (cachedValue != null)
                    return cachedValue;

                var section = _configuration.GetSection(sectionName);
                if (section.Exists())
                {
                    var value = section.Get<T>();
                    if (value != null)
                    {
                        AddToCache(sectionName, value);
                        return value;
                    }
                }

                if (required)
                {
                    throw new ConfigurationException($"Required configuration section '{sectionName}' not found or invalid");
                }

                return default;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get configuration section '{SectionName}'", sectionName);
                if (required)
                    throw new ConfigurationException($"Failed to get configuration section '{sectionName}'", ex);
                return default;
            }
        }

        /// <summary>
        /// Gets all keys under a specific configuration section.
        /// </summary>
        /// <param name="sectionName">The configuration section name.</param>
        /// <returns>An enumerable of configuration keys.</returns>
        /// <exception cref="ConfigurationException">Thrown when the section doesn't exist.</exception>
        public IEnumerable<string> GetSectionKeys(string sectionName)
        {
            if (string.IsNullOrWhiteSpace(sectionName))
                throw new ArgumentException("Section name cannot be null or whitespace", nameof(sectionName));

            try
            {
                var section = _configuration.GetSection(sectionName);
                if (!section.Exists())
                    throw new ConfigurationException($"Configuration section '{sectionName}' not found");

                return section.GetChildren().Select(s => s.Key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get section keys for '{SectionName}'", sectionName);
                throw new ConfigurationException($"Failed to get section keys for '{sectionName}'", ex);
            }
        }

        /// <summary>
        /// Validates that required configuration keys exist and have valid values.
        /// </summary>
        /// <param name="requiredKeys">The required configuration keys.</param>
        /// <exception cref="ConfigurationException">Thrown when validation fails.</exception>
        public void ValidateRequiredKeys(IEnumerable<string> requiredKeys)
        {
            if (requiredKeys == null)
                throw new ArgumentNullException(nameof(requiredKeys));

            var missingKeys = new List<string>();
            var invalidKeys = new List<string>();

            foreach (var key in requiredKeys)
            {
                try
                {
                    var value = _configuration[key];
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        missingKeys.Add(key);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Validation failed for key '{Key}'", key);
                    invalidKeys.Add(key);
                }
            }

            if (missingKeys.Any() || invalidKeys.Any())
            {
                var errorMessage = new System.Text.StringBuilder();
                if (missingKeys.Any())
                {
                    errorMessage.AppendLine($"Missing required configuration keys: {string.Join(", ", missingKeys)}");
                }
                if (invalidKeys.Any())
                {
                    errorMessage.AppendLine($"Invalid configuration keys: {string.Join(", ", invalidKeys)}");
                }

                throw new ConfigurationException(errorMessage.ToString());
            }
        }

        /// <summary>
        /// Gets the current application environment (e.g., Development, Production).
        /// </summary>
        /// <returns>The current environment name.</returns>
        public string GetEnvironment()
        {
            try
            {
                return _configuration[DefaultEnvironmentVariableName] ?? "Production";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get environment, defaulting to Production");
                return "Production";
            }
        }

        /// <summary>
        /// Checks if the application is running in a specific environment.
        /// </summary>
        /// <param name="environmentName">The environment name to check.</param>
        /// <returns>True if the application is running in the specified environment.</returns>
        public bool IsEnvironment(string environmentName)
        {
            if (string.IsNullOrWhiteSpace(environmentName))
                throw new ArgumentException("Environment name cannot be null or whitespace", nameof(environmentName));

            return string.Equals(GetEnvironment(), environmentName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Reloads the configuration from all sources.
        /// </summary>
        public void Reload()
        {
            try
            {
                _configuration.Reload();
                ClearCache();
                _logger.LogInformation("Configuration reloaded successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reload configuration");
                throw new ConfigurationException("Failed to reload configuration", ex);
            }
        }

        /// <summary>
        /// Gets the underlying IConfigurationRoot instance.
        /// </summary>
        /// <returns>The IConfigurationRoot instance.</returns>
        public IConfigurationRoot GetConfigurationRoot()
        {
            return _configuration;
        }

        #region Private Methods

        private T GetFromCache<T>(string key)
        {
            lock (_cacheLock)
            {
                if (_cache.TryGetValue(key, out var cachedValue) && cachedValue is T typedValue)
                {
                    return typedValue;
                }
                return default;
            }
        }

        private void AddToCache<T>(string key, T value)
        {
            lock (_cacheLock)
            {
                _cache[key] = value;
            }
        }

        private void ClearCache()
        {
            lock (_cacheLock)
            {
                _cache.Clear();
            }
        }

        private IEnumerable<string> GetReloadableSections()
        {
            return _configuration.GetChildren()
                .Where(section => section.Key.EndsWith("Settings") || section.Key.EndsWith("Config"))
                .Select(section => section.Key);
        }

        #endregion
    }

    /// <summary>
    /// Custom exception for configuration-related errors.
    /// </summary>
    public class ConfigurationException : Exception
    {
        public ConfigurationException() { }
        public ConfigurationException(string message) : base(message) { }
        public ConfigurationException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Interface for configuration management.
    /// </summary>
    public interface IConfigurationManager
    {
        T Get<T>(string key, bool required = true);
        string GetString(string key, bool required = true);
        T GetSection<T>(string sectionName, bool required = true);
        IEnumerable<string> GetSectionKeys(string sectionName);
        void ValidateRequiredKeys(IEnumerable<string> requiredKeys);
        string GetEnvironment();
        bool IsEnvironment(string environmentName);
        void Reload();
        IConfigurationRoot GetConfigurationRoot();
    }
}