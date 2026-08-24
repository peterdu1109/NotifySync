using System;
using System.Linq;
using System.Text.RegularExpressions;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;

namespace NotifySync
{
    /// <summary>
    /// Decides what a change to the library actually was: a genuine upgrade, a file that merely
    /// moved, or neither.
    /// <para>
    /// Pulled out of <see cref="NotificationManager"/> because it is the one part of that class
    /// with no state and no dependency on Jellyfin's event plumbing — every answer here comes
    /// from paths, sizes and names alone. It is also the part that has to be right: a wrong
    /// answer either announces content the user already had, or silences content they never saw.
    /// </para>
    /// </summary>
    internal static class MediaChangeDetector
    {
        // Upgrade kind constants. Stored in NotificationItem.UpgradeKind and read by the
        // client to render a precise sub-label next to the UPD/MAJ badge.
        // We deliberately surface only three kinds; anything that doesn't match one of
        // these signals is treated as a non-upgrade (item stays in place, no badge).
        internal const string KindQuality = "quality";

        internal const string KindCodec = "codec";

        internal const string KindAudio = "audio";

        // Path tokens (lowercase) used to detect upgrade type from filename conventions.
        // Tokens are matched as standalone tags surrounded by non-alphanumeric separators
        // (dots, dashes, underscores, spaces) — see ContainsTag for the exact rule.
        internal static readonly string[] ResolutionUpTokens4K = { "2160p", "4k", "uhd" };

        internal static readonly string[] ResolutionUpTokens1080 = { "1080p" };

        internal static readonly string[] SourceBetterTokens = { "bluray", "blu-ray", "remux", "blueray" };

        // Audio-track-added tokens: when any of these appears in the new filename but not
        // in the old one, we flag MAJ • Audio. Covers FR variants (priority for the French
        // audience), multi-language markers, and generic "dubbed" markers. VOSTFR is NOT
        // here — it's subtitles-only, not an audio change.
        internal static readonly string[] AudioAddedTokens =
        {
            "vff", "vfq", "vfi", "vf", "truefrench", "french",
            "multi", "dual",
            "dubbed", "dub"
        };

        /// <summary>
        /// Classifies the type(s) of upgrade detected when replacing an existing notification's
        /// media file. Returns a comma-separated list of kinds (e.g. <c>"codec,audio"</c> or
        /// <c>"quality,codec,audio"</c>) in display priority order, or <c>null</c> when no
        /// meaningful change is detected. The client splits this and renders each kind as a
        /// localized label (e.g. "MAJ Codec + Audio"). Detection is purely filename-based —
        /// release-group naming conventions are the only reliable signal here. Size, bitrate,
        /// container, and pixel-dimension heuristics were tried (Phase B Lite) and produced
        /// too many false positives.
        /// </summary>
        /// <param name="existing">The copy that was there before.</param>
        /// <param name="updated">The copy that replaced it.</param>
        /// <returns>The kinds detected, comma-separated, or <c>null</c> for no real upgrade.</returns>
        internal static string? ClassifyUpgrade(NotificationItem existing, NotificationItem updated)
        {
            string oldPath = (existing.FilePath ?? string.Empty).ToLowerInvariant();
            string newPath = (updated.FilePath ?? string.Empty).ToLowerInvariant();

            // No path-based signal possible without a usable old path. The caller will
            // treat this as "unspecified MAJ" or, on the deleted-match path, decide to
            // skip the upgrade flag entirely.
            if (string.IsNullOrEmpty(oldPath) || oldPath == newPath)
            {
                return null;
            }

            // Size suppressor: when both file sizes are known and byte-for-byte identical,
            // this is the same file under a different name (a manual rename/move), not a
            // real replacement — suppress even if the filename tokens differ. An exact
            // size match never occurs for a genuine re-encode (which always changes size).
            // Only applies when both sizes exist; otherwise we fall back to token logic.
            if (existing.Size.HasValue && updated.Size.HasValue && existing.Size.Value == updated.Size.Value)
            {
                return null;
            }

            var kinds = new List<string>(3);

            // 1. Quality — resolution or source token differs (in either direction).
            //    Symmetric: 1080p → 4K and 4K → 1080p both qualify. Same for source
            //    (WEBRip ↔ BluRay).
            bool oldHas4K = ContainsAnyTag(oldPath, ResolutionUpTokens4K);
            bool newHas4K = ContainsAnyTag(newPath, ResolutionUpTokens4K);
            bool oldHas1080 = ContainsAnyTag(oldPath, ResolutionUpTokens1080);
            bool newHas1080 = ContainsAnyTag(newPath, ResolutionUpTokens1080);
            bool oldHasBetterSource = ContainsAnyTag(oldPath, SourceBetterTokens);
            bool newHasBetterSource = ContainsAnyTag(newPath, SourceBetterTokens);

            if (oldHas4K != newHas4K || oldHas1080 != newHas1080 || oldHasBetterSource != newHasBetterSource)
            {
                kinds.Add(KindQuality);
            }

            // 2. Codec — codec family changed in either direction (x264 ↔ HEVC ↔ AV1).
            string? oldCodec = DetectCodec(oldPath);
            string? newCodec = DetectCodec(newPath);
            if (oldCodec != newCodec && (oldCodec != null || newCodec != null))
            {
                kinds.Add(KindCodec);
            }

            // 3. Audio — any audio-track-added token appears in the new filename but not
            //    in the old one (asymmetric on purpose — removing a track isn't an upgrade).
            foreach (var token in AudioAddedTokens)
            {
                if (!ContainsTag(oldPath, token) && ContainsTag(newPath, token))
                {
                    kinds.Add(KindAudio);
                    break;
                }
            }

            return kinds.Count == 0 ? null : string.Join(",", kinds);
        }

