using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Yata.Services;

namespace Yata;

internal static class Program
{
    /// <summary><c>yata-cli activity ...</c>; exit codes as in <see cref="ActivityCommand.Usage"/>.</summary>
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length == 0 || args[0] != ActivityCommand.Verb)
        {
            bool help = args.Length > 0 && args[0] is "-h" or "--help" or "help";
            (help ? Console.Out : Console.Error).WriteLine(ActivityCommand.Usage);
            return help ? 0 : 1;
        }
        return await ActivityCommand.RunAsync(args.Skip(1).ToArray(), Console.Error.WriteLine);
    }
}
