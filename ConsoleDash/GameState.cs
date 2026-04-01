namespace ConsoleDash;

public class GameState
{
    private const int MaxWidth  = 100;
    private const int MaxHeight = 50;

    private readonly Cell[,] _grid  = new Cell[MaxWidth, MaxHeight];
    private readonly bool[,] _moved = new bool[MaxWidth, MaxHeight];
    private readonly object  _lock  = new();
    private readonly Random  _rng   = new();

    // ── Level configuration ──────────────────────────────────────────────────
    public int    Width               { get; private set; }
    public int    Height              { get; private set; }
    public string LevelName           { get; private set; } = "";
    public int    DiamondsRequired    { get; private set; }
    public int    TimeLimit           { get; private set; }
    public int    GameTickInterval    { get; private set; }
    public int    AnimationTickInterval { get; private set; }

    // ── Runtime state (all reads go through the public properties) ───────────
    public int  RockfordX          { get; private set; }
    public int  RockfordY          { get; private set; }
    public int  DiamondsCollected  { get; private set; }
    public int  TimeRemaining      { get; private set; }
    public bool GameOver           { get; private set; }
    public bool PlayerWins         { get; private set; }
    public bool IsAlive            => !GameOver && !PlayerWins;

    // magic wall
    private int  _magicWallTimer;
    private bool _magicWallExhausted;
    private int  _magicWallDuration;
    public  bool MagicWallActive   => _magicWallTimer > 0;

    // amoeba
    private int _amoebaCurrentSize;
    private int _amoebaMaxSize;
    private int _amoebaGrowthFactor;

    // slime
    private int _slimePermeability;

    // pending input (set by caller, consumed by Tick)
    private int  _pendingDx;
    private int  _pendingDy;
    private bool _pendingReach;

    // time tracking
    private DateTime _lastTimeUpdate;

    // ── Public API ───────────────────────────────────────────────────────────

    public void LoadLevel(LevelData data)
    {
        lock (_lock)
        {
            Width               = data.Width;
            Height              = data.Height;
            LevelName           = data.Name;
            DiamondsRequired    = data.DiamondsRequired;
            TimeLimit           = data.TimeLimit;
            TimeRemaining       = data.TimeLimit;
            GameTickInterval    = data.GameTickInterval;
            AnimationTickInterval = data.AnimationTickInterval;

            _amoebaMaxSize      = Math.Max(1, data.AmoebaMaxSize);
            _amoebaGrowthFactor = Math.Max(1, data.AmoebaGrowthFactor);
            _amoebaCurrentSize  = 0;

            _magicWallDuration  = data.MagicWallDuration;
            _magicWallTimer     = 0;
            _magicWallExhausted = false;

            _slimePermeability  = data.SlimePermeabilityValue;

            DiamondsCollected   = 0;
            GameOver            = false;
            PlayerWins          = false;
            RockfordX           = data.RockfordX;
            RockfordY           = data.RockfordY;

            _pendingDx = _pendingDy = 0;
            _pendingReach = false;

            Array.Clear(_moved);
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    _grid[x, y] = data.Grid[x, y];

            _lastTimeUpdate = DateTime.UtcNow;
        }
    }

    public void SetInput(int dx, int dy, bool reach)
    {
        lock (_lock)
        {
            _pendingDx    = dx;
            _pendingDy    = dy;
            _pendingReach = reach;
        }
    }

