// EN: Entry point. Routes the verb (argv[0]) to its command handler.
// TR: Giriş noktası. Komutu (argv[0]) ilgili işleyiciye yönlendirir.

using Configurite.Cli;

return Dispatcher.Run(args, Console.Out, Console.Error);
