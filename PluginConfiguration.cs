using MediaBrowser.Model.Plugins;
using System.Collections.Generic;

namespace NotifySync.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public int MaxItems { get; set; } = 5;
        public List<string> EnabledLibraries { get; set; } = [];
        public List<string> ManualLibraryIds { get; set; } = [];
        public List<CategoryMapping> CategoryMappings { get; set; } = [];
    }

    public class CategoryMapping
    {
        public string LibraryId { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
    }
}