    public void Tick()
    {
        lock (_lock)
        {
            // ── Update time ──────────────────────────────────────────────────
            var now     = DateTime.UtcNow;
            int elapsed = (int)(now - _lastTimeUpdate).TotalSeconds;
            if (elapsed > 0)
            {
                if (!PlayerWins)
                {
                    if (!GameOver)
                    {
                        if (elapsed >= TimeRemaining)
                        {
                            TimeRemaining = 0;
                            GameOver      = true;
                        }
                        else
                        {
                            TimeRemaining -= elapsed;
                        }
                    }
                    else
                    {
                        // Keep scrolling after death so the HUD looks alive
                        TimeRemaining = Math.Max(0, TimeRemaining - elapsed);
                    }
                }
                _lastTimeUpdate += TimeSpan.FromSeconds(elapsed);
            }

            // ── Advance explosions first (same order as C++) ─────────────────
            AdvanceExplosions();

            // ── Clear moved flags ────────────────────────────────────────────
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    _moved[x, y] = false;

            // ── Process player input ─────────────────────────────────────────
            if (!GameOver && !PlayerWins)
            {
                if (_pendingReach)
                    TryReachRockford(_pendingDx, _pendingDy);
                else
                    TryMoveRockford(_pendingDx, _pendingDy);
            }
            _pendingDx = _pendingDy = 0;
            _pendingReach = false;

            // ── Process world ────────────────────────────────────────────────
            // If already dead, keep simulating (for explosions / creature movement).
            // If player dies during this tick, stop immediately.
            bool wasDead = GameOver;
            for (int y = 0; y < Height && (wasDead || !GameOver) && !PlayerWins; y++)
                for (int x = 0; x < Width; x++)
                    if (!_moved[x, y])
                        ProcessCell(x, y);

            // ── Post-tick amoeba ─────────────────────────────────────────────
            PostTickAmoeba();

            // ── Magic wall timer ─────────────────────────────────────────────
            if (_magicWallTimer > 0 && --_magicWallTimer == 0)
                _magicWallExhausted = true;
        }
    }

    /// <summary>Read grid state for rendering (must be called while holding lock or accept a snapshot).</summary>
    public void Render(long animationCounter)
    {
        lock (_lock)
        {
            Renderer.Render(this, _grid, animationCounter);
        }
    }

    // ── Cell processing ──────────────────────────────────────────────────────

    private void ProcessCell(int x, int y)
    {
        switch (_grid[x, y].Tile)
        {
            case Tile.Rock:
            case Tile.Diamond:
                ProcessFalling(x, y);
                break;
            case Tile.Firefly:
                ProcessFirefly(x, y);
                break;
            case Tile.Butterfly:
                ProcessButterfly(x, y);
                break;
            case Tile.Amoeba:
                TryAmoebaGrow(x, y);
                break;
            // Explosions, walls, space, dirt, exit: no per-tick logic
        }
    }

    // ── Physics ──────────────────────────────────────────────────────────────

    private void ProcessFalling(int x, int y)
    {
        Cell cell      = _grid[x, y];
        bool isDiamond = cell.Tile == Tile.Diamond;
        int  nx        = x;
        int  ny        = y + 1;

        if (!InBounds(nx, ny)) { cell.WasFalling = false; _grid[x, y] = cell; return; }

        Tile below = _grid[nx, ny].Tile;

        // ── Rockford below ───────────────────────────────────────────────────
        if (below == Tile.Rockford)
        {
            if (cell.WasFalling) { ExplodeAt(nx, ny, Tile.Space, Tile.Space); GameOver = true; }
            else { cell.WasFalling = false; _grid[x, y] = cell; }
            return;
        }

        // ── Empty space: fall ────────────────────────────────────────────────
        if (below == Tile.Space)
        {
            cell.WasFalling  = true;
            _grid[nx, ny]    = cell;
            _grid[x, y]      = default;
            MarkMoved(x, y);
            MarkMoved(nx, ny);
            return;
        }

        // ── Creature below: explode if falling ───────────────────────────────
        if (below == Tile.Firefly)
        {
            if (cell.WasFalling) { ExplodeFirefly(nx, ny); MarkMoved(x, y); MarkMoved(nx, ny); }
            else { cell.WasFalling = false; _grid[x, y] = cell; }
            return;
        }
        if (below == Tile.Butterfly)
        {
            if (cell.WasFalling) { ExplodeButterfly(nx, ny); MarkMoved(x, y); MarkMoved(nx, ny); }
            else { cell.WasFalling = false; _grid[x, y] = cell; }
            return;
        }

        // ── Slime ────────────────────────────────────────────────────────────
        if (below == Tile.Slime)
        {
            int randNum = _slimePermeability > 0
                ? _rng.Next(0, _slimePermeability + 1)
                : 0;
            if (randNum == 0)
            {
                int tx = nx, ty = ny + 1;
                if (InBounds(tx, ty) && _grid[tx, ty].Tile == Tile.Space)
                {
                    cell.WasFalling = true;
                    _grid[tx, ty]   = cell;
                    _grid[x, y]     = default;
                    MarkMoved(tx, ty);
                    MarkMoved(x, y);
                    return;
                }
            }
            cell.WasFalling = false;
            _grid[x, y] = cell;
            return;
        }

        // ── Magic wall ───────────────────────────────────────────────────────
        if (below == Tile.MagicWall)
        {
            if (cell.WasFalling)
            {
                if (!_magicWallExhausted)
                {
                    _magicWallTimer = _magicWallDuration; // reset (not add)
                    Tile converted  = isDiamond ? Tile.Rock : Tile.Diamond;
                    int tx = nx, ty = ny + 1;
                    if (InBounds(tx, ty) && _grid[tx, ty].Tile == Tile.Space)
                    {
                        _grid[tx, ty] = new Cell { Tile = converted, WasFalling = true };
                        MarkMoved(tx, ty);
                    }
                }
                // Object is always consumed (active or exhausted)
                _grid[x, y] = default;
                MarkMoved(x, y);
            }
            else
            {
                cell.WasFalling = false;
                _grid[x, y] = cell;
            }
            return;
        }

        // ── Solid surface: try rolling ───────────────────────────────────────
        if (!CanRollOver(nx, ny)) { cell.WasFalling = false; _grid[x, y] = cell; return; }

        bool downLeft  = CanRollInto(x - 1, y + 1);
        bool downRight = CanRollInto(x + 1, y + 1);
        bool leftFree  = !IsBlocking(x - 1, y);
        bool rightFree = !IsBlocking(x + 1, y);

        // Roll moves to (x±1, y) at the same height with WasFalling=true;
        // gravity will pull it down into the empty (x±1, y+1) next tick.
        if (downLeft && leftFree)
        {
            cell.WasFalling    = true;
            _grid[x - 1, y]   = cell;
            _grid[x, y]        = default;
            MarkMoved(x, y);
            MarkMoved(x - 1, y);
            return;
        }
        if (downRight && rightFree)
        {
            cell.WasFalling    = true;
            _grid[x + 1, y]   = cell;
            _grid[x, y]        = default;
            MarkMoved(x, y);
            MarkMoved(x + 1, y);
            return;
        }

        cell.WasFalling = false;
        _grid[x, y] = cell;
    }

