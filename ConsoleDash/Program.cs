using ConsoleDash;

Console.CursorVisible = false;
Console.OutputEncoding = System.Text.Encoding.UTF8;

string? levelsDir = FindLevelsDir();
if (levelsDir is null)
{
    Console.WriteLine("Could not find 'levels' directory.");
    return;
}

string[] levelFiles = Directory.GetFiles(levelsDir, "level_*.txt");
Array.Sort(levelFiles);

if (levelFiles.Length == 0)
{
    Console.WriteLine("No level files found in: " + levelsDir);
    return;
}

while (true)
{
    int selectedLevel = ShowMenu(levelFiles);
    if (selectedLevel < 0) break;

    LevelData? levelData = null;
    try { levelData = LevelLoader.LoadFromFile(levelFiles[selectedLevel]); }
    catch (Exception ex) { Console.WriteLine("Failed to load level: " + ex.Message); continue; }

    Renderer.ResetState();
    PlayLevel(levelData);
}

Console.CursorVisible = true;
Console.Clear();

// ── Menu ──────────────────────────────────────────────────────────────────────

static int ShowMenu(string[] files)
{
    Console.Clear();
    Console.WriteLine("  ╔══════════════════════════════╗");
    Console.WriteLine("  ║       B O U L D E R          ║");
    Console.WriteLine("  ║         D A S H              ║");
    Console.WriteLine("  ╚══════════════════════════════╝");
    Console.WriteLine();
    Console.WriteLine("  Select a level (1-" + files.Length + "):");
    Console.WriteLine();
    for (int i = 0; i < files.Length; i++)
        Console.WriteLine("    " + (i + 1) + ". " + Path.GetFileNameWithoutExtension(files[i]));
    Console.WriteLine();
    Console.WriteLine("    Q. Quit");
    Console.WriteLine();

    while (true)
    {
        if (!Console.KeyAvailable) { Thread.Sleep(16); continue; }
        var key = Console.ReadKey(intercept: true);
        if (key.KeyChar == 'q' || key.KeyChar == 'Q' || key.Key == ConsoleKey.Escape)
            return -1;
        if (key.KeyChar >= '1' && key.KeyChar <= '9')
        {
            int idx = key.KeyChar - '1';
            if (idx < files.Length) return idx;
        }
    }
}

// ── Game loop ─────────────────────────────────────────────────────────────────

static void PlayLevel(LevelData data)
{
    var game = new GameState();
    game.LoadLevel(data);

    long animCounter = 0;
    bool animStop    = false;

    var animThread = new Thread(() =>
    {
        var next = DateTime.UtcNow;
        while (!Volatile.Read(ref animStop))
        {
            long c = Interlocked.Increment(ref animCounter);
            game.Render(c);
            next += TimeSpan.FromMilliseconds((double)game.AnimationTickInterval);
            var sleep = next - DateTime.UtcNow;
            if (sleep > TimeSpan.Zero) Thread.Sleep(sleep);
        }
    });
    animThread.IsBackground = true;
    animThread.Start();

    bool reachArmed   = false;
    var  reachArmedAt = DateTime.UtcNow;
    const int reachTimeoutMs = 350;

    bool playerQuit = false;
    var  nextTick   = DateTime.UtcNow;

    // ── Main game loop ────────────────────────────────────────────────────────
    while (game.IsAlive)
    {
        SampleInput(ref reachArmed, ref reachArmedAt, reachTimeoutMs,
            out int dx, out int dy, out bool reach, out bool quit);
        if (quit) { playerQuit = true; break; }

        game.SetInput(dx, dy, reach);
        game.Tick();
        game.Render(Interlocked.Read(ref animCounter));

        nextTick += TimeSpan.FromMilliseconds((double)game.GameTickInterval);
        var sleep = nextTick - DateTime.UtcNow;
        if (sleep > TimeSpan.Zero) Thread.Sleep(sleep);
    }

    // ── Post-death: world keeps simulating until Q ────────────────────────────
    while (game.GameOver && !playerQuit)
    {
        SampleInput(ref reachArmed, ref reachArmedAt, reachTimeoutMs,
            out _, out _, out _, out bool quit);
        if (quit) { playerQuit = true; break; }

        game.SetInput(0, 0, false);
        game.Tick();

        nextTick += TimeSpan.FromMilliseconds((double)game.GameTickInterval);
        var sleep = nextTick - DateTime.UtcNow;
        if (sleep > TimeSpan.Zero) Thread.Sleep(sleep);
    }

    // ── Win: animation continues, just wait for Q ─────────────────────────────
    while (game.PlayerWins && !playerQuit)
    {
        SampleInput(ref reachArmed, ref reachArmedAt, reachTimeoutMs,
            out _, out _, out _, out bool quit);
        if (quit) { playerQuit = true; break; }
        Thread.Sleep(16);
    }

    Volatile.Write(ref animStop, true);
    animThread.Join();
}

// ── Input ─────────────────────────────────────────────────────────────────────

static void SampleInput(ref bool reachArmed, ref DateTime reachArmedAt, int reachTimeoutMs,
    out int dx, out int dy, out bool reach, out bool quit)
{
    dx = 0; dy = 0; reach = false; quit = false;

    if (reachArmed && (DateTime.UtcNow - reachArmedAt).TotalMilliseconds > reachTimeoutMs)
        reachArmed = false;

    while (Console.KeyAvailable)
    {
        var key = Console.ReadKey(intercept: true);

        if (key.KeyChar == 'q' || key.KeyChar == 'Q' || key.Key == ConsoleKey.Escape)
        { quit = true; return; }

        if (key.KeyChar == ' ')
        {
            reachArmed   = true;
            reachArmedAt = DateTime.UtcNow;
            continue;
        }

        int ndx = 0, ndy = 0;
        switch (key.KeyChar)
        {
            case 'w': case 'W': ndy = -1; break;
            case 's': case 'S': ndy =  1; break;
            case 'a': case 'A': ndx = -1; break;
            case 'd': case 'D': ndx =  1; break;
            default:
                switch (key.Key)
                {
                    case ConsoleKey.UpArrow:    ndy = -1; break;
                    case ConsoleKey.DownArrow:  ndy =  1; break;
                    case ConsoleKey.LeftArrow:  ndx = -1; break;
                    case ConsoleKey.RightArrow: ndx =  1; break;
                }
                break;
        }
        if (ndx == 0 && ndy == 0) continue;

        dx    = ndx;
        dy    = ndy;
        reach = reachArmed;
        reachArmed = false;
    }
}

// ── Utilities ─────────────────────────────────────────────────────────────────

static string? FindLevelsDir()
{
    string[] candidates =
    [
        Path.Combine(AppContext.BaseDirectory, "levels"),
        Path.Combine(Directory.GetCurrentDirectory(), "levels"),
    ];
    foreach (string c in candidates)
        if (Directory.Exists(c)) return c;
    return null;
}
