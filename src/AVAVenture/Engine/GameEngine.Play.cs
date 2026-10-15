namespace AVAVenture.Engine;

public sealed partial class GameEngine
{
    public const double HallSpeed = 170, RoomSpeed = 150, WebSlow = 0.4, ArrowSpeed = 560;
    public const double StartSeconds = 2.0, DyingSeconds = 2.2, SafeSeconds = 1.5, ClearSeconds = 3.5;
    public const int MonsterPoints = 100, FirstExtraLife = 10000, ExtraLifeEvery = 20000;

    /// <summary>The level: the two maps alternate, quicker each time round.</summary>
    public int Wave { get; private set; } = 1;
    public int Score { get; private set; }
    public int Lives { get; private set; }
    public Hero Hero { get; } = new();
    public List<MapRoom> Map { get; private set; } = Dungeon.Map(1);
    public List<Lurker> Lurkers { get; } = [];
    /// <summary>The room the adventurer is in, if any, and the door it came in by.</summary>
    public MapRoom? Room { get; private set; }
    public Side Entered { get; private set; }
    public List<Monster> Monsters { get; } = [];
    public List<Corpse> Corpses { get; } = [];
    public List<Arrow> Arrows { get; } = [];
    public List<Flame> Flames { get; } = [];
    public List<Debris> Wreckage { get; } = [];
    public ShipPilot Pilot { get; } = new();
    public string Cause { get; private set; } = "";
    public double RoomTime { get; private set; }
    /// <summary>The adventurer has picked up this room's treasure.</summary>
    public bool Holding { get; private set; }
    public int Bonus { get; private set; }
    /// <summary>For tests: no monster moves on its own, no Lurker comes into a room, and the dragon doesn't breathe.</summary>
    internal bool Calm { get; set; }

    private int _nextExtra;
    private bool _firePrev;

    public double Pace => Math.Min(1.8, 1 + 0.12 * (Wave - 1));
    public double LurkerAfter => Math.Max(12, 28 - 3 * (Wave - 1));
    public int Left => Map.Count(r => !r.Done);

    /// <summary>Everything solid in the room as it is now: its outer wall, its own walls, and its sliding walls where they are.</summary>
    public IEnumerable<Box> Solid
    {
        get
        {
            if (Room is not { } r) yield break;
            foreach (var b in Dungeon.Outer(r.Doors)) yield return b;
            foreach (var b in r.Plan.Walls) yield return b;
            foreach (var s in r.Plan.Sliders) yield return s.At(RoomTime);
        }
    }

    private void NewGame()
    {
        _rng = new Random(_seed);
        Score = 0;
        Lives = 3;
        Wave = 1;
        _nextExtra = FirstExtraLife;
        Wreckage.Clear();
        SetPhase(Phase.Start);
    }

    private void BeginWave(int wave)
    {
        Wave = wave;
        Map = Dungeon.Map(wave);
        ToHall(start: true);
        SetPhase(Phase.Start);
        Say($"LEVEL {wave}", "FOUR ROOMS, FOUR TREASURES", StartSeconds + 0.4);
        Play(Sfx.LevelStart);
    }

    /// <summary>Back into the halls: at the start, or just outside the door of the room being left.</summary>
    private void ToHall(bool start, MapRoom? from = null, Side side = Side.Top)
    {
        Room = null;
        RoomLurker = null;
        _lurkerIn = false;
        Monsters.Clear();
        Corpses.Clear();
        Arrows.Clear();
        Flames.Clear();
        Holding = false;
        var h = Hero;
        if (start || from == null)
        {
            (h.X, h.Y) = Dungeon.Start(Wave);
            PlaceLurkers();
        }
        else
        {
            var (dx, dy) = from.DoorAt(side);
            var (nx, ny) = Out(side);
            h.X = dx + nx * (Hero.Radius + 6);
            h.Y = dy + ny * (Hero.Radius + 6);
        }
        h.Alive = true;
        SetPhase(Phase.Hall);
    }

    private static (int X, int Y) Out(Side s) => s switch { Side.Top => (0, -1), Side.Bottom => (0, 1), Side.Left => (-1, 0), _ => (1, 0) };

