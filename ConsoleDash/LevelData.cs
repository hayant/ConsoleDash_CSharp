namespace ConsoleDash;

public class LevelData
{
    public string Name { get; set; } = "Level";
    public int Width { get; set; }
    public int Height { get; set; }
    public Cell[,] Grid { get; set; } = new Cell[0, 0];
    public int RockfordX { get; set; } = 1;
    public int RockfordY { get; set; } = 1;
    public int TimeLimit { get; set; } = 200;
    public int DiamondsRequired { get; set; } = 10;
    public int AmoebaMaxSize { get; set; } = 200;
    public int AmoebaGrowthFactor { get; set; } = 75;
    public int MagicWallDuration { get; set; } = 200;
    public int SlimePermeabilityValue { get; set; } = 0;
    public int GameTickInterval { get; set; } = 250;
    public int AnimationTickInterval { get; set; } = 200;
}
