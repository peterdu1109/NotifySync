using System;
using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

#pragma warning disable CA2227, CA1002
namespace NotifySync
{
    /// <summary>
    /// Configuration for the NotifySync plugin.
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        private int _maxItems = 10;
        private int _deletedRetentionDays = 30;

        /// <summary>
        /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
        /// </summary>
        public PluginConfiguration()
        {
            EnabledLibraries = new List<string>();
            ManualLibraryIds = new List<string>();
            CategoryMappings = new List<CategoryMapping>();
            EnabledCollections = new List<string>();
            MaxItems = 10;
            EnableDeletedTracking = true;
            DeletedRetentionDays = 30;
        }

        /// <summary>
        /// Gets or sets the list of enabled library IDs.
        /// </summary>
        public List<string> EnabledLibraries { get; set; }

        /// <summary>
        /// Gets or sets the list of manual library IDs.
        /// </summary>
        public List<string> ManualLibraryIds { get; set; }

        /// <summary>
        /// Gets or sets the list of category mappings.
        /// </summary>
        public List<CategoryMapping> CategoryMappings { get; set; }

        /// <summary>
        /// Gets or sets the list of enabled collection (BoxSet) IDs to monitor.
        /// </summary>
        public List<string> EnabledCollections { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of items per category (clamped 1–50).
        /// </summary>
        public int MaxItems
        {
            get => _maxItems;
            set => _maxItems = Math.Clamp(value, 1, 50);
        }

        /// <summary>
        /// Gets or sets a value indicating whether deleted item tracking is enabled.
        /// </summary>
        public bool EnableDeletedTracking { get; set; }

        /// <summary>
        /// Gets or sets the number of days to retain deleted item records (clamped 1–365).
        /// </summary>
        public int DeletedRetentionDays
        {
            get => _deletedRetentionDays;
            set => _deletedRetentionDays = Math.Clamp(value, 1, 365);
        }
    }
}
