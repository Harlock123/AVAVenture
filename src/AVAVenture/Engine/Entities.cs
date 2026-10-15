namespace AVAVenture.Engine;

/// <summary>The eight directions (Dx, Dy), and the fire button: an arrow, in a room.</summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Fire = false);

/// <summary>The screen, in pixels: the dungeon below the score bar, with a wall all round.</summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const double Left = 32, Right = 992, Top = 96, Bottom = 744;
    public const double CentreX = (Left + Right) / 2, CentreY = (Top + Bottom) / 2;
    /// <summary>The thickness of a room's outer wall, and the width of its doorways.</summary>
    public const double Wall = 20, Doorway = 84;
}

/// <summary>An upright rectangle: a wall, a chasm, a room on the map.</summary>
public readonly record struct Box(double X, double Y, double W, double H)
{
    public double Right => X + W;
    public double Bottom => Y + H;
    public double CentreX => X + W / 2;
    public double CentreY => Y + H / 2;

    public bool Contains(double px, double py) => px >= X && px <= Right && py >= Y && py <= Bottom;

    /// <summary>Whether a circle overlaps it.</summary>
    public bool Touches(double px, double py, double r)
    {
        double cx = Math.Clamp(px, X, Right), cy = Math.Clamp(py, Y, Bottom);
        return (px - cx) * (px - cx) + (py - cy) * (py - cy) < r * r;
    }

    /// <summary>Whether the straight line from one point to another passes through it.</summary>
    public bool Crosses(double x0, double y0, double x1, double y1)
    {
        for (double t = 0; t <= 1; t += 0.05)
            if (Contains(x0 + (x1 - x0) * t, y0 + (y1 - y0) * t)) return true;
        return false;
    }
}

public enum Side { Top, Bottom, Left, Right }

public enum RoomKind { Shifting, Goblins, Crypt, Serpents, Spiders, Bridge, Bats, Dragon }

public enum MonsterKind { Goblin, Skeleton, Snake, Spider, Troll, Bat, Drake, Dragon }

/// <summary>A wall that slides back and forth.</summary>
public readonly record struct Slider(Box Home, double Dx, double Dy, double Period, double Offset)
{
    public Box At(double t)
    {
        double s = Math.Sin(t * Math.PI * 2 / Period + Offset);
        return Home with { X = Home.X + Dx * s, Y = Home.Y + Dy * s };
    }
}

/// <summary>A room: its walls and hazards, its treasure, and what guards it.</summary>
public sealed class RoomPlan
{
    public required RoomKind Kind { get; init; }
    public required string Name { get; init; }
    public required string Treasure { get; init; }
    public double TreasureX { get; init; } = Field.CentreX;
    public double TreasureY { get; init; } = Field.CentreY;
    public List<Box> Walls { get; init; } = [];
    public List<Slider> Sliders { get; init; } = [];
    /// <summary>Floors that drop away: step on one and you fall.</summary>
    public List<Box> Chasms { get; init; } = [];
    /// <summary>Sticky webs that slow the adventurer down: centres and radii.</summary>
    public List<(double X, double Y, double R)> Webs { get; init; } = [];
    public required MonsterKind Monster { get; init; }
    public int Count { get; init; } = 5;
    public uint Colour { get; init; } = 0xFF8A6AFF;
}

/// <summary>A room on the map: where it is, and which sides it has doors on.</summary>
public sealed class MapRoom
{
    public required Box Area { get; init; }
    public required Side[] Doors { get; init; }
    public required RoomPlan Plan { get; init; }
    public bool Done;

    /// <summary>The middle of a doorway on the map, on the room's edge.</summary>
    public (double X, double Y) DoorAt(Side s) => s switch
    {
        Side.Top => (Area.CentreX, Area.Y),
        Side.Bottom => (Area.CentreX, Area.Bottom),
        Side.Left => (Area.X, Area.CentreY),
        _ => (Area.Right, Area.CentreY),
    };
}

/// <summary>The two dungeon levels, each four rooms off the halls, and what's in every room.</summary>
public static class Dungeon
{
    /// <summary>Where a room's doorway is, inside it: the middle of that side of its outer wall.</summary>
    public static (double X, double Y) DoorInside(Side s) => s switch
    {
        Side.Top => (Field.CentreX, Field.Top),
        Side.Bottom => (Field.CentreX, Field.Bottom),
        Side.Left => (Field.Left, Field.CentreY),
        _ => (Field.Right, Field.CentreY),
    };

