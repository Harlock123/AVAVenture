using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAVenture.Engine;
using AVAVenture.Views;
using Controls = AVAVenture.Engine.Controls;

namespace AVAVenture.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set VENTURE_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avaventure-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("VENTURE_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAVenture-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150, bool holdFire = false)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            var move = g.Pilot.Decide(g);
            g.Tick(holdFire ? move with { Fire = false } : move);
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }


    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("ACE", 64250, 6);
        g.HighScores.Insert("AVA", 41800, 4);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void The_halls()
    {
        var (w, c, g) = Open(2);
        g.Go(1);
        g.SetLives(20);
        g.Map[0].Done = true;
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Hall && g.PhaseTime > 2 && g.Hero.Y < 450, 60));
        Shot(w, c, "halls");
    }

    /// <summary>Into a room, and the computer at work in it until something worth showing.</summary>
    private static void InRoom(int level, int room, string name, Func<GameEngine, bool> busy, int seed = 3)
    {
        var (w, c, g) = Open(seed);
        g.Go(level);
        g.SetLives(20);
        g.Enter(room);
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Room && g.RoomTime > 1 && g.Hero.Alive && busy(g), 30));
        Shot(w, c, name);
    }

    [AvaloniaFact]
    public void The_Shifting_Hall() => InRoom(1, 0, "shifting", g => g.RoomTime > 2.5);

    [AvaloniaFact]
    public void The_Goblin_Den() => InRoom(1, 1, "goblins", g => g.Arrows.Count > 0 && g.Corpses.Count > 0);

    [AvaloniaFact]
    public void The_Bone_Crypt() => InRoom(1, 2, "crypt", g => g.Holding && g.Monsters.Count >= 2);

    [AvaloniaFact]
    public void The_Spider_Lair() => InRoom(2, 0, "spiders", g => g.Monsters.Count >= 3 && g.Arrows.Count > 0);

    [AvaloniaFact]
    public void The_Troll_Bridge() => InRoom(2, 1, "bridge", g => g.Hero.Y > 300 && g.Hero.Y < 560);

    [AvaloniaFact]
    public void The_Dragons_Hoard() => InRoom(2, 3, "dragon", g => g.Flames.Count > 0 && g.Monsters.Any(m => m.Kind == MonsterKind.Dragon));

    [AvaloniaFact]
    public void A_Lurker_comes_in()
    {
        var (w, c, g) = Open(4);
        g.Go(1);
        g.SetLives(20);
        g.Enter(3);
        // Stand about (with the monsters kept off) until the Lurker comes.
        g.Hero.Safe = 60;
        g.PutHero(780, 640);
        for (int t = 0; t < 60 * 40 && g.RoomLurker == null; t++) { g.Tick(default); c.Advance(1 / 60.0); }
        Assert.NotNull(g.RoomLurker);
        g.Idle(70);
        // Steady (not blinking), and clear of the snakes, for the picture.
        g.Hero.Safe = 0;
        g.PutHero(820, 300);
        g.Run(1, new Controls(Dx: -1));
        c.Advance(0.1);
        Shot(w, c, "lurker");
    }
}