    /// <summary>The Lurkers start in the corners of the halls, away from the adventurer.</summary>
    private void PlaceLurkers()
    {
        Lurkers.Clear();
        (double, double)[] corners = [(60, 120), (964, 120), (60, 720), (964, 720), (512, 120), (60, 420)];
        int n = Math.Min(corners.Length, 3 + (Wave - 1) / 2);
        foreach (var (x, y) in corners.OrderByDescending(c => Math.Abs(c.Item1 - Hero.X) + Math.Abs(c.Item2 - Hero.Y)).Take(n))
            Lurkers.Add(new Lurker { X = x, Y = y, T = _rng.NextDouble() * 6 });
    }

    /// <summary>Into a room through one of its doors: its monsters waiting, the clock started.</summary>
    private void EnterRoom(MapRoom room, Side side)
    {
        Room = room;
        Entered = side;
        RoomLurker = null;
        _lurkerIn = false;
        RoomTime = 0;
        Holding = false;
        Monsters.Clear();
        Corpses.Clear();
        Arrows.Clear();
        Flames.Clear();
        var (x, y) = Dungeon.DoorInside(side);
        var (nx, ny) = Out(side);
        Hero.X = x - nx * (Field.Wall + Hero.Radius + 10);
        Hero.Y = y - ny * (Field.Wall + Hero.Radius + 10);
        Hero.Safe = 0.6;
        // Its monsters, somewhere well away from the door.
        var plan = room.Plan;
        for (int i = 0; i < plan.Count; i++)
        {
            for (int tries = 0; tries < 60; tries++)
            {
                double mx = Field.Left + 80 + _rng.NextDouble() * (Field.Right - Field.Left - 160);
                double my = Field.Top + 80 + _rng.NextDouble() * (Field.Bottom - Field.Top - 160);
                if (Math.Abs(mx - Hero.X) + Math.Abs(my - Hero.Y) < 380 || Blocked(mx, my, 16, plan.Monster == MonsterKind.Troll)) continue;
                Monsters.Add(new Monster { Kind = plan.Monster, X = mx, Y = my, T = _rng.NextDouble() * 6 });
                break;
            }
        }
        if (plan.Kind == RoomKind.Dragon)
            Monsters.Add(new Monster { Kind = MonsterKind.Dragon, X = plan.TreasureX - 120, Y = plan.TreasureY - 90, Hp = 5 + Wave / 2, FireIn = 2 });
        // Lurkers in the halls wait where they are.
        SetPhase(Phase.Room);
        Say(plan.Name, null, 1.8);
        Play(Sfx.EnterRoom);
    }

