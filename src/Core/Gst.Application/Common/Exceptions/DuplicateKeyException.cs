namespace Gst.Application.Common.Exceptions
{
    // Unique index tootne par (SqlException 2601 / 2627) Persistence ye throw karta hai
    public sealed class DuplicateKeyException(string? indexName, Exception inner)
        : Exception("Duplicate key.", inner)
    {
        public string? IndexName { get; } = indexName;
    }
}
