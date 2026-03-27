using System;
using System.Text.RegularExpressions;
using System.Globalization;

namespace AutomatedDeploymentCoordinator.Utils
{
    /// <summary>
    /// Utility class for common string operations with robust error handling and input validation.
    /// </summary>
    public static class StringUtils
    {
        private const int DefaultMaxLength = 255;
        private const string EmailRegexPattern = @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\\.[a-zA-Z]{2,}$";
        private const string UrlRegexPattern = @"^(https?|ftp|file)://[-A-Za-z0-9+&@#/%?=~_|!:,.;]*[-A-Za-z0-9+&@#/%=~_|]";
        private static readonly Regex EmailRegex = new Regex(EmailRegexPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex UrlRegex = new Regex(UrlRegexPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Trims whitespace from both ends of a string and ensures it's not null.
        /// </summary>
        /// <param name="input">The string to trim.</param>
        /// <returns>The trimmed string or empty string if input is null.</returns>
        public static string SafeTrim(string? input)
        {
            if (input == null)
                return string.Empty;
            
            try
            {
                return input.Trim();
            }
            catch (Exception ex)
            {
                // Log and return empty string on unexpected errors
                Logger.LogError($"Error trimming string: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Validates if a string is a valid email address.
        /// </summary>
        /// <param name="email">The email string to validate.</param>
        /// <returns>True if valid email, false otherwise.</returns>
        public static bool IsValidEmail(string? email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return false;

            try
            {
                return EmailRegex.IsMatch(email);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error validating email: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Validates if a string is a valid URL.
        /// </summary>
        /// <param name="url">The URL string to validate.</param>
        /// <returns>True if valid URL, false otherwise.</returns>
        public static bool IsValidUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            try
            {
                return UrlRegex.IsMatch(url);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error validating URL: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Converts a string to camelCase format.
        /// </summary>
        /// <param name="input">The string to convert.</param>
        /// <returns>The camelCase version of the string.</returns>
        public static string ToCamelCase(string? input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            try
            {
                string[] words = Regex.Split(input, @"[^a-zA-Z0-9]");
                if (words.Length == 0)
                    return string.Empty;

                string firstWord = words[0].ToLower(CultureInfo.InvariantCulture);
                string rest = string.Join("", words.Skip(1).Select(w => char.ToUpper(w[0], CultureInfo.InvariantCulture) + w.Substring(1).ToLower(CultureInfo.InvariantCulture)));
                
                return firstWord + rest;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error converting to camelCase: {ex.Message}");
                return input ?? string.Empty;
            }
        }

        /// <summary>
        /// Converts a string to PascalCase format.
        /// </summary>
        /// <param name="input">The string to convert.</param>
        /// <returns>The PascalCase version of the string.</returns>
        public static string ToPascalCase(string? input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            try
            {
                string[] words = Regex.Split(input, @"[^a-zA-Z0-9]");
                if (words.Length == 0)
                    return string.Empty;

                return string.Join("", words.Select(w => char.ToUpper(w[0], CultureInfo.InvariantCulture) + w.Substring(1).ToLower(CultureInfo.InvariantCulture)));
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error converting to PascalCase: {ex.Message}");
                return input ?? string.Empty;
            }
        }

        /// <summary>
        /// Masks a string with asterisks except for the first and last characters.
        /// </summary>
        /// <param name="input">The string to mask.</param>
        /// <param name="maskChar">The character to use for masking (default: '*').</param>
        /// <returns>The masked string.</returns>
        public static string MaskString(string? input, char maskChar = '*')
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            try
            {
                if (input.Length <= 2)
                    return new string(maskChar, input.Length);

                int maskLength = input.Length - 2;
                return input[0] + new string(maskChar, maskLength) + input[^1];
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error masking string: {ex.Message}");
                return new string(maskChar, input?.Length ?? 0);
            }
        }

        /// <summary>
        /// Truncates a string to a specified maximum length.
        /// </summary>
        /// <param name="input">The string to truncate.</param>
        /// <param name="maxLength">The maximum length allowed.</param>
        /// <param name="ellipsis">Whether to add ellipsis if truncated.</param>
        /// <returns>The truncated string.</returns>
        public static string Truncate(string? input, int maxLength = DefaultMaxLength, bool ellipsis = true)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            if (maxLength <= 0)
                throw new ArgumentException("Maximum length must be greater than zero", nameof(maxLength));

            try
            {
                if (input.Length <= maxLength)
                    return input;

                return ellipsis ? input.Substring(0, maxLength - 3) + "..." : input.Substring(0, maxLength);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error truncating string: {ex.Message}");
                return input.Length <= maxLength ? input : input.Substring(0, maxLength);
            }
        }

        /// <summary>
        /// Removes all whitespace from a string.
        /// </summary>
        /// <param name="input">The string to process.</param>
        /// <returns>The string without any whitespace.</returns>
        public static string RemoveWhitespace(string? input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            try
            {
                return Regex.Replace(input, @"\\s+", string.Empty);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error removing whitespace: {ex.Message}");
                return input;
            }
        }

        /// <summary>
        /// Counts the number of words in a string.
        /// </summary>
        /// <param name="input">The string to analyze.</param>
        /// <returns>The word count.</returns>
        public static int WordCount(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return 0;

            try
            {
                string[] words = Regex.Split(input, @"[^a-zA-Z0-9']+");
                return words.Count(w => !string.IsNullOrEmpty(w));
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error counting words: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Escapes special characters in a string for safe use in JSON.
        /// </summary>
        /// <param name="input">The string to escape.</param>
        /// <returns>The escaped string.</returns>
        public static string EscapeJson(string? input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            try
            {
                return System.Text.Json.JsonEncodedText.Encode(input).ToString();
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error escaping JSON: {ex.Message}");
                return input;
            }
        }

        /// <summary>
        /// Checks if a string contains only alphanumeric characters.
        /// </summary>
        /// <param name="input">The string to check.</param>
        /// <returns>True if only alphanumeric, false otherwise.</returns>
        public static bool IsAlphanumeric(string? input)
        {
            if (string.IsNullOrEmpty(input))
                return false;

            try
            {
                return Regex.IsMatch(input, @"^[a-zA-Z0-9]+$");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error checking alphanumeric: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Converts a string to a secure hash (SHA-256).
        /// </summary>
        /// <param name="input">The string to hash.</param>
        /// <returns>The SHA-256 hash as a hexadecimal string.</returns>
        public static string ToSha256Hash(string? input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            try
            {
                using var sha256 = System.Security.Cryptography.SHA256.Create();
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(input);
                byte[] hash = sha256.ComputeHash(bytes);
                
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error hashing string: {ex.Message}");
                return string.Empty;
            }
        }
    }
}