        /// <summary>
        /// Extracts the dominant video codec family from a filename path.
        /// Returns "av1", "hevc", "x264", or null if no codec marker is present.
        /// Used by <see cref="ClassifyUpgrade"/> to detect codec transitions in any direction.
        /// </summary>
        /// <param name="path">The lowercased path to read.</param>
        /// <returns>The codec family, or <c>null</c> when the name carries no marker.</returns>
        internal static string? DetectCodec(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            if (ContainsTag(path, "av1"))
            {
                return "av1";
            }

            if (ContainsTag(path, "hevc") || ContainsTag(path, "x265") || ContainsTag(path, "h265") || ContainsTag(path, "h.265"))
            {
                return "hevc";
            }

            if (ContainsTag(path, "x264") || ContainsTag(path, "h264") || ContainsTag(path, "h.264") || ContainsTag(path, "avc"))
            {
                return "x264";
            }

            return null;
        }

        /// <summary>
        /// Returns true if <paramref name="path"/> contains any of the given <paramref name="tags"/>
        /// as a standalone token (delimited by start/end of string or non-alphanumeric separators).
        /// Prevents false matches like "vf" inside "movie name vfx" (extras).
        /// </summary>
        /// <param name="path">The lowercased path to search.</param>
        /// <param name="tags">The tokens to look for.</param>
        /// <returns><c>true</c> when any of them appears as a standalone token.</returns>
        internal static bool ContainsAnyTag(string path, string[] tags)
        {
            foreach (var tag in tags)
            {
                if (ContainsTag(path, tag))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Single-tag word-boundary match (case-insensitive — caller normalizes to lowercase).
        /// </summary>
        /// <param name="path">The lowercased path to search.</param>
        /// <param name="tag">The token to look for.</param>
        /// <returns><c>true</c> when it appears as a standalone token.</returns>
        internal static bool ContainsTag(string path, string tag)
        {
            string pattern = $"(?:^|[^a-z0-9]){Regex.Escape(tag)}(?:$|[^a-z0-9])";
            return Regex.IsMatch(path, pattern, RegexOptions.CultureInvariant);
        }

        /// <summary>
        /// True when the new file sits under a folder that was removed from somewhere else a
        /// moment ago — i.e. it did not arrive, it moved.
        /// <para>
        /// Jellyfin identifies items by path, so relocating a series produces removals for its
        /// CONTAINERS — the series folder, or only its season folders, depending on the scan —
        /// and never for the episodes, which then arrive as fresh adds. Nothing in that pair can
        /// be matched on metadata. What survives the move is the folder naming, present on both
        /// sides.
        /// </para>
        /// <para>
        /// A season folder is matched together with its parent, never alone: measured on a live
        /// server, the only removal announced for one move was ".../Star Wars Visions (2021)
        /// [tvdbid-393190]/Saison 01". Matching "Saison 01" on its own would have silenced new
        /// episodes of every other series that numbers its seasons the same way — which is all
        /// of them.
        /// </para>
        /// </summary>
        /// <param name="filePath">The new file's path.</param>
        /// <param name="deletedFolders">Containers removed recently, fetched once per batch.</param>
        /// <param name="matched">The folder that matched, for logging.</param>
        /// <returns><c>true</c> when this add is the tail of a move.</returns>
        internal static bool CameFromDeletedFolder(string? filePath, DeletedFolder[] deletedFolders, out string matched)
        {
            matched = string.Empty;
            if (string.IsNullOrEmpty(filePath) || deletedFolders.Length == 0)
            {
                return false;
            }

            var segments = filePath.Split('/', '\\');

            foreach (var folder in deletedFolders)
            {
                string trimmed = folder.Path.TrimEnd('/', '\\');
                var folderSegments = trimmed.Split('/', '\\');

                // How much of the removed path has to be recognised. A series or artist folder
                // carries its own title, so its last segment identifies it. A season is called
                // the same thing under every series, and album folders named "Greatest Hits" or
                // "Live" are shared by dozens of artists — those only mean something with their
                // parent, or they would silence new content belonging to someone else entirely.
                bool needsParent = string.Equals(folder.Type, "Season", StringComparison.Ordinal)
                    || string.Equals(folder.Type, "MusicAlbum", StringComparison.Ordinal);
                int depth = needsParent && folderSegments.Length >= 2 ? 2 : 1;
                if (folderSegments.Length < depth)
                {
                    continue;
                }

                // Those segments have to appear consecutively among the new file's ancestors —
                // the last segment is the file itself and cannot be a folder.
                for (int i = 0; i + depth <= segments.Length - 1; i++)
                {
                    bool allMatch = true;
                    for (int k = 0; k < depth; k++)
                    {
                        string expected = folderSegments[folderSegments.Length - depth + k];
                        if (expected.Length == 0 || !string.Equals(expected, segments[i + k], StringComparison.OrdinalIgnoreCase))
                        {
                            allMatch = false;
                            break;
                        }
                    }

                    if (!allMatch)
                    {
                        continue;
                    }

                    // Rebuilt at the same absolute location means the container never went
                    // anywhere — a refresh that dropped and re-created it in place.
                    string rebuilt = string.Join('/', segments.Take(i + depth));
                    if (string.Equals(rebuilt, trimmed.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    matched = folder.Path;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True when two copies of a title are byte-for-byte the same size — the signature of
        /// a file that was moved or renamed rather than replaced. A genuine re-encode always
        /// changes the size, so this never masks a real upgrade.
        /// </summary>
        /// <param name="deletedSize">Size of the copy that disappeared.</param>
        /// <param name="addedSize">Size of the copy that appeared.</param>
        /// <returns><c>true</c> when both sizes are known and identical.</returns>
        internal static bool IsSameFileBackAgain(long? deletedSize, long? addedSize)
            => deletedSize.HasValue && addedSize.HasValue && deletedSize.Value > 0 && deletedSize.Value == addedSize.Value;

        /// <summary>
        /// True when an existing notification describes the same title as the item being
        /// removed, under a different id and with an identical size — i.e. the file has
        /// already been re-added somewhere else and this removal is the tail of a move.
        /// </summary>
        /// <param name="candidate">An existing notification.</param>
        /// <param name="removed">The item Jellyfin is removing.</param>
        /// <returns><c>true</c> when the notification is the twin of the removed item.</returns>
        internal static bool IsSameFileReadded(NotificationItem candidate, BaseItem removed)
        {
            if (!IsSameFileBackAgain(removed.Size, candidate.Size))
            {
                return false;
            }

            string removedType = removed.GetType().Name;
            if (!string.Equals(candidate.Type, removedType, StringComparison.Ordinal))
            {
                return false;
            }

            // Episodes are identified by their slot in the series, not by their title:
            // the same episode can carry a different name across libraries (TBA, VO/VF).
            if (removed is Episode episode)
            {
                return !string.IsNullOrEmpty(candidate.SeriesName)
                    && string.Equals(candidate.SeriesName, episode.SeriesName, StringComparison.OrdinalIgnoreCase)
                    && candidate.IndexNumber == removed.IndexNumber
                    && candidate.ParentIndexNumber == removed.ParentIndexNumber;
            }

            return !string.IsNullOrEmpty(removed.Name)
                && string.Equals(candidate.Name, removed.Name, StringComparison.OrdinalIgnoreCase)
                && candidate.ProductionYear == removed.ProductionYear;
        }
    }
}
