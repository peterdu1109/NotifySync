using System;

namespace NotifySync
{
    /// <summary>
    /// Represents a record of a deleted media item.
    /// </summary>
    public class DeletedItemRecord
    {
        /// <summary>
        /// Gets or sets the auto-increment row ID.
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// Gets or sets the original Jellyfin item ID.
        /// </summary>
        public string ItemId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the item name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the item type (Movie, Episode, Audio, etc.).
        /// </summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the series name, if applicable.
        /// </summary>
        public string? SeriesName { get; set; }

        /// <summary>
        /// Gets or sets the production year, if applicable.
        /// </summary>
        public int? ProductionYear { get; set; }

        /// <summary>
        /// Gets or sets the UTC date when the item was deleted.
        /// </summary>
        public DateTime DeletedAt { get; set; }

        /// <summary>
        /// Gets or sets the episode index number (when the deleted item is an Episode).
        /// </summary>
        public int? IndexNumber { get; set; }

        /// <summary>
        /// Gets or sets the season index number (when the deleted item is an Episode).
        /// </summary>
        public int? ParentIndexNumber { get; set; }

        /// <summary>
        /// Gets or sets the file path of the deleted media source.
        /// Used by upgrade detection: when a new file arrives with a matching identity,
        /// the old path is the &quot;before&quot; reference for the path-based heuristic.
        /// </summary>
        public string? FilePath { get; set; }
    }
}
