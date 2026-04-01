namespace ConsoleDash;

public static class LevelLoader
{
    public static LevelData LoadFromFile(string path)
    {
        string[] lines = File.ReadAllLines(path);

        int separatorIndex = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Length == 0)
            {
                separatorIndex = i;
                break;
            }
        }

        var data = new LevelData();

        if (separatorIndex >= 0)
        {
            for (int i = separatorIndex + 1; i < lines.Length; i++)
                ParseParameter(data, lines[i]);
        }

        int mapEnd = separatorIndex >= 0 ? separatorIndex : lines.Length;
        int height = mapEnd;
        int width = 0;
        for (int i = 0; i < mapEnd; i++)
            width = Math.Max(width, lines[i].Length);

        width = Math.Min(width, 100);
        height = Math.Min(height, 50);

        data.Width = width;
        data.Height = height;
        data.Grid = new Cell[width, height];

        bool foundPlayer = false;
        for (int y = 0; y < height; y++)
        {
            string line = y < lines.Length ? lines[y] : "";
            for (int x = 0; x < width; x++)
            {
                char c = x < line.Length ? line[x] : ' ';
                data.Grid[x, y] = CharToCell(c);
                if (c == '@')
                {
                    data.RockfordX = x;
                    data.RockfordY = y;
                    foundPlayer = true;
                }
            }
        }

        if (!foundPlayer)
        {
            data.RockfordX = 1;
            data.RockfordY = 1;
            if (data.Width > 1 && data.Height > 1)
                data.Grid[1, 1] = new Cell { Tile = Tile.Rockford };
        }

        return data;
    }

    private static Cell CharToCell(char c) => c switch
    {
        '#' => new Cell { Tile = Tile.TitaniumWall },
        'W' => new Cell { Tile = Tile.Wall },
        'R' => new Cell { Tile = Tile.Rock },
        'D' => new Cell { Tile = Tile.Diamond },
        'F' => new Cell { Tile = Tile.Firefly },
        'B' => new Cell { Tile = Tile.Butterfly },
        'A' => new Cell { Tile = Tile.Amoeba },
        'M' => new Cell { Tile = Tile.MagicWall },
        'S' => new Cell { Tile = Tile.Slime },
        'E' => new Cell { Tile = Tile.Exit },
        '.' => new Cell { Tile = Tile.Dirt },
        '@' => new Cell { Tile = Tile.Rockford },
        ' ' => new Cell { Tile = Tile.Space },
        _   => new Cell { Tile = Tile.Dirt },
    };

    private static void ParseParameter(LevelData data, string line)
    {
        int colon = line.IndexOf(':');
        if (colon < 0) return;
        string key = line[..colon].Trim();
        string val = line[(colon + 1)..].Trim().Trim('"');
        switch (key)
        {
            case "NAME":                     data.Name = val; break;
            case "TIME":                     if (int.TryParse(val, out int t))   data.TimeLimit = t; break;
            case "DIAMONDS_REQUIRED":        if (int.TryParse(val, out int dr))  data.DiamondsRequired = dr; break;
            case "AMOEBA_MAX_SIZE":          if (int.TryParse(val, out int ams)) data.AmoebaMaxSize = ams; break;
            case "AMOEBA_GROWTH_FACTOR":     if (int.TryParse(val, out int agf)) data.AmoebaGrowthFactor = agf; break;
            case "MAGIC_WALL_DURATION":      if (int.TryParse(val, out int mwd)) data.MagicWallDuration = mwd; break;
            case "SLIME_PERMEABILITY_VALUE": if (int.TryParse(val, out int spv)) data.SlimePermeabilityValue = spv; break;
            case "GAME_TICK_INTERVAL":       if (int.TryParse(val, out int gti)) data.GameTickInterval = gti; break;
            case "ANIMATION_TICK_INTERVAL":  if (int.TryParse(val, out int ati)) data.AnimationTickInterval = ati; break;
        }
    }
}
