using System;

namespace NotifySync
{
    /// <summary>
    /// Shared helper for normalizing user IDs across the plugin.
    /// </summary>
    internal static class IdHelper
    {
        /// <summary>
        /// Normalizes a user ID to a consistent lowercase format without dashes.
        /// </summary>
        /// <param name="userId">The user identifier to normalize.</param>
        /// <returns>The normalized user identifier.</returns>
        internal static string NormalizeId(string userId)
        {
            if (string.IsNullOrEmpty(userId))
            {
                return string.Empty;
            }

            if (Guid.TryParse(userId, out var guid))
            {
                return guid.ToString("N");
            }

            return userId.Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
        }
    }
}