    // ───────────────────────────── a step of play ─────────────────────────────

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        switch (Phase)
        {
            case Phase.Start:
                if (PhaseTime > StartSeconds)
                {
                    SetPhase(Phase.Hall);
                    if (State == GameState.Playing) Music(true);
                }
                break;
            case Phase.Hall:
                WalkHall(c, dt);
                if (Phase == Phase.Hall) MoveLurkers(dt);
                break;
            case Phase.Room:
                RoomTime += dt;
                WalkRoom(c, dt);
                if (Phase != Phase.Room) break;
                MoveArrows(dt);
                MoveMonsters(dt);
                MoveFlames(dt);
                if (Phase == Phase.Room) RoomHarm();
                break;
            case Phase.Dying:
                if (Room != null) { MoveMonsters(dt); MoveFlames(dt); }
                else MoveLurkers(dt);
                if (PhaseTime > DyingSeconds)
                {
                    if (Lives <= 0) { GameOver(); return; }
                    ToHall(start: true);
                    Hero.Safe = SafeSeconds;
                    if (State == GameState.Playing) Music(true);
                }
                break;
            case Phase.Clear:
                if (PhaseTime > ClearSeconds) BeginWave(Wave + 1);
                break;
        }
        foreach (var d in Wreckage)
        {
            d.Age += dt;
            d.X += d.Vx * dt;
            d.Y += d.Vy * dt;
        }
        Wreckage.RemoveAll(d => d.Age > d.Life);
    }

    // ───────────────────────────── the halls ─────────────────────────────

    /// <summary>Whether a circle in the halls would be inside a room's walls or out of the dungeon.</summary>
    public bool HallBlocked(double x, double y, double r)
    {
        if (x - r < Field.Left || x + r > Field.Right || y - r < Field.Top || y + r > Field.Bottom) return true;
        return Map.Any(m => m.Area.Touches(x, y, r));
    }

    private void WalkHall(Controls c, double dt)
    {
        var h = Hero;
        h.Safe = Math.Max(0, h.Safe - dt);
        Move(c, HallSpeed * dt, (x, y) => HallBlocked(x, y, Hero.Radius));
        // Through a doorway of a room not yet emptied.
        foreach (var room in Map.Where(m => !m.Done))
            foreach (var side in room.Doors)
            {
                var (dx, dy) = room.DoorAt(side);
                if (Math.Abs(h.X - dx) + Math.Abs(h.Y - dy) < Hero.Radius + 8) { EnterRoom(room, side); return; }
            }
        if (h.Safe > 0) return;
        foreach (var l in Lurkers)
            if (Math.Abs(l.X - h.X) < Lurker.Radius + Hero.Radius - 4 && Math.Abs(l.Y - h.Y) < Lurker.Radius + Hero.Radius - 4)
            {
                Die("CAUGHT BY A LURKER");
                return;
            }
    }

    /// <summary>Moves the adventurer, sliding along whatever it runs into, and turns it to face the way it went.</summary>
    private void Move(Controls c, double step, Func<double, double, bool> blocked)
    {
        var h = Hero;
        if (c.Dx == 0 && c.Dy == 0) return;
        h.FaceX = Math.Sign(c.Dx);
        h.FaceY = Math.Sign(c.Dy);
        double k = c.Dx != 0 && c.Dy != 0 ? Math.Sqrt(0.5) : 1;
        double nx = h.X + Math.Sign(c.Dx) * step * k, ny = h.Y + Math.Sign(c.Dy) * step * k;
        if (!blocked(nx, h.Y)) h.X = nx;
        if (!blocked(h.X, ny)) h.Y = ny;
    }

    /// <summary>The Lurkers drift after the adventurer, through anything, weaving a little.</summary>
    private void MoveLurkers(double dt)
    {
        double speed = (55 + 8 * (Wave - 1)) * (Calm ? 0 : 1);
        foreach (var l in Lurkers)
        {
            l.T += dt;
            double dx = Hero.X - l.X, dy = Hero.Y - l.Y, d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
            double wob = Math.Sin(l.T * 2.3) * 0.6;
            double ux = dx / d * Math.Cos(wob) - dy / d * Math.Sin(wob), uy = dx / d * Math.Sin(wob) + dy / d * Math.Cos(wob);
            l.X = Math.Clamp(l.X + ux * speed * dt, Field.Left + 10, Field.Right - 10);
            l.Y = Math.Clamp(l.Y + uy * speed * dt, Field.Top + 10, Field.Bottom - 10);
        }
    }

    // ───────────────────────────── the rooms ─────────────────────────────

    /// <summary>Whether a circle in the room would be inside a wall (or, for a troll, over the chasm).</summary>
    public bool Blocked(double x, double y, double r, bool chasms = false)
    {
        if (Room is not { } room) return false;
        foreach (var b in Dungeon.Outer(room.Doors)) if (b.Touches(x, y, r)) return true;
        foreach (var b in room.Plan.Walls) if (b.Touches(x, y, r)) return true;
        foreach (var s in room.Plan.Sliders) if (s.At(RoomTime).Touches(x, y, r)) return true;
        if (chasms) foreach (var b in room.Plan.Chasms) if (b.Touches(x, y, r)) return true;
        return x < Field.Left - 30 || x > Field.Right + 30 || y < Field.Top - 30 || y > Field.Bottom + 30;
    }

    public bool InWeb(double x, double y) => Room?.Plan.Webs.Any(w => (w.X - x) * (w.X - x) + (w.Y - y) * (w.Y - y) < w.R * w.R) == true;

    private void WalkRoom(Controls c, double dt)
    {
        var h = Hero;
        var room = Room!;
        h.Safe = Math.Max(0, h.Safe - dt);
        double speed = RoomSpeed * (InWeb(h.X, h.Y) ? WebSlow : 1);
        Move(c, speed * dt, (x, y) => Blocked(x, y, Hero.Radius));
        bool fresh = c.Fire && !_firePrev;
        _firePrev = c.Fire;
        if (fresh && Arrows.Count == 0)
        {
            double k = h.FaceX != 0 && h.FaceY != 0 ? Math.Sqrt(0.5) : 1;
            Arrows.Add(new Arrow { X = h.X, Y = h.Y, Vx = h.FaceX * ArrowSpeed * k, Vy = h.FaceY * ArrowSpeed * k });
            Play(Sfx.Arrow);
        }
        // The treasure.
        if (!Holding && !room.Done && Math.Abs(h.X - room.Plan.TreasureX) < 22 && Math.Abs(h.Y - room.Plan.TreasureY) < 22)
        {
            Holding = true;
            int value = TreasureValue;
            AddScore(value);
            Effects.Add(new Effect { Kind = EffectKind.Word, X = room.Plan.TreasureX, Y = room.Plan.TreasureY - 30, Text = $"{room.Plan.Treasure} {value}", Life = 2, Colour = 0xFFFFE04A });
            Play(Sfx.Treasure);
        }
        // Out through a doorway.
        foreach (var side in room.Doors)
        {
            var (x, y) = Dungeon.DoorInside(side);
            bool through = side switch
            {
                Side.Top => h.Y < Field.Top + 4 && Math.Abs(h.X - x) < Field.Doorway / 2,
                Side.Bottom => h.Y > Field.Bottom - 4 && Math.Abs(h.X - x) < Field.Doorway / 2,
                Side.Left => h.X < Field.Left + 4 && Math.Abs(h.Y - y) < Field.Doorway / 2,
                _ => h.X > Field.Right - 4 && Math.Abs(h.Y - y) < Field.Doorway / 2,
            };
            if (!through) continue;
            LeaveRoom(side);
            return;
        }
        // Dawdle, and a Lurker comes in after you.
        if (!Calm && RoomTime > LurkerAfter)
        {
            if (!_lurkerIn)
            {
                _lurkerIn = true;
                var (x, y) = Dungeon.DoorInside(Entered);
                RoomLurker = new Lurker { X = x, Y = y };
                Say("A LURKER COMES!", null, 1.5);
                Play(Sfx.LurkerComes);
            }
        }
    }

    private bool _lurkerIn;
    /// <summary>The Lurker that has come into the room after the adventurer, if one has.</summary>
    public Lurker? RoomLurker { get; private set; }

    public int TreasureValue => 500 * Wave;
    public double FlameSpeed => Math.Min(260, 190 + 10 * Wave);

    private void LeaveRoom(Side side)
    {
        var room = Room!;
        bool took = Holding;
        if (took) room.Done = true;
        _lurkerIn = false;
        RoomLurker = null;
        Play(Sfx.LeaveRoom);
        if (took && Left == 0)
        {
            Bonus = 2000 * Wave;
            AddScore(Bonus);
            ToHall(start: false, room, side);
            SetPhase(Phase.Clear);
            Music(false);
            Say($"LEVEL {Wave} CLEARED", $"BONUS {Bonus}", ClearSeconds);
            Play(Sfx.LevelClear);
            return;
        }
        ToHall(start: false, room, side);
        Hero.Safe = 0.8;
    }

    private void MoveArrows(double dt)
    {
        foreach (var a in Arrows)
        {
            for (int sub = 0; sub < 3 && !a.Dead; sub++)
            {
                a.X += a.Vx * dt / 3;
                a.Y += a.Vy * dt / 3;
                if (Blocked(a.X, a.Y, 2)) { a.Dead = true; break; }
                foreach (var m in Monsters)
                {
                    if (m.Dead || Math.Abs(m.X - a.X) > m.Radius + 3 || Math.Abs(m.Y - a.Y) > m.Radius + 3) continue;
                    a.Dead = true;
                    Wound(m);
                    break;
                }
            }
        }
        Arrows.RemoveAll(a => a.Dead);
        Monsters.RemoveAll(m => m.Dead);
    }

    /// <summary>An arrow in a monster: the dragon takes several; anything else dies, leaving its corpse behind.</summary>
    private void Wound(Monster m)
    {
        if (--m.Hp > 0)
        {
            m.Hurt = 0.15;
            Play(Sfx.Hit);
            return;
        }
        m.Dead = true;
        int points = (m.Kind == MonsterKind.Dragon ? 10 : 1) * MonsterPoints * (Holding ? 2 : 1);
        AddScore(points);
        Corpses.Add(new Corpse { Kind = m.Kind, X = m.X, Y = m.Y });
        Effects.Add(new Effect { Kind = EffectKind.Score, X = m.X, Y = m.Y - 22, Text = points.ToString(), Colour = Holding ? 0xFFFFE04A : 0xFFFFFFFF });
        Shatter(m.X, m.Y, 10, Room!.Plan.Colour);
        Play(Sfx.Kill);
    }

    private void MoveMonsters(double dt)
    {
        var h = Hero;
        foreach (var c in Corpses) c.Life -= dt;
        Corpses.RemoveAll(c => c.Life <= 0);
        if (RoomLurker is { } l)
        {
            // Faster than the hall Lurkers, and through anything.
            double dx = h.X - l.X, dy = h.Y - l.Y, d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
            double speed = (110 + 6 * (Wave - 1)) * dt;
            l.X += dx / d * speed;
            l.Y += dy / d * speed;
            l.T += dt;
        }
        if (Calm) return;
        foreach (var m in Monsters)
        {
            m.T += dt;
            m.Hurt = Math.Max(0, m.Hurt - dt);
            double dx = h.X - m.X, dy = h.Y - m.Y, d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
            double ux = dx / d, uy = dy / d, speed;
            switch (m.Kind)
            {
                case MonsterKind.Goblin:
                    speed = 78;
                    break;
                case MonsterKind.Skeleton:
                    speed = d < 200 ? 85 : 55;
                    break;
                case MonsterKind.Snake:
                {
                    // Slithering: its heading swings from side to side as it comes.
                    double a = Math.Atan2(uy, ux) + Math.Sin(m.T * 4) * 0.9;
                    (ux, uy) = (Math.Cos(a), Math.Sin(a));
                    speed = 82;
                    break;
                }
                case MonsterKind.Spider:
                    // Still, then a sudden dash.
                    speed = (m.T % 1.3) < 0.5 ? 240 : 0;
                    break;
                case MonsterKind.Troll:
                    speed = 66;
                    break;
                case MonsterKind.Bat:
                    // Flitting about, now and then at the adventurer.
                    m.Turn -= dt;
                    if (m.Turn <= 0)
                    {
                        m.Turn = 0.25 + _rng.NextDouble() * 0.4;
                        double a = _rng.NextDouble() < 0.45 ? Math.Atan2(uy, ux) + (_rng.NextDouble() - 0.5) : _rng.NextDouble() * Math.PI * 2;
                        (m.Vx, m.Vy) = (Math.Cos(a), Math.Sin(a));
                    }
                    (ux, uy) = (m.Vx, m.Vy);
                    speed = 150;
                    break;
                case MonsterKind.Drake:
                    speed = 95;
                    break;
                default:
                    // The dragon lumbers after the adventurer, and breathes fire at it.
                    speed = 26;
                    m.FireIn -= dt;
                    if (m.FireIn <= 0 && d < 520 && Phase == Phase.Room)
                    {
                        m.FireIn = Math.Max(1.4, 2.4 / Pace);
                        // A single fireball at first; a spread of three from the third level.
                        int spread = Wave >= 3 ? 1 : 0;
                        for (int i = -spread; i <= spread; i++)
                        {
                            double a = Math.Atan2(uy, ux) + i * 0.2;
                            Flames.Add(new Flame { X = m.X + ux * 30, Y = m.Y + uy * 30, Vx = Math.Cos(a) * FlameSpeed, Vy = Math.Sin(a) * FlameSpeed });
                        }
                        Play(Sfx.Fire);
                    }
                    break;
            }
            double step = speed * Pace * dt;
            bool troll = m.Kind == MonsterKind.Troll;
            double nx = m.X + ux * step, ny = m.Y + uy * step;
            if (!Blocked(nx, m.Y, m.Radius, troll)) m.X = nx;
            else if (m.Kind == MonsterKind.Bat) m.Vx = -m.Vx;
            if (!Blocked(m.X, ny, m.Radius, troll)) m.Y = ny;
            else if (m.Kind == MonsterKind.Bat) m.Vy = -m.Vy;
        }
    }

    private void MoveFlames(double dt)
    {
        foreach (var f in Flames)
        {
            f.X += f.Vx * dt;
            f.Y += f.Vy * dt;
            f.Life -= dt;
            if (f.Life <= 0 || Blocked(f.X, f.Y, 3)) f.Dead = true;
        }
        Flames.RemoveAll(f => f.Dead);
    }

    /// <summary>Whatever in the room can kill the adventurer: monsters, their corpses, fire, the Lurker, sliding walls, the chasm.</summary>
    private void RoomHarm()
    {
        var h = Hero;
        var plan = Room!.Plan;
        // The chasm and the sliding walls are always deadly.
        if (plan.Chasms.Any(b => b.Contains(h.X, h.Y))) { Die("FELL INTO THE CHASM", Sfx.Fall); return; }
        if (plan.Sliders.Any(s => s.At(RoomTime).Touches(h.X, h.Y, Hero.Radius - 3))) { Die("CRUSHED BY THE WALLS", Sfx.Crush); return; }
        if (RoomLurker is { } l && Math.Abs(l.X - h.X) < Lurker.Radius + Hero.Radius - 4 && Math.Abs(l.Y - h.Y) < Lurker.Radius + Hero.Radius - 4) { Die("THE LURKER CAUGHT UP"); return; }
        if (h.Safe > 0) return;
        foreach (var m in Monsters)
            if (Math.Abs(m.X - h.X) < m.Radius + Hero.Radius - 3 && Math.Abs(m.Y - h.Y) < m.Radius + Hero.Radius - 3)
            {
                Die($"CAUGHT BY A {m.Kind.ToString().ToUpperInvariant()}");
                return;
            }
        foreach (var c in Corpses)
            if (Math.Abs(c.X - h.X) < c.Radius + Hero.Radius - 4 && Math.Abs(c.Y - h.Y) < c.Radius + Hero.Radius - 4)
            {
                Die("TOUCHED A CORPSE");
                return;
            }
        foreach (var f in Flames)
            if (Math.Abs(f.X - h.X) < Hero.Radius + 5 && Math.Abs(f.Y - h.Y) < Hero.Radius + 5)
            {
                Die("BURNED BY DRAGON FIRE");
                return;
            }
    }

    private void Shatter(double x, double y, int n, uint colour)
    {
        for (int i = 0; i < n; i++)
        {
            double a = _rng.NextDouble() * Math.PI * 2, v = 40 + _rng.NextDouble() * 150;
            Wreckage.Add(new Debris { X = x, Y = y, Vx = Math.Cos(a) * v, Vy = Math.Sin(a) * v, Life = 0.4 + _rng.NextDouble() * 0.5, Colour = i % 3 == 0 ? 0xFFFFFFFF : colour });
        }
    }

    private void Die(string cause, Sfx sound = Sfx.Death)
    {
        Cause = cause;
        Hero.Alive = false;
        Lives--;
        _lurkerIn = false;
        Shatter(Hero.X, Hero.Y, 24, 0xFFFFE070);
        Effects.Add(new Effect { Kind = EffectKind.Burst, X = Hero.X, Y = Hero.Y, Life = 0.9, Size = 1.4, Colour = 0xFFFFE070 });
        Arrows.Clear();
        SetPhase(Phase.Dying);
        Music(false);
        Play(sound);
    }

    private void AddScore(int points)
    {
        Score += points;
        while (Score >= _nextExtra)
        {
            _nextExtra += ExtraLifeEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Straight into the halls of a level, past its name.</summary>
    internal void Go(int wave = 1)
    {
        StartGame();
        if (wave != 1) BeginWave(wave);
        SetPhase(Phase.Hall);
        Message = SubMessage = null;
    }

    /// <summary>Into one of the level's rooms through one of its doors.</summary>
    internal void Enter(int room, int door = 0)
    {
        EnterRoom(Map[room], Map[room].Doors[door]);
        Message = null;
    }

    internal Monster PutMonster(MonsterKind kind, double x, double y)
    {
        var m = new Monster { Kind = kind, X = x, Y = y, Hp = kind == MonsterKind.Dragon ? 5 : 1 };
        Monsters.Add(m);
        return m;
    }

    internal void PutHero(double x, double y) { Hero.X = x; Hero.Y = y; }
    internal void SetLives(int n) => Lives = n;
    internal void SetScore(int n) => Score = n;
}