    // ── Creature movement ────────────────────────────────────────────────────
    // Direction encoding: 0=Up, 1=Left, 2=Down, 3=Right
    private static readonly int[] CDx = { 0, -1, 0, 1 };
    private static readonly int[] CDy = { -1, 0, 1, 0 };
    private static byte TurnLeft(byte f)  => (byte)((f + 1) % 4); // counter-clockwise
    private static byte TurnRight(byte f) => (byte)((f + 3) % 4); // clockwise

    private void ProcessFirefly(int x, int y)
    {
        // Kill Rockford if adjacent
        for (int d = 0; d < 4; d++)
        {
            int ax = x + CDx[d], ay = y + CDy[d];
            if (InBounds(ax, ay) && _grid[ax, ay].Tile == Tile.Rockford)
            {
                ExplodeFirefly(x, y);
                GameOver = true;
                return;
            }
        }
        // Die on amoeba adjacency
        for (int d = 0; d < 4; d++)
        {
            int ax = x + CDx[d], ay = y + CDy[d];
            if (InBounds(ax, ay) && _grid[ax, ay].Tile == Tile.Amoeba)
            {
                ExplodeFirefly(x, y);
                return;
            }
        }

        byte facing = _grid[x, y].Facing;
        byte left   = TurnLeft(facing);

        int lx = x + CDx[left], ly = y + CDy[left]; // try turn left
        int fx = x + CDx[facing], fy = y + CDy[facing]; // try forward

        if (InBounds(lx, ly) && _grid[lx, ly].Tile == Tile.Space)
        {
            SetCell(lx, ly, Tile.Firefly, left, false);
            ClearCell(x, y); MarkMoved(x, y); MarkMoved(lx, ly);
            return;
        }
        if (InBounds(fx, fy) && _grid[fx, fy].Tile == Tile.Space)
        {
            SetCell(fx, fy, Tile.Firefly, facing, false);
            ClearCell(x, y); MarkMoved(x, y); MarkMoved(fx, fy);
            return;
        }
        // Turn right in place
        _grid[x, y].Facing = TurnRight(facing);
    }

