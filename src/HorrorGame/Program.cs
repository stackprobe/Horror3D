using HorrorGame;

if (args.Contains("--self-test"))
{
    SelfTest.Run();
    return;
}
using var game = new Game();
game.Run(args.Contains("--smoke-test"));