    /// <summary>The outer wall of a room, leaving its doorways open.</summary>
    public static List<Box> Outer(IEnumerable<Side> doors)
    {
        var open = doors.ToHashSet();
        double w = Field.Wall, d = Field.Doorway / 2, cx = Field.CentreX, cy = Field.CentreY;
        var list = new List<Box>();
        void Horizontal(double y, bool gap)
        {
            if (!gap) list.Add(new Box(Field.Left, y, Field.Right - Field.Left, w));
            else
            {
                list.Add(new Box(Field.Left, y, cx - d - Field.Left, w));
                list.Add(new Box(cx + d, y, Field.Right - cx - d, w));
            }
        }
        void Vertical(double x, bool gap)
        {
            if (!gap) list.Add(new Box(x, Field.Top, w, Field.Bottom - Field.Top));
            else
            {
                list.Add(new Box(x, Field.Top, w, cy - d - Field.Top));
                list.Add(new Box(x, cy + d, w, Field.Bottom - cy - d));
            }
        }
        Horizontal(Field.Top, open.Contains(Side.Top));
        Horizontal(Field.Bottom - w, open.Contains(Side.Bottom));
        Vertical(Field.Left, open.Contains(Side.Left));
        Vertical(Field.Right - w, open.Contains(Side.Right));
        return list;
    }

    private static readonly RoomPlan Shifting = new()
    {
        Kind = RoomKind.Shifting, Name = "THE SHIFTING HALL", Treasure = "GOLDEN CROWN", Monster = MonsterKind.Bat, Count = 2, Colour = 0xFF6AA0FF,
        Sliders =
        [
            new(new Box(200, 230, 250, 24), 150, 0, 4, 0),
            new(new Box(574, 230, 250, 24), -150, 0, 4, 0),
            new(new Box(200, 610, 250, 24), 150, 0, 4, Math.PI),
            new(new Box(574, 610, 250, 24), -150, 0, 4, Math.PI),
            new(new Box(300, 330, 24, 180), 0, 80, 3, 0),
            new(new Box(700, 330, 24, 180), 0, -80, 3, 0),
        ],
    };

    private static readonly RoomPlan Goblins = new()
    {
        Kind = RoomKind.Goblins, Name = "THE GOBLIN DEN", Treasure = "JEWELLED CHALICE", Monster = MonsterKind.Goblin, Colour = 0xFF6AFF6A,
        Walls = [new(190, 220, 90, 90), new(744, 220, 90, 90), new(190, 540, 90, 90), new(744, 540, 90, 90), new(440, 300, 144, 34), new(440, 520, 144, 34)],
    };

    private static readonly RoomPlan Crypt = new()
    {
        Kind = RoomKind.Crypt, Name = "THE BONE CRYPT", Treasure = "SILVER RELIQUARY", Monster = MonsterKind.Skeleton, Colour = 0xFFE0E0C0,
        Walls = [new(140, 200, 180, 40), new(704, 200, 180, 40), new(140, 610, 180, 40), new(704, 610, 180, 40), new(300, 370, 40, 110), new(684, 370, 40, 110)],
    };

    private static readonly RoomPlan Serpents = new()
    {
        Kind = RoomKind.Serpents, Name = "THE SERPENT PIT", Treasure = "EMERALD IDOL", Monster = MonsterKind.Snake, Count = 4, Colour = 0xFFB0FF4A, TreasureY = 430,
        Walls =
        [
            new(362, 280, 120, 24), new(542, 280, 120, 24), new(362, 556, 120, 24), new(542, 556, 120, 24),
            new(362, 304, 24, 96), new(362, 460, 24, 96), new(638, 304, 24, 96), new(638, 460, 24, 96),
        ],
    };

    private static readonly RoomPlan Spiders = new()
    {
        Kind = RoomKind.Spiders, Name = "THE SPIDER LAIR", Treasure = "SAPPHIRE AMULET", Monster = MonsterKind.Spider, Colour = 0xFFC08AFF,
        Walls = [new(190, 390, 60, 60), new(774, 390, 60, 60)],
        Webs = [(250, 250, 75), (774, 250, 75), (250, 600, 75), (774, 600, 75), (512, 290, 60), (512, 570, 55)],
    };

    private static readonly RoomPlan Bridge = new()
    {
        Kind = RoomKind.Bridge, Name = "THE TROLL BRIDGE", Treasure = "GOLDEN HARP", Monster = MonsterKind.Troll, Count = 3, Colour = 0xFFFFA04A, TreasureX = 512, TreasureY = 420,
        Chasms =
        [
            new(52, 260, 128, 340), new(844, 260, 128, 340),
            new(240, 260, 544, 110), new(240, 470, 544, 130),
            new(240, 370, 192, 25), new(240, 445, 192, 25), new(592, 370, 192, 100),
        ],
    };

