using System.Collections.Generic;
using System.Linq;

namespace NotifySync
{
    /// <summary>
    /// Service to apply database and UI quotas ensuring groups (e.g., Series) count as a single slot.
    /// </summary>
    public static class CategoryQuotaService
    {
        /// <summary>
        /// Applies the maximum item quota per category, ensuring that items of the same Series
        /// only consume a single quota slot.
        /// </summary>
        /// <param name="sourceList">The initial list of items (most recent first expected).</param>
        /// <param name="maxItems">The maximum retention or display quota per category.</param>
        /// <returns>A tuple of kept items and removed item IDs.</returns>
        public static (List<NotificationItem> Kept, List<string> RemovedIds) ApplyCategoryQuotas(IEnumerable<NotificationItem> sourceList, int maxItems)
        {
            var categorized = sourceList.GroupBy(n => n.Category).ToList();
            var finalNotifications = new List<NotificationItem>();
            var itemsToDelete = new List<string>();

            foreach (var group in categorized)
            {
                // Ensure items are processed strictly newest first
                var sorted = group.OrderByDescending(n => n.DateCreated).ToList();
                var categorySeriesIds = new HashSet<string>();
                int currentCount = 0;

                foreach (var item in sorted)
                {
                    bool isEpisode = !string.IsNullOrEmpty(item.SeriesId);
                    bool keep = false;

                    if (isEpisode)
                    {
                        bool isNewSeries = !categorySeriesIds.Contains(item.SeriesId!);
                        if (!isNewSeries || currentCount < maxItems)
                        {
                            keep = true;
                            if (isNewSeries)
                            {
                                categorySeriesIds.Add(item.SeriesId!);
                                currentCount++;
                            }
                        }
                    }
                    else
                    {
                        if (currentCount < maxItems)
                        {
                            keep = true;
                            currentCount++;
                        }
                    }

                    if (keep)
                    {
                        finalNotifications.Add(item);
                    }
                    else
                    {
                        itemsToDelete.Add(item.Id);
                    }
                }
            }

            return (finalNotifications, itemsToDelete);
        }
    }
}
