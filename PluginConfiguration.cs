using MediaBrowser.Model.Plugins;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;

namespace NotifySync.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        // Ce paramètre sera lu dynamiquement par le JS maintenant
        public int MaxItems { get; set; } = 5;
        public List<string> EnabledLibraries { get; set; } = new List<string>();

        [XmlIgnore]
        public Dictionary<string, string> UserLastSeen { get; set; } = new Dictionary<string, string>();

        public class UserSeenEntry
        {
            public string UserId { get; set; } = string.Empty;
            public string ItemId { get; set; } = string.Empty;
        }

        [XmlElement("UserLastSeenEntries")]
        public List<UserSeenEntry> UserLastSeenXml
        {
            get
            {
                return UserLastSeen.Select(kv => new UserSeenEntry { UserId = kv.Key, ItemId = kv.Value }).ToList();
            }
            set
            {
                UserLastSeen = new Dictionary<string, string>();
                if (value != null)
                {
                    foreach (var entry in value)
                    {
                        if (!string.IsNullOrEmpty(entry.UserId) && !UserLastSeen.ContainsKey(entry.UserId))
                        {
                            UserLastSeen[entry.UserId] = entry.ItemId;
                        }
                    }
                }
            }
        }

        // New features for V4.2
        public List<string> ManualLibraryIds { get; set; } = new List<string>();
        public List<CategoryMapping> CategoryMappings { get; set; } = new List<CategoryMapping>();
    }

    public class CategoryMapping
    {
        public string LibraryId { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
    }
}