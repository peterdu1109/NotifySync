namespace NotifySync
{
    /// <summary>
    /// Represents a mapping between a library and a category name.
    /// </summary>
    public class CategoryMapping
    {
        /// <summary>
        /// Gets or sets the library identifier.
        /// </summary>
        public string LibraryId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the name of the category.
        /// </summary>
        public string CategoryName { get; set; } = string.Empty;
    }
}
