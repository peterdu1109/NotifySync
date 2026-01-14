using MediaBrowser.Model.Plugins;
using System.Collections.Generic;

namespace NotifySync.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public int MaxItems { get; set; } = 5;
        public List<string> EnabledLibraries { get; set; } = new List<string>();
        public List<string> ManualLibraryIds { get; set; } = new List<string>();
        public List<CategoryMapping> CategoryMappings { get; set; } = new List<CategoryMapping>();
    }

    public class CategoryMapping
    {
        public string LibraryId { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
    }
}