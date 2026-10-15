using AVAVenture.Engine;

namespace AVAVenture.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avaventure-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A level in play, calm: the Lurkers and monsters stay put, and no Lurker comes into a room.</summary>
    private static GameEngine Game(int level = 1, int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
        g.Go(level);
        return g;
    }

    /// <summary>A real game, with plenty of lives.</summary>
    private static GameEngine Real(int level = 1, int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(level);
        g.SetLives(99);
        return g;
    }

    /// <summary>Into a room, emptied of its own monsters.</summary>
    private static GameEngine InRoom(int level, int room, int door = 0)
    {
        var g = Game(level);
        g.Enter(room, door);
        g.Monsters.Clear();
        return g;
    }

    private static readonly Controls FireButton = new(Fire: true);

    // ───────────────────────────── the halls ─────────────────────────────

    [Fact]
    public void The_adventurer_walks_the_halls_but_not_through_walls()
    {
        var g = Game();
        var (x, y) = (g.Hero.X, g.Hero.Y);
        g.Run(30, new Controls(Dx: 1));
        Assert.True(g.Hero.X > x + 60);
        g.Run(400, new Controls(Dy: 1));
        Assert.True(g.Hero.Y <= Field.Bottom - Hero.Radius);
        // Straight at a room's wall away from its doors: stopped by it.
        var room = g.Map[0];
        g.PutHero(room.Area.X + 30, room.Area.Y - 30);
        g.Run(60, new Controls(Dy: 1));
        Assert.True(g.Hero.Y <= room.Area.Y - Hero.Radius + 0.5);
        Assert.Equal(Phase.Hall, g.Phase);
    }

    [Fact]
    public void Lurkers_float_through_anything_after_the_adventurer()
    {
        var g = Game();
        g.Calm = false;
        var l = g.Lurkers[0];
        double d0 = Math.Abs(l.X - g.Hero.X) + Math.Abs(l.Y - g.Hero.Y);
        g.Idle(240);
        Assert.True(Math.Abs(l.X - g.Hero.X) + Math.Abs(l.Y - g.Hero.Y) < d0 - 100);
        l.X = g.Hero.X;
        l.Y = g.Hero.Y;
        g.Tick(default);
        Assert.Equal("CAUGHT BY A LURKER", g.Cause);
    }

    [Fact]
    public void Through_a_doorway_into_its_room()
    {
        var g = Game();
        var room = g.Map[1];
        var (x, y) = room.DoorAt(room.Doors[0]);
        g.PutHero(x, y + 20);
        g.Run(10, new Controls(Dy: -1));
        Assert.Equal(Phase.Room, g.Phase);
        Assert.Same(room, g.Room);
        Assert.Equal(room.Plan.Count, g.Monsters.Count);
    }

    // ───────────────────────────── the rooms ─────────────────────────────

    [Fact]
    public void An_arrow_flies_the_way_the_adventurer_faces_and_one_at_a_time()
    {
        var g = InRoom(1, 1);
        g.PutHero(400, 420);
        g.Run(2, new Controls(Dx: 1, Dy: -1));
        g.Tick(FireButton);
        var a = Assert.Single(g.Arrows);
        Assert.True(a.Vx > 0 && a.Vy < 0);
        g.Tick(default);
        g.Tick(FireButton);
        Assert.Single(g.Arrows);
    }

    [Fact]
    public void A_slain_monster_leaves_a_deadly_corpse_that_fades()
    {
        var g = InRoom(1, 1);
        g.PutHero(400, 440);
        g.Run(2, new Controls(Dx: 1));
        g.PutMonster(MonsterKind.Goblin, 620, 440);
        g.Tick(FireButton);
        g.Idle(30);
        var corpse = Assert.Single(g.Corpses);
        Assert.Equal(GameEngine.MonsterPoints, g.Score);
        g.PutHero(corpse.X - 18, corpse.Y);
        g.Hero.Safe = 0;
        g.Tick(default);
        Assert.Equal("TOUCHED A CORPSE", g.Cause);
        var h = InRoom(1, 1);
        h.Corpses.Add(new Corpse { Kind = MonsterKind.Goblin, X = 600, Y = 600 });
        h.Idle(400);
        Assert.Empty(h.Corpses);
    }

    [Fact]
    public void Monsters_are_worth_twice_as_much_with_the_treasure_in_hand()
    {
        var g = InRoom(1, 1);
        var plan = g.Room!.Plan;
        g.PutHero(plan.TreasureX, plan.TreasureY);
        g.Tick(default);
        Assert.True(g.Holding);
        Assert.Equal(g.TreasureValue, g.Score);
        g.PutHero(plan.TreasureX, 660);
        g.Run(2, new Controls(Dx: 1));
        g.PutMonster(MonsterKind.Goblin, plan.TreasureX + 200, 660);
        g.Tick(FireButton);
        g.Idle(30);
        Assert.Equal(g.TreasureValue + 2 * GameEngine.MonsterPoints, g.Score);
    }

    [Fact]
    public void Out_with_the_treasure_and_the_room_is_emptied_for_good()
    {
        var g = InRoom(1, 1);
        var room = g.Room!;
        g.PutHero(room.Plan.TreasureX, room.Plan.TreasureY);
        g.Tick(default);
        g.PutHero(Field.CentreX, Field.Bottom - 30);
        g.Run(30, new Controls(Dy: 1));
        Assert.Equal(Phase.Hall, g.Phase);
        Assert.True(room.Done);
        // Its doors are shut now.
        var (x, y) = room.DoorAt(room.Doors[0]);
        g.PutHero(x, y + 20);
        g.Run(20, new Controls(Dy: -1));
        Assert.Equal(Phase.Hall, g.Phase);
    }

    [Fact]
    public void Out_without_it_and_the_room_waits()
    {
        var g = InRoom(1, 1);
        var room = g.Room!;
        g.PutHero(Field.CentreX, Field.Bottom - 30);
        g.Run(30, new Controls(Dy: 1));
        Assert.Equal(Phase.Hall, g.Phase);
        Assert.False(room.Done);
    }

    [Fact]
    public void Dawdle_in_a_room_and_a_Lurker_comes_in_after_you()
    {
        var g = InRoom(1, 3);
        g.Calm = false;
        g.Monsters.Clear();
        g.Run((int)(g.LurkerAfter * 60) - 10, default);
        Assert.Null(g.RoomLurker);
        g.Run(20, default);
        Assert.NotNull(g.RoomLurker);
        for (int t = 0; t < 600 && g.Phase == Phase.Room; t++) g.Tick(default);
        Assert.Equal("THE LURKER CAUGHT UP", g.Cause);
    }

    [Fact]
    public void Sliding_walls_crush_and_webs_slow_you_down()
    {
        var g = InRoom(1, 0);
        var s = g.Room!.Plan.Sliders[0].At(g.RoomTime);
        g.PutHero(s.CentreX, s.CentreY);
        g.Tick(default);
        Assert.Equal("CRUSHED BY THE WALLS", g.Cause);
        var w = InRoom(2, 0);
        var (wx, wy, _) = w.Room!.Plan.Webs[0];
        w.PutHero(wx, wy);
        w.Run(30, new Controls(Dx: 1));
        double slow = w.Hero.X - wx;
        Assert.InRange(slow, GameEngine.RoomSpeed * 0.5 * GameEngine.WebSlow * 0.9, GameEngine.RoomSpeed * 0.5 * 0.75);
    }

    [Fact]
    public void Off_the_bridge_and_into_the_chasm()
    {
        var g = InRoom(2, 1);
        var c = g.Room!.Plan.Chasms[2];
        g.PutHero(c.CentreX, c.Y - 14);
        g.Run(30, new Controls(Dy: 1));
        Assert.Equal("FELL INTO THE CHASM", g.Cause);
    }

    [Fact]
    public void The_dragon_breathes_fire_and_takes_several_arrows()
    {
        var g = InRoom(2, 3);
        g.Calm = false;
        var d = g.PutMonster(MonsterKind.Dragon, 300, 440);
        g.PutHero(620, 440);
        g.Hero.Safe = 99;
        for (int t = 0; t < 240 && g.Flames.Count == 0; t++) g.Tick(default);
        Assert.NotEmpty(g.Flames);
        g.Calm = true;
        g.Flames.Clear();
        g.PutHero(620, d.Y);
        g.Run(2, new Controls(Dx: -1));
        int arrows = 0;
        while (!d.Dead && arrows < 10)
        {
            g.Tick(FireButton);
            g.Idle(30);
            arrows++;
        }
        Assert.True(d.Dead);
        Assert.Equal(5, arrows);
    }

    // ───────────────────────────── losing, and levels ─────────────────────────────

    [Fact]
    public void Caught_in_a_room_back_to_the_start_and_the_last_life_ends_the_game()
    {
        var g = InRoom(1, 1);
        g.SetLives(2);
        g.PutMonster(MonsterKind.Goblin, g.Hero.X, g.Hero.Y);
        g.Hero.Safe = 0;
        g.Tick(default);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal("CAUGHT BY A GOBLIN", g.Cause);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 5);
        Assert.Equal(Phase.Hall, g.Phase);
        Assert.Equal(Dungeon.Start(1), (g.Hero.X, g.Hero.Y));
        g.Calm = false;
        g.Hero.Safe = 0;
        g.Lurkers[0].X = g.Hero.X;
        g.Lurkers[0].Y = g.Hero.Y;
        g.Tick(default);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 5);
        Assert.Equal(GameState.GameOver, g.State);
    }

    [Fact]
    public void The_last_treasure_out_clears_the_level_for_a_bonus()
    {
        var g = Game();
        foreach (var r in g.Map.Skip(1)) r.Done = true;
        g.Enter(0);
        g.Monsters.Clear();
        var plan = g.Room!.Plan;
        g.PutHero(plan.TreasureX, plan.TreasureY);
        g.Tick(default);
        g.PutHero(Field.CentreX, Field.Bottom - 30);
        g.Run(30, new Controls(Dy: 1));
        Assert.Equal(Phase.Clear, g.Phase);
        Assert.Equal(2000, g.Bonus);
        g.Idle((int)(GameEngine.ClearSeconds * 60) + 5);
        Assert.Equal(2, g.Wave);
        Assert.Equal(RoomKind.Spiders, g.Map[0].Plan.Kind);
    }

    [Fact]
    public void Every_room_can_be_entered_and_left_by_each_of_its_doors()
    {
        for (int level = 1; level <= 2; level++)
        {
            var g = Game(level);
            for (int i = 0; i < 4; i++)
                for (int d = 0; d < 2; d++)
                {
                    g.Enter(i, d);
                    Assert.False(g.Blocked(g.Hero.X, g.Hero.Y, Hero.Radius), $"{g.Room!.Plan.Name} door {d}");
                    Assert.DoesNotContain(g.Room!.Plan.Chasms, b => b.Contains(g.Hero.X, g.Hero.Y));
                    var room = g.Map[i];
                    var (x, y) = room.DoorAt(room.Doors[d]);
                    Assert.False(g.Map.Any(m => m != room && m.Area.Touches(x, y, 30)), $"door {d} of {room.Plan.Name} opens onto another room");
                }
        }
    }

    [Fact]
    public void An_extra_life_at_10000()
    {
        var g = InRoom(1, 1);
        g.SetScore(9950);
        int lives = g.Lives;
        var plan = g.Room!.Plan;
        g.PutHero(plan.TreasureX, plan.TreasureY);
        g.Tick(default);
        Assert.Equal(lives + 1, g.Lives);
    }

    // ───────────────────────────── the machine ─────────────────────────────

    [Fact]
    public void The_title_runs_a_demonstration_then_the_high_scores()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Idle((int)(GameEngine.TitleIdleSeconds * 60) + 2);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle((int)(GameEngine.AttractSeconds * 60) + 2);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle((int)(GameEngine.HighScoreSeconds * 60) + 2);
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void The_computer_adventurer_clears_the_first_level()
    {
        var g = Real();
        g.SetLives(6);
        for (int i = 0; i < 60 * 180 && g.Wave < 2 && g.State == GameState.Playing; i++) g.Tick(g.Pilot.Decide(g));
        Assert.True(g.Wave >= 2, $"still on level {g.Wave}, {g.Left} rooms left, {g.Cause}");
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        string Play()
        {
            var g = Real(2, seed: 3);
            for (int i = 0; i < 60 * 40; i++) g.Tick(g.Pilot.Decide(g));
            return $"{g.Score} {g.Wave} {g.Lives} {g.Hero.X:0.00} {g.Hero.Y:0.00} {g.Left} {g.Phase}";
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void Pausing_stops_everything()
    {
        var g = Real(1);
        g.Run(30, new Controls(Dx: 1));
        double x = g.Hero.X;
        g.Pause();
        g.Idle(120);
        Assert.Equal(x, g.Hero.X);
        g.Resume();
        Assert.Equal(GameState.Playing, g.State);
    }
}
