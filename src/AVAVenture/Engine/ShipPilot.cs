namespace AVAVenture.Engine;

/// <summary>
/// The computer adventurer, for the demonstration (and the tests). It finds its way on a grid laid over the
/// screen, paying dearly to go near Lurkers and monsters and never through walls, chasms, sliding walls or
/// corpses. In the halls it heads for the nearest room not yet emptied; in a room, for the treasure, then out
/// by the nearest door (straight out if a Lurker comes in). Any monster lined up in one of the eight directions
/// with nothing in the way gets an arrow.
/// </summary>
public sealed class ShipPilot
{
    /// <summary>What it is up to, for the probe.</summary>
    public string Doing { get; private set; } = "";

    public const double Cell = 12;
    private const int Cols = (int)((Field.Right - Field.Left) / Cell), Rows = (int)((Field.Bottom - Field.Top) / Cell);

    private int _tick;
    private List<(double X, double Y)> _path = [];
    private bool _fireNext = true;

    public Controls Decide(GameEngine g)
    {
        _tick++;
        var h = g.Hero;
        if (!h.Alive || g.Phase is not (Phase.Hall or Phase.Room)) return default;
        if (g.Phase == Phase.Room && Dodge(g) is { } dodge) return dodge;
        if (g.Phase == Phase.Room && Shot(g) is { } shot) return shot;
        if (_tick % 6 == 0 || _path.Count == 0) Plan(g);
        // Follow the path a little way ahead.
        while (_path.Count > 1 && Math.Abs(_path[0].X - h.X) + Math.Abs(_path[0].Y - h.Y) < Cell * 0.9) _path.RemoveAt(0);
        if (_path.Count == 0) return default;
        var (tx, ty) = _path[Math.Min(1, _path.Count - 1)];
        int dx = Math.Abs(tx - h.X) > 2.5 ? Math.Sign(tx - h.X) : 0, dy = Math.Abs(ty - h.Y) > 2.5 ? Math.Sign(ty - h.Y) : 0;
        return new Controls(dx, dy, false);
    }

    /// <summary>An arrow at a monster lined up with one of the eight directions, if there is one and none is in the air.</summary>
    private Controls? Shot(GameEngine g)
    {
        var h = g.Hero;
        if (g.Arrows.Count > 0) return null;
        Monster? best = null;
        int bx = 0, by = 0;
        double bestD = 320;
        foreach (var m in g.Monsters)
        {
            double dx = m.X - h.X, dy = m.Y - h.Y, d = Math.Sqrt(dx * dx + dy * dy);
            if (d > bestD || d < 1) continue;
            double a = Math.Atan2(dy, dx), snap = Math.Round(a / (Math.PI / 4)) * (Math.PI / 4);
            // Lined up closely enough that the arrow will hit it.
            if (Math.Abs(Math.Sin(a - snap)) * d > m.Radius - 2) continue;
            if (g.Solid.Any(b => b.Crosses(h.X, h.Y, m.X, m.Y))) continue;
            best = m;
            bestD = d;
            bx = (int)Math.Round(Math.Cos(snap));
            by = (int)Math.Round(Math.Sin(snap));
        }
        if (best == null) return null;
        Doing = "shoot";
        _fireNext = !_fireNext;
        // Turning to face it means a step that way; never a step into something deadly.
        if (h.FaceX != bx || h.FaceY != by)
        {
            if (Unsafe(g, h.X + bx * 3, h.Y + by * 3)) return null;
            return new Controls(bx, by, false);
        }
        return new Controls(0, 0, _fireNext);
    }

    /// <summary>A fireball about to hit: a step across its path, whichever way is clear.</summary>
    private Controls? Dodge(GameEngine g)
    {
        var h = g.Hero;
        foreach (var f in g.Flames)
        {
            for (double t = 0; t <= 0.6; t += 0.05)
            {
                double fx = f.X + f.Vx * t, fy = f.Y + f.Vy * t;
                if (Math.Abs(fx - h.X) > Hero.Radius + 12 || Math.Abs(fy - h.Y) > Hero.Radius + 12) continue;
                double v = Math.Max(1, Math.Sqrt(f.Vx * f.Vx + f.Vy * f.Vy));
                // Across its path: of the two ways, the one that's clear and further from its line.
                foreach (int s in new[] { 1, -1 })
                {
                    double px = -f.Vy / v * s, py = f.Vx / v * s;
                    if ((h.X - fx) * px + (h.Y - fy) * py < -2) continue;
                    int dx = Math.Abs(px) > 0.38 ? Math.Sign(px) : 0, dy = Math.Abs(py) > 0.38 ? Math.Sign(py) : 0;
                    if (g.Blocked(h.X + dx * 14, h.Y + dy * 14, Hero.Radius) || Unsafe(g, h.X + dx * 14, h.Y + dy * 14)) continue;
                    Doing = "dodge";
                    return new Controls(dx, dy, false);
                }
                break;
            }
        }
        return null;
    }

