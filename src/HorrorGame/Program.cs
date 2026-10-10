using HorrorGame;

if (args.Contains("--self-test"))
{
	try { SelfTest.Run(); }
	catch (Exception error)
	{
		Console.Error.WriteLine(error);
		Environment.ExitCode = 1;
	}
	return;
}
using var game = new Game();
game.Run(args.Contains("--smoke-test"));
