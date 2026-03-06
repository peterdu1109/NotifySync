namespace NotifySync
{
    /// <summary>
    /// Payload class matching File Transformation's callback parameter.
    /// The property 'Contents' receives the raw HTML of the intercepted file.
    /// </summary>
    public sealed class FileTransformationPayload
    {
        /// <summary>
        /// Gets or sets the HTML contents of the file being transformed.
        /// </summary>
        public string Contents { get; set; } = string.Empty;
    }
}