    private void ProcessButterfly(int x, int y)
    {
        // Kill Rockford if adjacent
        for (int d = 0; d < 4; d++)
        {
            int ax = x + CDx[d], ay = y + CDy[d];
            if (InBounds(ax, ay) && _grid[ax, ay].Tile == Tile.Rockford)
            {
                ExplodeButterfly(x, y);
                GameOver = true;
                return;
            }
        }
        // Die on amoeba adjacency
        for (int d = 0; d < 4; d++)
        {
            int ax = x + CDx[d], ay = y + CDy[d];
            if (InBounds(ax, ay) && _grid[ax, ay].Tile == Tile.Amoeba)
            {
                ExplodeButterfly(x, y);
                return;
            }
        }

        byte facing = _grid[x, y].Facing;
        byte right  = TurnRight(facing);

        int rx = x + CDx[right], ry = y + CDy[right]; // try turn right
        int fx = x + CDx[facing], fy = y + CDy[facing]; // try forward

        if (InBounds(rx, ry) && _grid[rx, ry].Tile == Tile.Space)
        {
            SetCell(rx, ry, Tile.Butterfly, right, false);
            ClearCell(x, y); MarkMoved(x, y); MarkMoved(rx, ry);
            return;
        }
        if (InBounds(fx, fy) && _grid[fx, fy].Tile == Tile.Space)
        {
            SetCell(fx, fy, Tile.Butterfly, facing, false);
            ClearCell(x, y); MarkMoved(x, y); MarkMoved(fx, fy);
            return;
        }
        // Turn left in place
        _grid[x, y].Facing = TurnLeft(facing);
    }

    // ── Amoeba ───────────────────────────────────────────────────────────────

