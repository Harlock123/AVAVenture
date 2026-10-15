using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAVenture.Engine;
using Effect = AVAVenture.Engine.Effect;

namespace AVAVenture.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAVenture/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Fire = 0xFFFF8A2A, Cyan = 0xFF6AE0FF;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);

    private const uint Gold = 0xFFFFD84A, Stone = 0xFF5A6080, Floor = 0xFF0C0A16, LurkerGreen = 0xFF6AE04A, HeroYellow = 0xFFFFE04A;

    private bool InGame => Engine.State is GameState.Playing or GameState.Attract or GameState.Paused or GameState.GameOver;

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF04040A), new Rect(0, 0, Field.Width, Field.Height));
        if (InGame)
        {
            using (dc.PushClip(new Rect(0, Field.Top - 10, Field.Width, Field.Height - Field.Top + 10)))
            {
                if (g.Room is { } room) DrawRoom(dc, room);
                else DrawHall(dc);
                foreach (var d in g.Wreckage)
                {
                    double a = Math.Clamp(1 - d.Age / d.Life, 0, 1);
                    dc.FillRectangle(Brush(Alpha(d.Colour, a)), new Rect(d.X - 2, d.Y - 2, 4, 4));
                }
            }
        }
        foreach (var e in g.Effects) DrawEffect(dc, e);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(Field.Width / 2, 360), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(Field.Width / 2, Field.Height - 10), 24, Yellow, center: true);
    }

    // ───────────────────────────── the halls ─────────────────────────────

    /// <summary>The map: the halls, the four rooms (the emptied ones filled in), the Lurkers and the adventurer.</summary>
    private void DrawHall(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(Floor), new Rect(Field.Left, Field.Top, Field.Right - Field.Left, Field.Bottom - Field.Top));
        // Faint flagstones.
        for (double x = Field.Left; x < Field.Right; x += 40) dc.DrawLine(Pen(0xFF14121E, 1), new Point(x, Field.Top), new Point(x, Field.Bottom));
        for (double y = Field.Top; y < Field.Bottom; y += 40) dc.DrawLine(Pen(0xFF14121E, 1), new Point(Field.Left, y), new Point(Field.Right, y));
        dc.DrawRectangle(null, Pen(Stone, 6), new Rect(Field.Left - 3, Field.Top - 3, Field.Right - Field.Left + 6, Field.Bottom - Field.Top + 6));
        foreach (var room in g.Map)
        {
            var a = room.Area;
            uint col = room.Plan.Colour;
            if (room.Done)
            {
                dc.FillRectangle(Brush(Alpha(col, 0.35)), new Rect(a.X, a.Y, a.W, a.H));
                for (double k = a.X - a.H; k < a.Right; k += 14)
                    dc.DrawLine(Pen(Alpha(col, 0.3), 2), new Point(Math.Max(a.X, k), Math.Max(a.Y, a.Y + (a.X - k))), new Point(Math.Min(a.Right, k + a.H), Math.Min(a.Bottom, a.Y + (a.Right - k))));
            }
            else dc.FillRectangle(Brush(0xFF100E1C), new Rect(a.X, a.Y, a.W, a.H));
            // The walls, with gaps for the doors.
            var pen = Pen(col, 5);
            void Edge(Point p, Point q, bool door)
            {
                if (!door || room.Done) { dc.DrawLine(pen, p, q); return; }
                var m = new Point((p.X + q.X) / 2, (p.Y + q.Y) / 2);
                double len = Math.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y)), gap = 18 / len;
                dc.DrawLine(pen, p, new Point(m.X - (q.X - p.X) * gap, m.Y - (q.Y - p.Y) * gap));
                dc.DrawLine(pen, new Point(m.X + (q.X - p.X) * gap, m.Y + (q.Y - p.Y) * gap), q);
            }
            Edge(new Point(a.X, a.Y), new Point(a.Right, a.Y), room.Doors.Contains(Side.Top));
            Edge(new Point(a.X, a.Bottom), new Point(a.Right, a.Bottom), room.Doors.Contains(Side.Bottom));
            Edge(new Point(a.X, a.Y), new Point(a.X, a.Bottom), room.Doors.Contains(Side.Left));
            Edge(new Point(a.Right, a.Y), new Point(a.Right, a.Bottom), room.Doors.Contains(Side.Right));
            Text(dc, room.Plan.Name.Replace("THE ", ""), new Point(a.CentreX, a.CentreY - 12), 22, Alpha(col, room.Done ? 0.5 : 0.9), center: true);
            if (!room.Done) DrawTreasure(dc, room.Plan.Kind, a.CentreX, a.CentreY + 22, 0.8);
            else Text(dc, "EMPTIED", new Point(a.CentreX, a.CentreY + 20), 20, Alpha(col, 0.6), center: true);
        }
        foreach (var l in g.Lurkers) DrawLurker(dc, l.X, l.Y, l.T, 1);
        var h = g.Hero;
        if (h.Alive && (h.Safe <= 0 || (int)(_clock * 10) % 2 == 0)) DrawHero(dc, h.X, h.Y, h.FaceX, h.FaceY, 0.8);
    }

    /// <summary>A Lurker: a green, many-legged thing with two pale eyes, its tentacles writhing.</summary>
    private void DrawLurker(DrawingContext dc, double x, double y, double t, double k)
    {
        for (int i = 0; i < 6; i++)
        {
            double a = Math.PI * 0.15 + i * Math.PI * 0.14, w = Math.Sin(t * 8 + i) * 4;
            dc.DrawLine(Pen(LurkerGreen, 3 * k), new Point(x + Math.Cos(a) * 8 * k, y + Math.Sin(a) * 8 * k), new Point(x + Math.Cos(a) * 18 * k + w, y + Math.Sin(a) * 18 * k + 4 * k));
        }
        dc.DrawEllipse(Brush(LurkerGreen), Pen(0xFF1A5A10, 1.2), new Point(x, y - 2 * k), 12 * k, 10 * k);
        dc.DrawEllipse(Brush(0xFFF0FFE0), null, new Point(x - 4 * k, y - 4 * k), 3 * k, 3.5 * k);
        dc.DrawEllipse(Brush(0xFFF0FFE0), null, new Point(x + 4 * k, y - 4 * k), 3 * k, 3.5 * k);
        dc.DrawEllipse(Brush(Red), null, new Point(x - 4 * k, y - 3.5 * k), 1.4 * k, 1.4 * k);
        dc.DrawEllipse(Brush(Red), null, new Point(x + 4 * k, y - 3.5 * k), 1.4 * k, 1.4 * k);
    }

    /// <summary>The adventurer: a round yellow face, and a bow held the way it's facing.</summary>
    private void DrawHero(DrawingContext dc, double x, double y, int fx, int fy, double k)
    {
        double a = Math.Atan2(fy, fx);
        dc.DrawEllipse(Brush(HeroYellow), Pen(0xFF8A6A10, 1.2), new Point(x, y), 9 * k, 9 * k);
        dc.DrawEllipse(Brush(0xFF201000), null, new Point(x + Math.Cos(a) * 3 * k - 2.5 * k, y + Math.Sin(a) * 3 * k - 2 * k), 1.6 * k, 1.6 * k);
        dc.DrawEllipse(Brush(0xFF201000), null, new Point(x + Math.Cos(a) * 3 * k + 2.5 * k, y + Math.Sin(a) * 3 * k - 2 * k), 1.6 * k, 1.6 * k);
        // The bow: an arc across the facing, its string, and an arrow ready.
        var c = new Point(x + Math.Cos(a) * 11 * k, y + Math.Sin(a) * 11 * k);
        var g = new StreamGeometry();
        using (var s = g.Open())
        {
            s.BeginFigure(new Point(c.X + Math.Cos(a + 1.4) * 9 * k, c.Y + Math.Sin(a + 1.4) * 9 * k), false);
            s.ArcTo(new Point(c.X + Math.Cos(a - 1.4) * 9 * k, c.Y + Math.Sin(a - 1.4) * 9 * k), new Size(9 * k, 9 * k), 0, false, SweepDirection.CounterClockwise);
        }
        dc.DrawGeometry(null, Pen(0xFFC08040, 2.2 * k), g);
        dc.DrawLine(Pen(0xFFE0E0E0, 1), new Point(c.X + Math.Cos(a + 1.4) * 9 * k, c.Y + Math.Sin(a + 1.4) * 9 * k), new Point(c.X + Math.Cos(a - 1.4) * 9 * k, c.Y + Math.Sin(a - 1.4) * 9 * k));
    }

    // ───────────────────────────── a room ─────────────────────────────

    private void DrawRoom(DrawingContext dc, MapRoom room)
    {
        var g = Engine;
        var plan = room.Plan;
        uint col = plan.Colour;
        dc.FillRectangle(Brush(0xFF0E0C18), new Rect(Field.Left, Field.Top, Field.Right - Field.Left, Field.Bottom - Field.Top));
        for (double x = Field.Left; x < Field.Right; x += 48) dc.DrawLine(Pen(Alpha(col, 0.05), 1), new Point(x, Field.Top), new Point(x, Field.Bottom));
        for (double y = Field.Top; y < Field.Bottom; y += 48) dc.DrawLine(Pen(Alpha(col, 0.05), 1), new Point(Field.Left, y), new Point(Field.Right, y));
        // The chasm: a dark drop, with a lit edge.
        foreach (var b in plan.Chasms)
        {
            dc.FillRectangle(Brush(0xFF000000), new Rect(b.X, b.Y, b.W, b.H));
            for (int i = 1; i < 4; i++) dc.DrawRectangle(null, Pen(Alpha(0xFF3A1A0A, 0.5 / i), 2), new Rect(b.X + i * 3, b.Y + i * 3, Math.Max(0, b.W - i * 6), Math.Max(0, b.H - i * 6)));
        }
        foreach (var b in plan.Chasms) dc.DrawRectangle(null, Pen(0xFF6A3A1A, 1.5), new Rect(b.X, b.Y, b.W, b.H));
        foreach (var (wx, wy, r) in plan.Webs) DrawWeb(dc, wx, wy, r);
        foreach (var b in Dungeon.Outer(room.Doors)) DrawBlock(dc, b, col);
        foreach (var b in plan.Walls) DrawBlock(dc, b, col);
        foreach (var s in plan.Sliders) DrawBlock(dc, s.At(g.RoomTime), 0xFFFF6A6A);
        if (!g.Holding && !room.Done)
        {
            dc.DrawEllipse(Brush(Alpha(Gold, 0.15 + 0.1 * Math.Sin(_clock * 4))), null, new Point(plan.TreasureX, plan.TreasureY), 26, 26);
            DrawTreasure(dc, plan.Kind, plan.TreasureX, plan.TreasureY, 1.3);
        }
        foreach (var c in g.Corpses) DrawMonster(dc, c.Kind, c.X, c.Y, 0, false, corpse: Math.Clamp(c.Life / 2, 0.3, 1));
        foreach (var m in g.Monsters) DrawMonster(dc, m.Kind, m.X, m.Y, m.T, m.Hurt > 0);
        foreach (var f in g.Flames)
        {
            dc.DrawLine(Pen(Alpha(Fire, 0.4), 8), new Point(f.X, f.Y), new Point(f.X - f.Vx * 0.06, f.Y - f.Vy * 0.06));
            dc.DrawEllipse(Brush(Fire), null, new Point(f.X, f.Y), 7, 7);
            dc.DrawEllipse(Brush(0xFFFFF0A0), null, new Point(f.X, f.Y), 3.5, 3.5);
        }
        foreach (var a in g.Arrows)
        {
            double v = Math.Max(1, Math.Sqrt(a.Vx * a.Vx + a.Vy * a.Vy)), ux = a.Vx / v, uy = a.Vy / v;
            dc.DrawLine(Pen(0xFFC08040, 2.5), new Point(a.X - ux * 18, a.Y - uy * 18), new Point(a.X, a.Y));
            Poly(dc, 0xFFE0E0F0, null, 0, new Point(a.X + ux * 6, a.Y + uy * 6), new Point(a.X - uy * 4, a.Y + ux * 4), new Point(a.X + uy * 4, a.Y - ux * 4));
        }
        if (g.RoomLurker is { } l) DrawLurker(dc, l.X, l.Y, l.T, 1.4);
        var h = g.Hero;
        if (h.Alive && (h.Safe <= 0 || (int)(_clock * 10) % 2 == 0)) DrawHero(dc, h.X, h.Y, h.FaceX, h.FaceY, 1.2);
        if (g.Holding) DrawTreasure(dc, plan.Kind, h.X + 16, h.Y - 18, 0.7);
    }

    /// <summary>A wall: stone blocks in the room's colour.</summary>
    private static void DrawBlock(DrawingContext dc, Box b, uint colour)
    {
        dc.FillRectangle(Brush(Alpha(colour, 0.32)), new Rect(b.X, b.Y, b.W, b.H));
        dc.DrawRectangle(null, Pen(Alpha(colour, 0.9), 2), new Rect(b.X, b.Y, b.W, b.H));
        for (double y = b.Y + 12; y < b.Bottom - 2; y += 12) dc.DrawLine(Pen(Alpha(colour, 0.3), 1), new Point(b.X, y), new Point(b.Right, y));
        int row = 0;
        for (double y = b.Y; y < b.Bottom - 2; y += 12, row++)
            for (double x = b.X + (row % 2 == 0 ? 14 : 4); x < b.Right - 2; x += 20)
                dc.DrawLine(Pen(Alpha(colour, 0.3), 1), new Point(x, y), new Point(x, Math.Min(b.Bottom, y + 12)));
    }

    private static void DrawWeb(DrawingContext dc, double x, double y, double r)
    {
        var pen = Pen(0x50E0E0FF, 1.2);
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4;
            dc.DrawLine(pen, new Point(x, y), new Point(x + Math.Cos(a) * r, y + Math.Sin(a) * r));
        }
        for (double rr = r / 4; rr <= r; rr += r / 4)
            for (int i = 0; i < 8; i++)
            {
                double a0 = i * Math.PI / 4, a1 = (i + 1) * Math.PI / 4;
                dc.DrawLine(pen, new Point(x + Math.Cos(a0) * rr, y + Math.Sin(a0) * rr), new Point(x + Math.Cos(a1) * rr, y + Math.Sin(a1) * rr));
            }
    }

    /// <summary>Each room's treasure.</summary>
    private void DrawTreasure(DrawingContext dc, RoomKind kind, double x, double y, double k)
    {
        Point P(double px, double py) => new(x + px * k, y + py * k);
        double glint = 0.5 + 0.5 * Math.Sin(_clock * 6 + x);
        switch (kind)
        {
            case RoomKind.Shifting:
                Poly(dc, Gold, 0xFF8A6A10, 1, P(-12, 6), P(-12, -6), P(-6, 0), P(0, -10), P(6, 0), P(12, -6), P(12, 6));
                dc.DrawEllipse(Brush(Red), null, P(0, 2), 2.5 * k, 2.5 * k);
                break;
            case RoomKind.Goblins:
                Poly(dc, Gold, 0xFF8A6A10, 1, P(-9, -10), P(9, -10), P(4, 2), P(2, 8), P(7, 11), P(-7, 11), P(-2, 8), P(-4, 2));
                dc.DrawEllipse(Brush(0xFF4AE04A), null, P(0, -5), 2.5 * k, 2.5 * k);
                break;
            case RoomKind.Crypt:
                Poly(dc, 0xFFE0E8F0, 0xFF6A7088, 1, P(-8, 10), P(-8, -4), P(0, -11), P(8, -4), P(8, 10));
                dc.DrawLine(Pen(Gold, 2 * k), P(0, -6), P(0, 6));
                dc.DrawLine(Pen(Gold, 2 * k), P(-4, -2), P(4, -2));
                break;
            case RoomKind.Serpents:
                Poly(dc, 0xFF4AE06A, 0xFF1A6A2A, 1, P(0, -12), P(7, -2), P(5, 11), P(-5, 11), P(-7, -2));
                dc.DrawEllipse(Brush(Gold), null, P(0, -3), 2.5 * k, 2.5 * k);
                break;
            case RoomKind.Spiders:
                dc.DrawLine(Pen(Gold, 1.5 * k), P(-7, -10), P(0, 0));
                dc.DrawLine(Pen(Gold, 1.5 * k), P(7, -10), P(0, 0));
                Poly(dc, 0xFF4A8AFF, 0xFFFFFFFF, 1, P(0, -2), P(7, 5), P(0, 12), P(-7, 5));
                break;
            case RoomKind.Bridge:
                Poly(dc, Gold, 0xFF8A6A10, 1.5, P(-8, 10), P(-8, -6), P(-2, -11), P(8, -11), P(8, 10));
                for (int i = 0; i < 4; i++) dc.DrawLine(Pen(0xFFFFFFE0, 1), P(-5 + i * 3.5, -6), P(-5 + i * 3.5, 8));
                break;
            case RoomKind.Bats:
                dc.DrawEllipse(Brush(Alpha(0xFF6AE0FF, 0.8)), Pen(0xFFFFFFFF, 1.5), P(0, 0), 9 * k, 9 * k);
                dc.DrawEllipse(Brush(0xFFFFFFFF), null, P(-3, -3), 2.5 * k, 2.5 * k);
                break;
            default:
                for (int i = 0; i < 5; i++) dc.DrawEllipse(Brush(Gold), Pen(0xFF8A6A10, 1), P(-10 + i * 5, 6 - (i % 2) * 4), 5 * k, 3 * k);
                dc.DrawEllipse(Brush(Red), null, P(0, -6), 3.5 * k, 3.5 * k);
                break;
        }
        dc.FillRectangle(Brush(Alpha(0xFFFFFFFF, glint)), new Rect(x + 5 * k, y - 8 * k, 2, 2));
    }

    /// <summary>The monsters of the rooms; a corpse is drawn grey and fading.</summary>
    private void DrawMonster(DrawingContext dc, MonsterKind kind, double x, double y, double t, bool hurt, double corpse = 0)
    {
        Point P(double px, double py) => new(x + px, y + py);
        uint col = corpse > 0 ? Alpha(0xFF707070, corpse) : hurt ? 0xFFFFFFFF : kind switch
        {
            MonsterKind.Goblin => 0xFF6AE04A,
            MonsterKind.Skeleton => 0xFFE8E8D8,
            MonsterKind.Snake => 0xFFB0FF4A,
            MonsterKind.Spider => 0xFFB06AFF,
            MonsterKind.Troll => 0xFFFFA04A,
            MonsterKind.Bat => 0xFF6AE0FF,
            MonsterKind.Drake => 0xFFFF8A4A,
            _ => 0xFFFF4A3A,
        };
        double wob = corpse > 0 ? 0 : Math.Sin(t * 10);
        switch (kind)
        {
            case MonsterKind.Goblin:
                Poly(dc, col, 0xFF1A3A10, 1, P(-9, 12), P(-11, -2), P(-14, -10), P(-6, -6), P(0, -12), P(6, -6), P(14, -10), P(11, -2), P(9, 12));
                dc.DrawEllipse(Brush(corpse > 0 ? col : Yellow), null, P(-4, -3), 2, 2);
                dc.DrawEllipse(Brush(corpse > 0 ? col : Yellow), null, P(4, -3), 2, 2);
                break;
            case MonsterKind.Skeleton:
                dc.DrawEllipse(Brush(col), null, P(0, -8), 7, 6);
                dc.DrawEllipse(Brush(0xFF101010), null, P(-3, -9), 1.8, 2);
                dc.DrawEllipse(Brush(0xFF101010), null, P(3, -9), 1.8, 2);
                dc.DrawLine(Pen(col, 2), P(0, -2), P(0, 8));
                for (int i = 0; i < 3; i++) dc.DrawLine(Pen(col, 1.5), P(-6, 0 + i * 3), P(6, 0 + i * 3));
                dc.DrawLine(Pen(col, 2), P(0, 8), P(-5 + wob * 2, 14));
                dc.DrawLine(Pen(col, 2), P(0, 8), P(5 - wob * 2, 14));
                break;
            case MonsterKind.Snake:
                for (int i = 0; i < 5; i++)
                {
                    double sx = -12 + i * 6, sy = Math.Sin(t * 8 + i * 1.2) * 4;
                    dc.DrawEllipse(Brush(col), null, P(sx, sy), 5 - i * 0.4, 5 - i * 0.4);
                }
                dc.DrawEllipse(Brush(corpse > 0 ? col : Red), null, P(-13, Math.Sin(t * 8) * 4 - 1), 1.5, 1.5);
                break;
            case MonsterKind.Spider:
                for (int i = 0; i < 4; i++)
                    foreach (int s in new[] { -1, 1 })
                    {
                        double a = (i - 1.5) * 0.45 + wob * 0.15;
                        dc.DrawLine(Pen(col, 1.6), P(0, 0), P(s * (10 + Math.Cos(a) * 4), Math.Sin(a) * 12));
                    }
                dc.DrawEllipse(Brush(col), null, P(0, 0), 7, 8);
                dc.DrawEllipse(Brush(corpse > 0 ? col : Red), null, P(0, -3), 1.5, 1.5);
                break;
            case MonsterKind.Troll:
                Poly(dc, col, 0xFF5A2A0A, 1.2, P(-12, 12), P(-14, -4), P(-8, -12), P(8, -12), P(14, -4), P(12, 12));
                dc.DrawLine(Pen(0xFFE0E0E0, 2), P(-6, 2), P(-4, 6));
                dc.DrawLine(Pen(0xFFE0E0E0, 2), P(6, 2), P(4, 6));
                dc.DrawEllipse(Brush(0xFF101010), null, P(-4, -4), 2, 2);
                dc.DrawEllipse(Brush(0xFF101010), null, P(4, -4), 2, 2);
                break;
            case MonsterKind.Bat:
                Poly(dc, col, null, 0, P(0, -2), P(-6, -4 - wob * 5), P(-13, 0 - wob * 4), P(-8, 2), P(-4, 5), P(0, 3), P(4, 5), P(8, 2), P(13, 0 - wob * 4), P(6, -4 - wob * 5));
                dc.DrawEllipse(Brush(corpse > 0 ? col : Red), null, P(-2, -1), 1.2, 1.2);
                dc.DrawEllipse(Brush(corpse > 0 ? col : Red), null, P(2, -1), 1.2, 1.2);
                break;
            case MonsterKind.Drake:
                Poly(dc, col, 0xFF6A1A0A, 1, P(14, 0), P(4, -8), P(-8, -10 - wob * 3), P(-4, -2), P(-14, 0), P(-4, 2), P(-8, 10 + wob * 3), P(4, 8));
                dc.DrawEllipse(Brush(corpse > 0 ? col : Yellow), null, P(8, -2), 1.8, 1.8);
                break;
            default:
            {
                // The dragon: a great red head and body, wings spread.
                foreach (int s in new[] { -1, 1 })
                    Poly(dc, Alpha(col, 0.8), 0xFF6A0A0A, 1.5, P(s * 10, -6), P(s * 40, -24 - wob * 6), P(s * 34, 0), P(s * 44, 10 - wob * 4), P(s * 14, 10));
                dc.DrawEllipse(Brush(col), Pen(0xFF6A0A0A, 2), P(0, 4), 20, 16);
                Poly(dc, col, 0xFF6A0A0A, 2, P(-10, -6), P(0, -30), P(10, -6));
                dc.DrawEllipse(Brush(corpse > 0 ? col : Yellow), null, P(-4, -14), 2.5, 2.5);
                dc.DrawEllipse(Brush(corpse > 0 ? col : Yellow), null, P(4, -14), 2.5, 2.5);
                break;
            }
        }
    }

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        double t = e.Age / e.Life;
        var c = new Point(e.X, e.Y);
        switch (e.Kind)
        {
            case EffectKind.Burst:
            {
                double r = (6 + 30 * t) * e.Size;
                dc.DrawEllipse(null, Pen(Alpha(e.Colour, 1 - t), 2.5), c, r, r);
                break;
            }
            case EffectKind.Score:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 24), 24, e.Colour, center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 30), 30, e.Colour, center: true);
                break;
        }
    }

    // ───────────────────────────── the score bar, and the screens ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        Text(dc, "SCORE", new Point(16, 16), 22, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(16, 46), 36, Yellow);
        Text(dc, "HI", new Point(Field.Width - 16, 16), 22, Dim, right: true);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(Field.Width - 16, 46), 30, Dim, right: true);
        if (!InGame) return;
        Text(dc, $"LEVEL {g.Wave}", new Point(512, 18), 28, White, center: true);
        // The treasures: lit once taken.
        for (int i = 0; i < g.Map.Count; i++)
        {
            var room = g.Map[i];
            double x = 512 - 90 + i * 60;
            if (room.Done) DrawTreasure(dc, room.Plan.Kind, x, 58, 1);
            else dc.DrawEllipse(null, Pen(Alpha(room.Plan.Colour, 0.5), 1.5), new Point(x, 58), 12, 12);
        }
        int spare = Math.Max(0, g.Lives - (g.Hero.Alive ? 1 : 0));
        for (int i = 0; i < Math.Min(spare, 6); i++) DrawHero(dc, 220 + i * 28, 40, 1, 0, 0.8);
        if (g.Room is { } r2 && g.Phase is Phase.Room)
        {
            double left = Math.Max(0, g.LurkerAfter - g.RoomTime);
            if (g.RoomLurker == null) Text(dc, $"LURKER IN {left:0}", new Point(760, 46), 24, left < 6 ? Red : Dim, center: true);
            else if ((int)(_clock * 4) % 2 == 0) Text(dc, "RUN!", new Point(760, 46), 28, Red, center: true);
        }
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.Width / 2, cy = g.Room != null ? 650 : 160;
        var (w, h) = (Math.Max(msg.Length * 26, (g.SubMessage?.Length ?? 0) * 15) + 60, g.SubMessage != null ? 106.0 : 70.0);
        dc.FillRectangle(Brush(0xC0080A20), new Rect(cx - w / 2, cy - 46, w, h), 6);
        Text(dc, msg, new Point(cx, cy - 12), msg.Length > 14 ? 42 : 54, g.Phase == Phase.Dying ? Red : Yellow, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 34), 26, White, center: true);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        Text(dc, "AVA VENTURE", new Point(cx, 110), 112, Gold, center: true);
        Text(dc, "AFTER THE 1981 ARCADE CLASSIC", new Point(cx, 178), 28, Dim, center: true);
        DrawHero(dc, cx - 120, 250, 1, 0, 2.4);
        DrawLurker(dc, cx + 120, 250, _clock, 2.2);
        Text(dc, "RAID THE ROOMS FOR TREASURE - BEWARE THE LURKERS IN THE HALLS", new Point(cx, 316), 24, White, center: true);
        Text(dc, "DON'T DAWDLE IN A ROOM - AND DON'T TOUCH WHAT YOU'VE SLAIN", new Point(cx, 346), 24, White, center: true);
        MonsterKind[] kinds = [MonsterKind.Goblin, MonsterKind.Skeleton, MonsterKind.Snake, MonsterKind.Spider, MonsterKind.Troll, MonsterKind.Bat, MonsterKind.Drake, MonsterKind.Dragon];
        for (int i = 0; i < kinds.Length; i++)
        {
            double x = cx - 385 + i * 110;
            using (dc.PushTransform(Matrix.CreateScale(1.5, 1.5) * Matrix.CreateTranslation(x * -0.5, 430 * -0.5)))
                DrawMonster(dc, kinds[i], x, 430, _clock + i, false);
            Text(dc, kinds[i].ToString().ToUpperInvariant(), new Point(x, 478), 20, Dim, center: true);
        }
        var plans = Dungeon.Plans;
        for (int i = 0; i < plans.Length; i++)
        {
            double x = cx - 385 + i * 110;
            DrawTreasure(dc, plans[i].Kind, x, 524, 1.2);
        }
        Text(dc, $"TREASURE {500} A LEVEL - MONSTERS {GameEngine.MonsterPoints}, TWICE THAT WITH THE TREASURE IN HAND", new Point(cx, 560), 22, Yellow, center: true);
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER OR START", new Point(cx, 616), 46, Yellow, center: true);
        Text(dc, "ARROWS: WALK    SPACE: SHOOT AN ARROW (IN A ROOM)    P: PAUSE    M: SOUND", new Point(cx, 672), 23, Dim, center: true);
        Text(dc, $"EXTRA LIFE AT {GameEngine.FirstExtraLife} AND EVERY {GameEngine.ExtraLifeEvery}", new Point(cx, 704), 22, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "GREAT ADVENTURERS", new Point(cx, 140), 68, Gold, center: true);
        Text(dc, "RANK NAME    SCORE   LEVEL", new Point(cx, 204), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 248 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 710), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}
