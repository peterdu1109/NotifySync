using System;

namespace NotifySync
{
    /// <summary>
    /// Static callback class invoked by File Transformation plugin
    /// to inject the NotifySync client script into index.html.
    /// </summary>
    public static class NotifySyncTransformation
    {
        private const string ScriptTag = "<script src=\"/NotifySync/client.js\"></script>";

        /// <summary>
        /// Called by File Transformation via reflection.
        /// Injects the client.js script tag before &lt;/body&gt;.
        /// </summary>
        /// <param name="payload">The file contents payload.</param>
        /// <returns>The modified HTML string.</returns>
        public static string Transform(FileTransformationPayload payload)
        {
            if (string.IsNullOrEmpty(payload.Contents))
            {
                return payload.Contents;
            }

            // Already injected — return as-is
            if (payload.Contents.Contains(ScriptTag, StringComparison.OrdinalIgnoreCase))
            {
                return payload.Contents;
            }

            int bodyIndex = payload.Contents.IndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            if (bodyIndex < 0)
            {
                return payload.Contents;
            }

            return payload.Contents.Insert(bodyIndex, "    " + ScriptTag + "\n");
        }
    }
}
