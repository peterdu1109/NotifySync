using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace NotifySync
{
    /// <summary>
    /// Configuration for the NotifySync plugin.
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
        /// </summary>
        public PluginConfiguration()
        {
            EnabledLibraries = new List<string>();
            ManualLibraryIds = new List<string>();
            CategoryMappings = new List<CategoryMapping>();
            MaxItems = 10;
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
        /// Gets or sets the maximum number of items per category.
        /// </summary>
        public int MaxItems { get; set; }
    }
}