    private void TryAmoebaGrow(int x, int y)
    {
        int growthUpper = Math.Max(0,
            _amoebaGrowthFactor - (_amoebaCurrentSize / _amoebaMaxSize * _amoebaGrowthFactor));
        int roll = _rng.Next(0, growthUpper + 1);
        if (roll != 0) return;

        // Shuffle directions
        int[] order = { 0, 1, 2, 3 };
        for (int i = 3; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        foreach (int d in order)
        {
            int nx = x + CDx[d], ny = y + CDy[d];
            if (!InBounds(nx, ny)) continue;
            Tile t = _grid[nx, ny].Tile;
            if (t == Tile.Space || t == Tile.Dirt)
            {
                _grid[nx, ny] = new Cell { Tile = Tile.Amoeba };
                MarkMoved(nx, ny);
                return;
            }
        }
    }

    private void PostTickAmoeba()
    {
        _amoebaCurrentSize = 0;
        bool hasGrowthOption = false;

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                if (_grid[x, y].Tile != Tile.Amoeba) continue;
                _amoebaCurrentSize++;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + CDx[d], ny = y + CDy[d];
                    if (!InBounds(nx, ny)) continue;
                    Tile t = _grid[nx, ny].Tile;
                    if (t == Tile.Space || t == Tile.Dirt) hasGrowthOption = true;
                }
            }
        }

        if (_amoebaCurrentSize >= _amoebaMaxSize)
        {
            ConvertAllAmoeba(Tile.Rock);
            return;
        }
        if (!hasGrowthOption && _amoebaCurrentSize > 0)
            ConvertAllAmoeba(Tile.Diamond);
    }

    private void ConvertAllAmoeba(Tile to)
    {
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                if (_grid[x, y].Tile == Tile.Amoeba)
                    _grid[x, y] = new Cell { Tile = to };
    }

    // ── Explosions ───────────────────────────────────────────────────────────

    private void AdvanceExplosions()
    {
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                if (_grid[x, y].Tile != Tile.Explosion) continue;
                if (_grid[x, y].ExplosionStage < 2)
                {
                    _grid[x, y].ExplosionStage++;
                }
                else
                {
                    Tile result = _grid[x, y].ExplosionResult;
                    _grid[x, y] = new Cell { Tile = result };
                }
            }
        }
    }

    private void ExplodeFirefly(int x, int y)  => ExplodeAt(x, y, Tile.Firefly,   Tile.Space);
    private void ExplodeButterfly(int x, int y) => ExplodeAt(x, y, Tile.Butterfly, Tile.Diamond);

    private void ExplodeAt(int cx, int cy, Tile source, Tile fill)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int x = cx + dx, y = cy + dy;
                if (!InBounds(x, y)) continue;
                if (_grid[x, y].Tile == Tile.TitaniumWall) continue;
                if (_grid[x, y].Tile == Tile.Rockford) GameOver = true;
                _grid[x, y] = new Cell
                {
                    Tile            = Tile.Explosion,
                    ExplosionStage  = 0,
                    ExplosionSource = source,
                    ExplosionResult = fill,
                };
                MarkMoved(x, y);
            }
        }
    }

    // ── Player movement ──────────────────────────────────────────────────────

    private void TryMoveRockford(int dx, int dy)
    {
        if (dx == 0 && dy == 0) return;
        int tx = RockfordX + dx, ty = RockfordY + dy;
        if (!InBounds(tx, ty)) return;

        Tile target = _grid[tx, ty].Tile;

        if (target == Tile.Space || target == Tile.Dirt)
        {
            MoveRockford(tx, ty);
            return;
        }
        if (target == Tile.Diamond)
        {
            if (_grid[tx, ty].WasFalling) { ExplodeAt(RockfordX, RockfordY, Tile.Space, Tile.Space); GameOver = true; return; }
            DiamondsCollected++;
            MoveRockford(tx, ty);
            return;
        }
        if (target == Tile.Exit)
        {
            if (DiamondsCollected >= DiamondsRequired)
            {
                MoveRockford(tx, ty);
                PlayerWins = true;
            }
            return;
        }
        if (target == Tile.Rock && dy == 0)
        {
            int bx = tx + dx, by = ty;
            if (InBounds(bx, by) && CanRollInto(bx, by) && !_grid[tx, ty].WasFalling)
            {
                SetCell(bx, by, Tile.Rock, 0, false);
                ClearCell(RockfordX, RockfordY);
                ClearCell(tx, ty);
                SetCell(tx, ty, Tile.Rockford, 0, false);
                MarkMoved(RockfordX, RockfordY);
                MarkMoved(tx, ty);
                MarkMoved(bx, by);
                RockfordX = tx;
                RockfordY = ty;
            }
            return;
        }
        if (target == Tile.Firefly || target == Tile.Butterfly)
        {
            GameOver = true;
        }
    }

    private void TryReachRockford(int dx, int dy)
    {
        if (dx == 0 && dy == 0) return;
        int tx = RockfordX + dx, ty = RockfordY + dy;
        if (!InBounds(tx, ty)) return;

        Tile target = _grid[tx, ty].Tile;
        if (target == Tile.Diamond)
        {
            DiamondsCollected++;
            ClearCell(tx, ty);
            MarkMoved(tx, ty);
            return;
        }
        if (target == Tile.Rock && dy == 0)
        {
            int bx = tx + dx, by = ty;
            if (InBounds(bx, by) && CanRollInto(bx, by))
            {
                SetCell(bx, by, Tile.Rock, 0, false);
                ClearCell(tx, ty);
                MarkMoved(tx, ty);
                MarkMoved(bx, by);
            }
            return;
        }
        if (target == Tile.Dirt)
        {
            ClearCell(tx, ty);
        }
    }

    private void MoveRockford(int tx, int ty)
    {
        int ox = RockfordX, oy = RockfordY;
        ClearCell(ox, oy);
        SetCell(tx, ty, Tile.Rockford, 0, false);
        MarkMoved(ox, oy);
        MarkMoved(tx, ty);
        RockfordX = tx;
        RockfordY = ty;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private bool InBounds(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;

    private bool CanRollOver(int x, int y)
    {
        if (!InBounds(x, y)) return true;
        Tile t = _grid[x, y].Tile;
        return t == Tile.Wall || t == Tile.Rock || t == Tile.Diamond;
    }

    private bool CanRollInto(int x, int y) =>
        InBounds(x, y) && _grid[x, y].Tile == Tile.Space;

    private bool IsBlocking(int x, int y)
    {
        if (!InBounds(x, y)) return true;
        Tile t = _grid[x, y].Tile;
        return t != Tile.Space;
    }

    private void MarkMoved(int x, int y)
    {
        if (InBounds(x, y)) _moved[x, y] = true;
    }

    private void SetCell(int x, int y, Tile tile, byte facing, bool falling)
    {
        if (!InBounds(x, y)) return;
        _grid[x, y] = new Cell { Tile = tile, Facing = facing, WasFalling = falling };
    }

    private void ClearCell(int x, int y)
    {
        if (InBounds(x, y)) _grid[x, y] = default;
    }
}
