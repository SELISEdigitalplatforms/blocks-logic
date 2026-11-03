namespace Blocks.FunctionRunner.Cli.Commands
{
    /// <summary>The two argument shapes fnctl uses: <c>--name value</c> and <c>--flag</c>.</summary>
    internal static class CliArgs
    {
        public static string? Option(string[] args, string name)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        public static bool Flag(string[] args, string name) => Array.IndexOf(args, name) >= 0;
    }
}
