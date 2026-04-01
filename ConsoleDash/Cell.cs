namespace ConsoleDash;

public struct Cell
{
    public Tile Tile;
    public byte Facing;           // 0=Up, 1=Left, 2=Down, 3=Right
    public bool WasFalling;
    public byte ExplosionStage;   // 0-2
    public Tile ExplosionSource;  // Firefly or Butterfly (for explosion color)
    public Tile ExplosionResult;  // Space or Diamond (what the cell becomes after)
}
