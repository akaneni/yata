using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Yata.Services;

namespace Yata;

internal static class Program
{
    private const string Usage = """
        yata-cli: the console side of Yata (https://getyata.app), for scripts and AI agents.

          yata-cli activity ...                    show a task in the notch (yata-cli activity --help)
          yata-cli hook claude|cursor|codex        called by an agent's hooks: its state in the notch
          yata-cli hook setup claude|cursor|codex  print the hooks configuration to paste
          yata-cli skill                           print the agent skill (SKILL.md)
          yata-cli skill install                   install it for Claude Code, Cursor and Codex
        """;

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        switch (args.FirstOrDefault())
        {
            case ActivityCommand.Verb:
                return await ActivityCommand.RunAsync(args.Skip(1).ToArray(), Console.Error.WriteLine);
            case "hook" when args.Length >= 3 && args[1] == "setup":
                return Print(AgentHook.Setup(args[2]));
            case "hook":
                return await Hook(args);
            case "skill" when args.Length >= 2 && args[1] == "install":
                return InstallSkill();
            case "skill":
                Console.Out.Write(Skill());
                return 0;
            case "-h" or "--help" or "help":
                Console.Out.WriteLine(Usage);
                return 0;
            default:
                Console.Error.WriteLine(Usage);
                return 1;
        }
    }

    /// <summary>
    /// Never in the agent's way: always exit 0, print only the neutral answer the event expects,
    /// and give up quietly if Yata isn't running or anything goes wrong.
    /// </summary>
    private static async Task<int> Hook(string[] args)
    {
        try
        {
            string agent = args.Length > 1 ? args[1] : "";
            if (!AgentHook.Agents.Contains(agent))
            {
                Console.Error.WriteLine($"yata-cli hook: unknown agent \"{agent}\" (use {string.Join(", ", AgentHook.Agents)})");
                return 0;
            }
            // The raw bytes as UTF-8, not Console.In: that decodes with the console's code page, which
            // garbles accented folder names and the byte-order mark PowerShell puts before what it pipes.
            using var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false),
                detectEncodingFromByteOrderMarks: true);
            string? payload = AgentHook.ReadPayload(args.Skip(2).ToArray(), stdin, Console.IsInputRedirected);
            var result = AgentHook.Translate(agent, payload);
            if (result.Output != null)
                Console.Out.Write(result.Output);
            if (result.Request != null)
                await ActivityClient.SendAsync(result.Request);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("yata-cli hook: " + ex.Message);
        }
        return 0;
    }

    private static int Print(string? text)
    {
        if (text == null)
        {
            Console.Error.WriteLine(Usage);
            return 1;
        }
        Console.Out.WriteLine(text);
        return 0;
    }

    private static string Skill()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SKILL.md")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Where the three agents look for personal skills: Claude Code reads ~/.claude/skills; Codex
    /// reads ~/.agents/skills; Cursor reads both.
    /// </summary>
    private static int InstallSkill()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string skill = Skill();
        foreach (string root in new[] { ".claude", ".agents" })
        {
            string dir = Path.Combine(home, root, "skills", "yata-notch");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "SKILL.md");
            File.WriteAllText(file, skill, new UTF8Encoding(false));
            Console.Out.WriteLine(file);
        }
        return 0;
    }
}
