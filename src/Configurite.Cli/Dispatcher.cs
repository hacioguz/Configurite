using Configurite.Cli.Commands;

namespace Configurite.Cli;

/// <summary>
/// EN: Top-level command dispatcher. Tests inject custom <see cref="TextWriter"/>s to capture output.
/// TR: Üst seviye komut yönlendirici. Testler çıktıyı yakalamak için özel <see cref="TextWriter"/>'lar enjekte eder.
/// </summary>
internal static class Dispatcher
{
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintUsage(stdout);
            return args.Length == 0 ? 1 : 0;
        }

        var verb = args[0];
        var rest = args.Skip(1).ToArray();

        try
        {
            return verb switch
            {
                "init" => InitCommand.Execute(rest, stdout, stderr),
                "migrate" => MigrateCommand.Execute(rest, stdout, stderr),
                "rotate" => RotateCommand.Execute(rest, stdout, stderr),
                "get" => GetCommand.Execute(rest, stdout, stderr),
                "set" => SetCommand.Execute(rest, stdout, stderr),
                "list" => ListCommand.Execute(rest, stdout, stderr),
                "delete" => DeleteCommand.Execute(rest, stdout, stderr),
                "audit" => AuditCommand.Execute(rest, stdout, stderr),
                "export" => ExportCommand.Execute(rest, stdout, stderr),
                _ => UnknownVerb(verb, stderr),
            };
        }
        catch (ArgumentException ex)
        {
            stderr.WriteLine("error: " + ex.Message);
            return 2;
        }
        catch (FileNotFoundException ex)
        {
            stderr.WriteLine("error: " + ex.Message);
            return 3;
        }
        catch (InvalidOperationException ex)
        {
            stderr.WriteLine("error: " + ex.Message);
            return 4;
        }
        catch (Exception ex)
        {
            stderr.WriteLine("error: " + ex.Message);
            return 1;
        }
    }

    private static bool IsHelp(string s) => s is "-h" or "--help" or "help";

    private static int UnknownVerb(string verb, TextWriter stderr)
    {
        stderr.WriteLine($"error: unknown command '{verb}'. Run 'configurite --help'.");
        return 2;
    }

    private static void PrintUsage(TextWriter w)
    {
        w.WriteLine("""
            configurite — encrypted SQLite configuration CLI

            USAGE
              configurite <command> [options]

            COMMANDS
              init     <db>                              Create the schema in <db>.
              migrate  <db> <json-or-dir> [opts]         Migrate appsettings*.json into <db>.
              rotate   <db> --old <key> --new <key>      Rotate the master key.
              get      <db> <key> [--env <name>]         Read a single value (decrypts if needed).
              set      <db> <key> <value> [opts]         Insert or update a value.
              list     <db> [--env <name>] [--reveal]    List rows; --reveal decrypts encrypted values.
              delete   <db> <key> [--env <name>]         Remove a row.
              audit    <db> [--limit N] [--follow]       Show recent audit log entries; --follow tails new ones.
              export   <db> <out> [--per-env] [opts]     Dump the database as JSON.

            OPTIONS
              --env <name>           Environment scope (Development, Production, …).
              --encrypt <pattern>    (migrate) Glob pattern whose values are encrypted. Repeatable.
              --master-key <key>     Override CONFIGURITE_MASTER_KEY.
              --no-overwrite         (migrate) Preserve existing rows.
              --decrypt              (export) Decrypt and write secrets as plaintext.
              --include-encrypted    (export) Write "(encrypted)" placeholder instead of skipping.
              --per-env              (export) Emit appsettings.json + appsettings.{Env}.json files.
              --limit N              (audit)  How many recent entries to print (default 20).
              --follow               (audit)  Tail the log for new entries (poll every 500 ms).

            ENVIRONMENT
              CONFIGURITE_MASTER_KEY  Default master key for encryption/decryption.

            EXAMPLES
              configurite init ./appsettings.db
              configurite migrate ./appsettings.db ./appsettings.json --encrypt "ConnectionStrings:*"
              configurite rotate ./appsettings.db --old "$OLD_KEY" --new "$NEW_KEY"
              configurite set ./appsettings.db Auth:ApiKey hunter2 --encrypt
              configurite get ./appsettings.db Auth:ApiKey
              configurite list ./appsettings.db --env Development
            """);
    }
}
