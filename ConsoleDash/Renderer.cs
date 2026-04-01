using System.Text;

namespace ConsoleDash;

public static class Renderer
{
    // ANSI color codes
    private const string Reset       = "\x1b[0m";
    private const string White       = "\x1b[37m";
    private const string Gray        = "\x1b[90m";
    private const string Blue        = "\x1b[34m";
    private const string Cyan        = "\x1b[36m";
    private const string BrightCyan  = "\x1b[96m";
    private const string BrightGreen = "\x1b[92m";
    private const string BrightYellow= "\x1b[93m";
    private const string Magenta     = "\x1b[35m";
    private const string DimYellow   = "\x1b[2;33m";
    private const string LightBlue   = "\x1b[38;2;173;216;230m";

    private static bool _initialized;

    public static void Render(GameState game, Cell[,] grid, long counter)
    {
        bool animEven   = (counter % 2) == 0;
        int  animFrame  = (int)(counter % 3);

        var sb = new StringBuilder(capacity: (game.Width + 1) * game.Height * 10);

        if (!_initialized)
        {
            sb.Append("\x1b[2J"); // clear screen
            _initialized = true;
        }
        sb.Append("\x1b[H"); // cursor home

        for (int y = 0; y < game.Height; y++)
        {
            for (int x = 0; x < game.Width; x++)
            {
                Cell cell = grid[x, y];
                switch (cell.Tile)
                {
                    case Tile.Space:
                        sb.Append(' ');
                        break;

                    case Tile.Dirt:
                        sb.Append(DimYellow).Append('\xB7').Append(Reset); // middle dot
                        break;

                    case Tile.TitaniumWall:
                        sb.Append(White).Append('#').Append(Reset);
                        break;

                    case Tile.Wall:
                        sb.Append(Blue).Append('%').Append(Reset);
                        break;

                    case Tile.Rock:
                        sb.Append(Gray).Append('O').Append(Reset);
                        break;

                    case Tile.Diamond:
                        sb.Append(animEven ? BrightCyan : Cyan).Append('*').Append(Reset);
                        break;

                    case Tile.Firefly:
                        sb.Append(BrightYellow).Append(FireflyChar(animFrame)).Append(Reset);
                        break;

                    case Tile.Butterfly:
                        sb.Append(Magenta).Append(ButterflyChar(animFrame)).Append(Reset);
                        break;

                    case Tile.Amoeba:
                        sb.Append(BrightGreen).Append(animEven ? '~' : '-').Append(Reset);
                        break;

                    case Tile.MagicWall:
                        if (game.MagicWallActive)
                            sb.Append(Blue).Append(animEven ? '%' : '\xB0').Append(Reset);
                        else
                            sb.Append(Blue).Append('%').Append(Reset);
                        break;

                    case Tile.Slime:
                        sb.Append(LightBlue).Append(animEven ? '~' : '-').Append(Reset);
                        break;

                    case Tile.Rockford:
                        sb.Append(BrightGreen).Append('@').Append(Reset);
                        break;

                    case Tile.Exit:
                        bool open = game.DiamondsCollected >= game.DiamondsRequired;
                        if (open)
                            sb.Append(White).Append(animEven ? ' ' : '#').Append(Reset);
                        else
                            sb.Append(White).Append('#').Append(Reset);
                        break;

                    case Tile.Explosion:
                        int stage = Math.Min(cell.ExplosionStage, (byte)2);
                        if (cell.ExplosionSource == Tile.Butterfly)
                            sb.Append(Magenta).Append(ButterflyExplosionChar(stage)).Append(Reset);
                        else
                            sb.Append(BrightYellow).Append(FireflyExplosionChar(stage)).Append(Reset);
                        break;

                    default:
                        sb.Append('?');
                        break;
                }
            }
            sb.Append('\n');
        }

        // HUD
        sb.Append("Diamonds: ").Append(game.DiamondsCollected).Append('/').Append(game.DiamondsRequired);
        sb.Append("  Time: ").Append(game.TimeRemaining);

        if (game.PlayerWins)
            sb.Append("\nYOU WIN! Press Q to return to menu     ");
        else if (game.GameOver)
            sb.Append("  GAME OVER - Press Q to return         ");
        else
            sb.Append("  [WASD] Move  [Space] Reach  [Q] Quit ");

        Console.Write(sb);
    }

    private static char FireflyChar(int frame) => frame switch
    {
        0 => '|',
        1 => '<',
        _ => '>',
    };

    private static char ButterflyChar(int frame) => frame switch
    {
        0 => '|',
        1 => '(',
        _ => ')',
    };

    private static char FireflyExplosionChar(int stage) => stage switch
    {
        0 => '+',
        1 => 'x',
        _ => '*',
    };

    private static char ButterflyExplosionChar(int stage) => stage switch
    {
        0 => 'o',
        1 => 'O',
        _ => '@',
    };

    public static void ResetState() => _initialized = false;
}
