using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

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
        /// Gets or sets a value indicating whether this notification has been read by the current user.
        /// This is a transient property set during API response building, not persisted in the Notifications table.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool IsRead { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the current user marked this item — or its
        /// parent series/album — as favorite. Favoriting the series is enough to light up all
        /// its episodes. Transient per-user property set during API response building, not
        /// persisted in the Notifications table.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool IsFavorite { get; set; }

        /// <summary>
        /// Gets or sets the real Jellyfin item ID for synthetic notifications (e.g. collection items).
        /// When <see cref="Id"/> is a synthetic key like "col:{collectionId}:{itemId}", this stores
        /// the actual Jellyfin item ID so permission and played-status checks can resolve the real item.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? RealItemId { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this item is a file upgrade (replaced source file).
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool IsUpgrade { get; set; }

        /// <summary>
        /// Gets or sets the kind of upgrade detected for this item, used by the client to render
        /// a precise label next to the UPD/MAJ badge. One of "quality", "codec", "audio", or null
        /// when the upgrade type couldn't be classified (the client falls back to a plain MAJ badge).
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? UpgradeKind { get; set; }

        /// <summary>
        /// Gets or sets the file path of the media source, used for upgrade detection.
        /// A change in path (e.g. WEBDL → Bluray) is the strongest indicator of a file replacement.
        /// Internal only — not exposed in the API response.
        /// </summary>
        [JsonIgnore]
        public string? FilePath { get; set; }

        /// <summary>
        /// Creates a shallow copy of the current notification item.
        /// </summary>
        /// <returns>A new <see cref="NotificationItem"/>.</returns>
        public NotificationItem Clone()
        {
            var clone = (NotificationItem)MemberwiseClone();
            clone.BackdropImageTags = new List<string>(BackdropImageTags);
            return clone;
        }
    }
}
