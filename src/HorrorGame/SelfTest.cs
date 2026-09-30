using System.Numerics;

namespace HorrorGame;

internal static class SelfTest
{
    public static void Run()
    {
        int checks = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("FAIL: " + name);
            checks++; Console.WriteLine("PASS: " + name);
        }
        var w = new World { Phase = Phase.Playing };
        Check(w.Walkable(w.Player) && w.Walkable(w.Ghost), "Spawn locations are clear");
        Check(w.FindPath(World.Spawn, World.Key).Count > 0, "Forest to manor key is reachable");
        Check(w.FindPath(World.Spawn, World.Relic).Count == 0, "Cave cannot be bypassed before unlocking");
        w.Player = World.At(World.Exit); w.Interact();
        Check(w.Phase == Phase.Playing, "Exit requires relic");
        w.Player = World.At(new(9, 19)); w.Interact();
        Check(!w.CaveOpen, "Cave requires key");
        w.Player = World.At(World.Key); w.Interact();
        Check(w.HasKey && w.Grace > 0, "Key activates chase with grace time");
        Check(w.FindPath(World.Key, new(9, 19)).Count > 0, "Manor to cave approach is reachable");
        w.Player = World.At(new(9, 19)); w.Interact();
        Check(w.CaveOpen && !w.Solid(9, 18), "Key opens cave collision");
        Check(w.FindPath(World.ToCell(w.Player), World.Relic).Count > 0, "Cave relic is reachable");
        // 全行程を実際の移動・衝突処理でたどり、タイル上の接続だけでないことを確認。
        foreach (Cell target in new[] { World.Relic, World.Key, World.Exit })
        {
            foreach (Cell c in w.FindPath(World.ToCell(w.Player), target))
            {
                Vector3 goal = World.At(c);
                for (int i = 0; i < 100 && Vector3.Distance(w.Player, goal) > .02f; i++)
                {
                    Vector3 delta = goal - w.Player;
                    w.Player = w.Move(w.Player, Vector3.Normalize(delta) * Math.Min(delta.Length(), .1f));
                }
                if (Vector3.Distance(w.Player, goal) > .03f) throw new InvalidOperationException($"Blocked at {c}");
            }
            Check(w.Near(target, .05f), $"Physical route to {target}");
            w.Interact();
        }
        Check(w.HasRelic && w.Phase == Phase.Escaped, "Complete key / cave / relic / escape flow");
        w = new World { Phase = Phase.Playing };
        Vector3 wallApproach = World.At(new(24, 21));
        Vector3 stopped = w.Move(wallApproach, new(0, 0, -25));
        Check(w.Walkable(stopped) && stopped.Z > 52.5f, "Large movement cannot tunnel through manor wall");
        for (int i = 0; i < 100; i++) w.Update(.05f, new(0, 1), true);
        Check(w.Stamina < 10 && w.Stamina >= 0, "Sprinting consumes bounded stamina");
        w.Flashlight = false; float before = w.Battery;
        for (int i = 0; i < 100; i++) w.Update(.05f, Vector2.Zero, false);
        Check(w.Battery >= before && w.Stamina > 60, "Battery and stamina recover");
        w.Phase = Phase.Paused; float time = w.Time; w.Update(1, Vector2.One, true);
        Check(w.Time == time, "Pause freezes simulation");
        w = new World { Phase = Phase.Playing, HasKey = true, Grace = 0, Flashlight = false };
        w.Player = World.At(new(19, 36)); w.Ghost = World.At(new(23, 36));
        float distance = Vector3.Distance(w.Player, w.Ghost);
        for (int i = 0; i < 25; i++) w.Update(.05f, Vector2.Zero, false);
        Check(Vector3.Distance(w.Player, w.Ghost) < distance - 2, "Ghost follows navigable route");
        for (int i = 0; i < 200; i++) w.Update(.05f, Vector2.Zero, false);
        Check(w.Phase == Phase.Dead, "Ghost catches player");
        var lit = new World { Phase = Phase.Playing, HasKey = true, Grace = 0 };
        lit.Player = World.At(new(19, 36)); lit.Ghost = World.At(new(19, 33));
        var unlit = new World { Phase = Phase.Playing, HasKey = true, Grace = 0, Flashlight = false };
        unlit.Player = lit.Player; unlit.Ghost = lit.Ghost;
        for (int i = 0; i < 20; i++) { lit.Update(.05f, Vector2.Zero, false); unlit.Update(.05f, Vector2.Zero, false); }
        Check(Vector3.Distance(lit.Player, lit.Ghost) > Vector3.Distance(unlit.Player, unlit.Ghost) + 1, "Aimed flashlight slows ghost");
        w = new World { Phase = Phase.Playing, Player = World.At(World.Shotgun) };
        Check(w.Walkable(w.Player) && w.FindPath(World.Spawn, World.Shotgun).Count > 0,
            "Hidden shotgun at northeast edge is reachable");
        Check(!w.ShotgunAvailable && !w.Prompt.Contains("ショットガン") && !w.Fire(), "Shotgun hidden before relic and cannot fire unarmed");
        w.Interact();
        Check(!w.HasShotgun, "Cannot collect shotgun before relic");
        w.HasRelic = true; w.HasKey = true; w.CaveOpen = true;
        Check(w.ShotgunAvailable && w.Prompt.Contains("ショットガン"), "Relic reveals nearby shotgun pickup");
        Check(w.FindPath(World.Relic, World.Shotgun).Count > 0, "Relic to hidden shotgun route exists");
        w.Interact();
        Check(w.HasShotgun && !w.ShotgunAvailable, "Shotgun is collected once");
        w.Player = World.At(new(19, 36)); w.Ghost = w.Player - Vector3.UnitZ * 8; w.Grace = 100;
        Check(w.Fire() && !w.Fire() && !w.GhostDefeated, "Shot launches with cooldown, not instant hit");
        Vector3 origin = w.Shots[0].Position;
        w.Update(.05f, Vector2.Zero, false);
        Check(w.Shots.Count == 1 && w.Shots[0].Position.Z < origin.Z, "Projectile travels forward");
        for (int i = 0; i < 8; i++) w.Update(.05f, Vector2.Zero, false);
        Check(w.GhostDefeated && !w.GhostActive && w.Threat == 0 && w.Shots.Count == 0, "One projectile defeats ghost");
        Vector3 defeatedPosition = w.Ghost;
        w.Player = w.Ghost; w.Grace = 0;
        for (int i = 0; i < 100; i++) w.Update(.05f, Vector2.Zero, false);
        Check(w.Phase == Phase.Playing && w.Ghost == defeatedPosition && w.Threat == 0 && !w.GhostActive,
            "Defeated ghost never moves, attacks, or respawns");
        w.Player = World.At(World.Exit); w.Interact();
        Check(w.Phase == Phase.Escaped, "Exit still works after ghost defeat");
        w = new World { Phase = Phase.Playing, HasShotgun = true, HasKey = true, Grace = 100,
            Player = World.At(new(24, 21)), Ghost = World.At(new(24, 19)) };
        w.Fire();
        w.Update(.5f, Vector2.Zero, false);
        Check(!w.GhostDefeated && w.Shots.Count == 0, "Wall stops projectile even on a long frame");
        w.Player = World.At(new(19, 36)); w.Ghost = w.Player - Vector3.UnitZ * 8; w.Pitch = .8f;
        w.Update(.1f, Vector2.Zero, false); w.Fire();
        for (int i = 0; i < 45; i++) w.Update(.05f, Vector2.Zero, false);
        Check(!w.GhostDefeated && w.Shots.Count == 0, "Shot follows pitch, misses above ghost, and expires");
        w.Fire(); w.Phase = Phase.Paused;
        MagicShot pausedShot = w.Shots[0]; w.Update(1, Vector2.Zero, false);
        Check(!w.Fire() && w.Shots[0] == pausedShot, "Pause stops firing and projectile movement");
        w = new World();
        Check(!w.HasShotgun && !w.GhostDefeated && w.Shots.Count == 0, "New game resets weapon and defeat state");
        w = new World { Phase = Phase.Playing, HasKey = true, HasRelic = true, CaveOpen = true, HasShotgun = true, Grace = 100 };
        Check(w.Family.Select(p => p.Name).SequenceEqual(new[] { "Mother", "Son", "Daughter", "Father" }), "Family order is mother, son, daughter, father");
        Check(w.Family.All(p => w.Walkable(p.Position) && w.FindPath(World.Key, World.ToCell(p.Position)).Count > 0), "Family stands in accessible manor interior");
        void ShootFamily(int index)
        {
            FamilyMember person = w.Family[index];
            w.Player = person.Position + Vector3.UnitZ * 6;
            w.Yaw = 0; w.Pitch = MathF.Atan2(person.Height * .65f - 1.65f, 6);
            Check(w.Fire(), "Can fire at family position");
            w.Update(.6f, Vector2.Zero, false);
        }
        ShootFamily(0);
        Check(w.Family.All(p => p.Alive) && !w.FamilyFrightened && !w.Cursed, "Family cannot be hit before ghost defeat");
        w.GhostDefeated = true;
        Check(w.FamilyDialogue.Contains("Thank you"), "Family thanks player after ghost defeat");
        ShootFamily(0);
        Check(w.Family.Count(p => p.Alive) == 3 && w.FamilyFrightened && !w.Cursed && w.FamilyDialogue.Contains("spare us"), "One hit removes one person and changes surviving dialogue");
        w.Player = World.At(World.Exit); w.Interact();
        Check(w.Phase == Phase.Escaped && w.RevengeEnding, "Escape after one family casualty triggers revenge ending");
        w.Phase = Phase.Playing;
        ShootFamily(1);
        w.Player = World.At(World.Exit); w.Interact();
        Check(w.RevengeEnding, "Escape after two family casualties triggers revenge ending");
        w.Phase = Phase.Playing; ShootFamily(2);
        Check(w.Family.Count(p => p.Alive) == 1 && !w.Cursed, "Curse waits for last family member");
        Check(!w.RevengeEnding, "Revenge screen only activates after escape");
        w.Player = World.At(World.Exit); w.Interact();
        Check(w.RevengeEnding, "Escape with one survivor triggers revenge ending");
        w.Phase = Phase.Playing;
        ShootFamily(3);
        Check(w.Cursed && w.Family.All(p => !p.Alive), "Last hit curses the stage");
        w.Player = World.At(World.Exit); w.Interact();
        Check(w.Phase == Phase.Playing && !w.RevengeEnding && !w.Prompt.Contains("[E]") && w.Objective.Contains("閉ざされた"), "Cursed exit blocks escape and updates guidance");
        w.Update(3, Vector2.Zero, false);
        Check(w.Cursed && !w.GhostActive && w.Family.All(p => !p.Alive), "Curse persists and nobody respawns");
        w = new World();
        Check(!w.Cursed && !w.FamilyFrightened && !w.GhostDefeated && w.Family.All(p => p.Alive), "Restart resets family and curse");
        Check(!w.RevengeEnding, "Restart clears revenge ending");
        w.Phase = Phase.Playing; w.HasRelic = true; w.GhostDefeated = true;
        w.Player = World.At(World.Exit); w.Interact();
        Check(w.Phase == Phase.Escaped && !w.RevengeEnding, "Unharmed family preserves normal ending");
        Console.WriteLine($"All {checks} gameplay checks passed.");
    }
}
