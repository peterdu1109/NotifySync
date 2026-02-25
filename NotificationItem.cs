using System;
using System.Collections.Generic;

namespace NotifySync
{
    /// <summary>
    /// Represents a single notification item for the NotifySync plugin.
    /// </summary>
    public class NotificationItem
    {
        /// <summary>
        /// Gets or sets the item identifier.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the item name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the category name.
        /// </summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the name of the series, if applicable.
        /// </summary>
        public string? SeriesName { get; set; }

        /// <summary>
        /// Gets or sets the series identifier, if applicable.
        /// </summary>
        public string? SeriesId { get; set; }

        /// <summary>
        /// Gets or sets the date the item was created.
        /// </summary>
        public DateTime DateCreated { get; set; }

        /// <summary>
        /// Gets or sets the type of the item.
        /// </summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the runtime in ticks.
        /// </summary>
        public long? RunTimeTicks { get; set; }

        /// <summary>
        /// Gets or sets the production year.
        /// </summary>
        public int? ProductionYear { get; set; }

        /// <summary>
        /// Gets or sets the list of backdrop image tags.
        /// </summary>
        public IReadOnlyList<string> BackdropImageTags { get; set; } = new List<string>();

        /// <summary>
        /// Gets or sets the primary image tag.
        /// </summary>
        public string? PrimaryImageTag { get; set; }

        /// <summary>
        /// Gets or sets the index number (episode number).
        /// </summary>
        public int? IndexNumber { get; set; }

        /// <summary>
        /// Gets or sets the parent index number (season number).
        /// </summary>
        public int? ParentIndexNumber { get; set; }

        /// <summary>
        /// Creates a shallow copy of the current notification item.
        /// </summary>
        /// <returns>A new <see cref="NotificationItem"/>.</returns>
        public NotificationItem Clone()
        {
            return (NotificationItem)MemberwiseClone();
        }
    }
}
