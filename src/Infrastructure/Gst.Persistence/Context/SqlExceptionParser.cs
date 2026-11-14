using System.Text.RegularExpressions;

namespace Gst.Persistence.Context
{
    // SqlException 2601 / 2627 ke message se unique index / constraint ka naam nikalta hai
    internal static partial class SqlExceptionParser
    {
        [GeneratedRegex("unique index '([^']+)'")]
        private static partial Regex IndexRegex();

        [GeneratedRegex("constraint '([^']+)'")]
        private static partial Regex ConstraintRegex();

        internal static string? GetIndexName(int number, string message)
        {
            var match = number == 2601 ? IndexRegex().Match(message) : ConstraintRegex().Match(message);
            return match.Success ? match.Groups[1].Value : null;
        }
    }
}