    private static bool Unsafe(GameEngine g, double x, double y)
    {
        if (g.Room is not { } room) return false;
        if (room.Plan.Chasms.Any(b => b.Touches(x, y, Hero.Radius))) return true;
        return g.Corpses.Any(c => Math.Abs(c.X - x) < c.Radius + Hero.Radius + 4 && Math.Abs(c.Y - y) < c.Radius + Hero.Radius + 4);
    }

    /// <summary>Plans the way to wherever it's going, by the cheapest path across the grid.</summary>
    private void Plan(GameEngine g)
    {
        var h = g.Hero;
        var blocked = new bool[Cols, Rows];
        var cost = new double[Cols, Rows];
        var targets = new List<(int C, int R)>();
        double X(int c) => Field.Left + (c + 0.5) * Cell;
        double Y(int r) => Field.Top + (r + 0.5) * Cell;
        (int, int) CellOf(double x, double y) => (Math.Clamp((int)((x - Field.Left) / Cell), 0, Cols - 1), Math.Clamp((int)((y - Field.Top) / Cell), 0, Rows - 1));

        if (g.Phase == Phase.Hall)
        {
            Doing = "to room";
            for (int c = 0; c < Cols; c++)
                for (int r = 0; r < Rows; r++)
                {
                    double x = X(c), y = Y(r);
                    blocked[c, r] = g.HallBlocked(x, y, Hero.Radius + 1);
                    foreach (var l in g.Lurkers)
                    {
                        double d = Math.Sqrt((l.X - x) * (l.X - x) + (l.Y - y) * (l.Y - y));
                        if (d < 150) cost[c, r] += (150 - d) * (d < 50 ? 30 : 2);
                    }
                }
            foreach (var room in g.Map.Where(m => !m.Done))
                foreach (var side in room.Doors)
                {
                    var (dx, dy) = room.DoorAt(side);
                    int ox = side == Side.Left ? -1 : side == Side.Right ? 1 : 0, oy = side == Side.Top ? -1 : side == Side.Bottom ? 1 : 0;
                    targets.Add(CellOf(dx + ox * (Hero.Radius + 2), dy + oy * (Hero.Radius + 2)));
                }
        }
        else
        {
            var room = g.Room!;
            bool forDoor = g.Holding || room.Done || (g.RoomLurker != null && Math.Abs(h.X - room.Plan.TreasureX) + Math.Abs(h.Y - room.Plan.TreasureY) > 200);
            Doing = forDoor ? "to door" : "to treasure";
            for (int c = 0; c < Cols; c++)
                for (int r = 0; r < Rows; r++)
                {
                    double x = X(c), y = Y(r);
                    blocked[c, r] = g.Blocked(x, y, Hero.Radius + 1) || room.Plan.Chasms.Any(b => b.Touches(x, y, Hero.Radius + 3));
                    // Where the sliding walls will be over the next second.
                    for (double t = 0.2; t <= 1.2 && !blocked[c, r]; t += 0.25)
                        if (room.Plan.Sliders.Any(s => s.At(g.RoomTime + t).Touches(x, y, Hero.Radius + 4))) cost[c, r] += 60;
                    foreach (var k in g.Corpses)
                        if (Math.Abs(k.X - x) < k.Radius + Hero.Radius + 6 && Math.Abs(k.Y - y) < k.Radius + Hero.Radius + 6) blocked[c, r] = true;
                    foreach (var m in g.Monsters)
                    {
                        double d = Math.Sqrt((m.X - x) * (m.X - x) + (m.Y - y) * (m.Y - y));
                        if (d < 90) cost[c, r] += (90 - d) * (d < m.Radius + 16 ? 20 : 1.5);
                    }
                    foreach (var f in g.Flames)
                        if (Math.Abs(f.X + f.Vx * 0.2 - x) < 30 && Math.Abs(f.Y + f.Vy * 0.2 - y) < 30) cost[c, r] += 300;
                    if (g.RoomLurker is { } l)
                    {
                        double d = Math.Sqrt((l.X - x) * (l.X - x) + (l.Y - y) * (l.Y - y));
                        if (d < 160) cost[c, r] += (160 - d) * 8;
                    }
                    if (g.InWeb(x, y)) cost[c, r] += 2;
                }
            if (forDoor)
                foreach (var side in room.Doors)
                {
                    var (dx, dy) = Dungeon.DoorInside(side);
                    int ox = side == Side.Left ? -1 : side == Side.Right ? 1 : 0, oy = side == Side.Top ? -1 : side == Side.Bottom ? 1 : 0;
                    var (tc, tr) = CellOf(dx - ox * 4, dy - oy * 4);
                    targets.Add((tc, tr));
                    blocked[tc, tr] = false;
                }
            else targets.Add(CellOf(room.Plan.TreasureX, room.Plan.TreasureY));
        }

        // Dijkstra from where it stands to the cheapest of the targets.
        var (sc, sr) = CellOf(h.X, h.Y);
        var dist = new double[Cols, Rows];
        var from = new (int, int)[Cols, Rows];
        for (int c = 0; c < Cols; c++) for (int r = 0; r < Rows; r++) dist[c, r] = double.MaxValue;
        var queue = new PriorityQueue<(int C, int R), double>();
        dist[sc, sr] = 0;
        queue.Enqueue((sc, sr), 0);
        var goal = targets.ToHashSet();
        (int C, int R)? found = null;
        while (queue.TryDequeue(out var at, out double d0))
        {
            if (d0 > dist[at.C, at.R]) continue;
            if (goal.Contains(at)) { found = at; break; }
            for (int ddx = -1; ddx <= 1; ddx++)
                for (int ddy = -1; ddy <= 1; ddy++)
                {
                    if (ddx == 0 && ddy == 0) continue;
                    int nc = at.C + ddx, nr = at.R + ddy;
                    if (nc < 0 || nr < 0 || nc >= Cols || nr >= Rows) continue;
                    if (blocked[nc, nr] && !goal.Contains((nc, nr))) continue;
                    // No cutting corners past walls.
                    if (ddx != 0 && ddy != 0 && (blocked[at.C + ddx, at.R] || blocked[at.C, at.R + ddy])) continue;
                    double nd = d0 + (ddx != 0 && ddy != 0 ? 1.414 : 1) + cost[nc, nr];
                    if (nd >= dist[nc, nr]) continue;
                    dist[nc, nr] = nd;
                    from[nc, nr] = at;
                    queue.Enqueue((nc, nr), nd);
                }
        }
        _path = [];
        if (found is not { } end) return;
        var cell = end;
        while (cell != (sc, sr))
        {
            _path.Add((X(cell.C), Y(cell.R)));
            cell = from[cell.C, cell.R];
        }
        _path.Reverse();
        // On through the doorway at the end.
        if (g.Phase == Phase.Room && Doing == "to door")
        {
            var (lx, ly) = _path.Count > 0 ? _path[^1] : (h.X, h.Y);
            var side = g.Room!.Doors.MinBy(s => { var (x, y) = Dungeon.DoorInside(s); return Math.Abs(x - lx) + Math.Abs(y - ly); });
            var (ex, ey) = Dungeon.DoorInside(side);
            _path.Add((ex + (side == Side.Left ? -12 : side == Side.Right ? 12 : 0), ey + (side == Side.Top ? -12 : side == Side.Bottom ? 12 : 0)));
        }
        if (g.Phase == Phase.Hall)
        {
            // And into the doorway.
            var (lx, ly) = _path.Count > 0 ? _path[^1] : (h.X, h.Y);
            var door = g.Map.Where(m => !m.Done).SelectMany(m => m.Doors.Select(m.DoorAt)).MinBy(p => Math.Abs(p.X - lx) + Math.Abs(p.Y - ly));
            _path.Add(door);
        }
    }
}
