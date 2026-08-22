namespace NotifySync
{
    /// <summary>
    /// A container item that disappeared from the library, kept only to recognise a move.
    /// The type matters as much as the path: a series folder is identified by its own name,
    /// a season folder only together with its parent.
    /// </summary>
    public sealed class DeletedFolder
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DeletedFolder"/> class.
        /// </summary>
        /// <param name="path">Where the folder was.</param>
        /// <param name="type">Jellyfin's type for it — Series, Season or Folder.</param>
        public DeletedFolder(string path, string type)
        {
            Path = path;
            Type = type;
        }

        /// <summary>
        /// Gets where the folder was.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Gets Jellyfin's type for it.
        /// </summary>
        public string Type { get; }
    }
}
