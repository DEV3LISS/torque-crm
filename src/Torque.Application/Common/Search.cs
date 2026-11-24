namespace Torque.Application.Common;

public static class Search
{
    /// <summary>
    /// Escapes LIKE wildcards in user input. Without it, searching for "50%" or "_" would match
    /// everything — not a security hole (EF parameterises the value), but wrong results.
    /// </summary>
    public static string ContainsPattern(string term) =>
        "%" + term.Replace(@"\", @"\\", StringComparison.Ordinal)
                  .Replace("%", @"\%", StringComparison.Ordinal)
                  .Replace("_", @"\_", StringComparison.Ordinal) + "%";
}
