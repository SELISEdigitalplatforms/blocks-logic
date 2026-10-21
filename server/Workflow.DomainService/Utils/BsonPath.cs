using MongoDB.Bson;

namespace Workflow.DomainService.Utils
{
    /// <summary>
    /// Get and remove values in a BSON document by path. With dot notation, "a.b.c" walks nested documents
    /// (segments are document keys only, never array indexes); without it, the path is one literal key.
    /// </summary>
    public static class BsonPath
    {
        public static bool TryGet(BsonDocument document, string path, bool dotNotation, out BsonValue value)
        {
            value = BsonNull.Value;
            if (!dotNotation)
            {
                if (!document.TryGetValue(path, out var literal)) return false;
                value = literal;
                return true;
            }

            BsonValue current = document;
            foreach (var segment in path.Split('.'))
            {
                if (current is not BsonDocument doc || !doc.TryGetValue(segment, out var next)) return false;
                current = next;
            }
            value = current;
            return true;
        }

        /// <summary>Removes the value at the path, if present. Returns whether something was removed.</summary>
        public static bool Remove(BsonDocument document, string path, bool dotNotation)
        {
            if (!dotNotation)
            {
                if (!document.Contains(path)) return false;
                document.Remove(path);
                return true;
            }

            var segments = path.Split('.');
            var parent = document;
            for (var i = 0; i < segments.Length - 1; i++)
            {
                if (!parent.TryGetValue(segments[i], out var next) || next is not BsonDocument nested) return false;
                parent = nested;
            }
            if (!parent.Contains(segments[^1])) return false;
            parent.Remove(segments[^1]);
            return true;
        }
    }
}
