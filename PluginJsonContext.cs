using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NotifySync
{
    /// <summary>
    /// Unified JSON serialization context for the NotifySync plugin.
    /// </summary>
    [JsonSerializable(typeof(object))]
    [JsonSerializable(typeof(List<string>))]
    [JsonSerializable(typeof(List<NotificationItem>))]
    [JsonSerializable(typeof(Dictionary<string, long>))]
    [JsonSerializable(typeof(Dictionary<string, bool>))]
    [JsonSerializable(typeof(PluginConfiguration))]
    internal sealed partial class PluginJsonContext : JsonSerializerContext
    {
    }
}