    private static readonly RoomPlan Bats = new()
    {
        Kind = RoomKind.Bats, Name = "THE BAT CAVERN", Treasure = "CRYSTAL ORB", Monster = MonsterKind.Bat, Count = 6, Colour = 0xFF6AE0FF, TreasureX = 760, TreasureY = 420,
        Walls = [new(200, 200, 50, 50), new(400, 180, 40, 60), new(650, 220, 60, 50), new(820, 180, 50, 50), new(260, 560, 60, 60), new(700, 580, 50, 60), new(380, 610, 40, 40), new(560, 380, 40, 80)],
    };

    private static readonly RoomPlan Dragon = new()
    {
        Kind = RoomKind.Dragon, Name = "THE DRAGON'S HOARD", Treasure = "DRAGON'S HOARD", Monster = MonsterKind.Drake, Count = 2, Colour = 0xFFFF6A4A, TreasureX = 830, TreasureY = 640,
        Walls = [new(260, 280, 80, 80), new(560, 240, 60, 60), new(220, 560, 70, 60)],
    };

    public static RoomPlan[] Plans => [Shifting, Goblins, Crypt, Serpents, Spiders, Bridge, Bats, Dragon];

    /// <summary>The map of a level: the first and second alternate, round after round.</summary>
    public static List<MapRoom> Map(int level) => (level - 1) % 2 == 0
        ?
        [
            new() { Area = new Box(100, 150, 280, 170), Doors = [Side.Bottom, Side.Right], Plan = Shifting },
            new() { Area = new Box(644, 150, 280, 170), Doors = [Side.Bottom, Side.Left], Plan = Goblins },
            new() { Area = new Box(100, 470, 280, 170), Doors = [Side.Top, Side.Right], Plan = Crypt },
            new() { Area = new Box(644, 470, 280, 170), Doors = [Side.Top, Side.Left], Plan = Serpents },
        ]
        :
        [
            new() { Area = new Box(372, 130, 280, 150), Doors = [Side.Bottom, Side.Left], Plan = Spiders },
            new() { Area = new Box(80, 330, 220, 200), Doors = [Side.Top, Side.Bottom], Plan = Bridge },
            new() { Area = new Box(724, 330, 220, 200), Doors = [Side.Left, Side.Bottom], Plan = Bats },
            new() { Area = new Box(372, 500, 280, 160), Doors = [Side.Top, Side.Right], Plan = Dragon },
        ];

    /// <summary>Where the adventurer starts on each level's map.</summary>
    public static (double X, double Y) Start(int level) => (level - 1) % 2 == 0 ? (512, 700) : (512, 400);
}

/// <summary>The adventurer: a little hero with a bow.</summary>
public sealed class Hero
{
    public const double Radius = 9;
    public double X, Y, Safe;
    /// <summary>The way it last moved, which is the way it shoots.</summary>
    public int FaceX = 0, FaceY = -1;
    public bool Alive = true;
}

public sealed class Arrow
{
    public double X, Y, Vx, Vy;
    public bool Dead;
}

/// <summary>A hall Lurker: it floats through walls after the adventurer, and nothing can hurt it.</summary>
public sealed class Lurker
{
    public const double Radius = 15;
    public double X, Y, T;
}

public sealed class Monster
{
    public MonsterKind Kind;
    public double X, Y, Vx, Vy, T, Turn, FireIn = 2, Hurt;
    public int Hp = 1;
    public bool Dead;
    public double Radius => Kind switch { MonsterKind.Dragon => 30, MonsterKind.Bat => 11, MonsterKind.Snake => 12, _ => 14 };
}

/// <summary>What's left of a slain monster: as deadly to touch as it was, until it fades.</summary>
public sealed class Corpse
{
    public MonsterKind Kind;
    public double X, Y, Life = 6;
    public double Radius => Kind == MonsterKind.Dragon ? 30 : 14;
}

/// <summary>A dragon's fireball.</summary>
public sealed class Flame
{
    public double X, Y, Vx, Vy, Life = 3;
    public bool Dead;
}

/// <summary>A fleck of something slain, flying out.</summary>
public sealed class Debris
{
    public double X, Y, Vx, Vy, Age, Life = 1;
    public uint Colour;
}

public enum EffectKind { Score, Burst, Word }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 0.8, Size = 1;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}
