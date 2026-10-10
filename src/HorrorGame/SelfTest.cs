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
		w.Player = World.At(new(9, 20)); w.Update(0, Vector2.Zero, false);
		Check(!w.FamilyGhostsAwakened, "Normal cave approach does not awaken family ghosts");
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
		w = new World
		{
			Phase = Phase.Playing,
			HasShotgun = true,
			HasKey = true,
			Grace = 100,
			Player = World.At(new(24, 21)),
			Ghost = World.At(new(24, 19))
		};
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
		Check(EndingAchievements.FromWorld(w) == EndingId.Revenge, "Revenge ending maps to its achievement");
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
		Check(w.Cursed && !w.FamilyGhostsAwakened, "Curse waits for cave approach");
		w.Player = World.At(new(9, 21)); w.Update(.01f, Vector2.Zero, false);
		Check(!w.FamilyGhostsAwakened, "Family ghosts stay dormant more than five meters before entrance");
		w.Player = World.At(new(12, 20)); w.Update(.01f, Vector2.Zero, false);
		Check(!w.FamilyGhostsAwakened, "Passing beside cave away from entrance path does not awaken ghosts");
		w.Player = World.At(new(9, 20));
		w.Phase = Phase.Paused; w.Update(.01f, Vector2.Zero, false);
		Check(!w.FamilyGhostsAwakened, "Pause does not trigger family ghosts at cave approach");
		w.Phase = Phase.Playing; w.Update(.01f, Vector2.Zero, false);
		Check(w.Area == "夜霧の森" && w.FamilyGhostsAwakened && w.FamilyGhosts.Count(g => g.Alive) == 4,
			"Approaching cave entrance from forest awakens all four ghosts before entry");
		w.Update(.01f, Vector2.Zero, false);
		Check(w.FamilyGhosts.Count == 4, "Waiting at cave approach does not duplicate family ghosts");
		Check(w.FamilyGhosts.Select(g => g.Scale).SequenceEqual(new[] { 1f, .5f, .5f, 1f }), "Parents keep full size and children have half size");
		Check(w.FamilyGhosts.All(g => w.Walkable(g.Position) && w.FindPath(World.ToCell(g.Position), World.Relic).Count > 0), "All four corner spawns can reach cave");
		Check(w.FamilyGhosts.Select(g => (g.Position.X < World.Width * World.Tile / 2, g.Position.Z < World.Depth * World.Tile / 2)).Distinct().Count() == 4, "Ghosts occupy four different map corners");
		Vector3[] positions = w.FamilyGhosts.Select(g => g.Position).ToArray();
		w.Phase = Phase.Paused; w.Update(1, Vector2.Zero, false);
		Check(w.FamilyGhosts.Select(g => g.Position).SequenceEqual(positions), "Pause freezes all family ghosts");
		w.Phase = Phase.Playing; w.Player = World.At(new(19, 36)); w.Flashlight = false;
		for (int i = 0; i < 4; i++) w.FamilyGhosts[i].Position = w.Player + new Vector3(-14, 0, (i - 1.5f) * 1.1f);
		positions = w.FamilyGhosts.Select(g => g.Position).ToArray();
		for (int frame = 0; frame < 20; frame++) w.Update(.02f, Vector2.Zero, false);
		Check(w.FamilyGhosts.All(g => Vector3.Distance(g.Position, w.Player) < 12), "Every family ghost pursues player at increased speed");
		Check(w.FamilyGhosts.Select((g, i) => Vector3.Distance(g.Position, positions[i])).All(d => d > 3 && d <= 4.2f), "Family ghosts move approximately 2.5 times normal speed");
		Check(w.FamilyGhosts.SelectMany((a, i) => w.FamilyGhosts.Skip(i + 1).Select(b => Vector3.Distance(a.Position, b.Position))).All(d => d >= .99f), "Repulsion preserves space between converging ghosts");
		// 実際の発射と弾移動で4体を順番に撃破。最後の1体までは出口を開かない。
		for (int i = 0; i < 4; i++)
		{
			w.Shots.Clear();
			w.Player = World.At(World.Exit); w.Update(.6f, Vector2.Zero, false);
			for (int j = i; j < 4; j++) w.FamilyGhosts[j].Position = World.At(new(19, 36)) + new Vector3((j - i) * 4, 0, 0);
			w.Player = World.At(new(19, 36)) + Vector3.UnitZ * 3;
			w.Yaw = 0; w.Pitch = MathF.Atan2(1.5f * w.FamilyGhosts[i].Scale - 1.65f, 3); w.Flashlight = true;
			Check(w.Fire(), "Shotgun fires at revived family ghost");
			w.Update(.1f, Vector2.Zero, false);
			Check(!w.FamilyGhosts[i].Alive, "Shotgun defeats revived ghost in one hit");
			Check(w.Cursed == (i < 3), "World stays red until final ghost is defeated");
		}
		Check(w.FamilyGhostsDefeated && !w.Cursed && w.Threat == 0, "Final kill restores normal world and clears threat");
		Check(w.WitnessAppeared && w.WitnessActive && !w.WitnessFleeing && w.WitnessPosition == World.At(World.WitnessSpawn),
			"Final family ghost defeat spawns watching witness east of manor");
		w.Player = World.At(new(9, 17)); w.Update(.1f, Vector2.Zero, false);
		Check(w.FamilyGhosts.Count == 4 && w.FamilyGhosts.All(g => !g.Alive), "Returning to cave does not respawn family ghosts");
		w.Player = World.At(World.Exit); w.Interact();
		Check(w.Phase == Phase.Escaped && w.ConvictionEnding && w.BadEnding && !w.RevengeEnding, "Restored exit leads to conviction BAD END");
		Check(EndingAchievements.FromWorld(w) == EndingId.Conviction, "Conviction ending maps to its achievement");
		w.Phase = Phase.Playing; w.FamilyGhosts[0].Alive = true; w.FamilyGhosts[0].Position = w.Player;
		w.Update(.01f, Vector2.Zero, false);
		Check(w.Phase == Phase.Dead, "Revived ghost catches player");
		w = new World();
		Check(!w.Cursed && !w.FamilyFrightened && !w.GhostDefeated && w.Family.All(p => p.Alive), "Restart resets family and curse");
		Check(!w.RevengeEnding, "Restart clears revenge ending");
		Check(!w.FamilyGhostsAwakened && !w.ConvictionEnding && w.FamilyGhosts.Count == 0, "Restart clears family ghost encounter and conviction ending");
		Check(!w.WitnessAppeared && !w.WitnessActive && !w.WitnessFleeing && !w.WitnessDefeated && !w.WitnessHiding && !w.WitnessHasFled && w.WitnessEscapeTime == 0, "Restart clears witness encounter");
		w.Phase = Phase.Playing; w.HasRelic = true; w.GhostDefeated = true;
		w.Player = World.At(World.Exit); w.Interact();
		Check(w.Phase == Phase.Escaped && !w.RevengeEnding, "Unharmed family preserves normal ending");
		Check(EndingAchievements.FromWorld(w) == EndingId.Dawn, "Good ending maps to its achievement");
		for (int corner = 0; corner < 4; corner++)
		{
			var chase = new World
			{
				Phase = Phase.Playing,
				HasKey = true,
				HasRelic = true,
				CaveOpen = true,
				GhostDefeated = true,
				Flashlight = false,
				Player = World.At(new(9, 20))
			};
			foreach (FamilyMember person in chase.Family) person.Alive = false;
			chase.Update(.01f, Vector2.Zero, false);
			chase.Player = World.At(World.Relic);
			for (int i = 0; i < 4; i++) chase.FamilyGhosts[i].Alive = i == corner;
			for (int frame = 0; frame < 4000 && chase.Phase == Phase.Playing; frame++)
				chase.Update(.02f, Vector2.Zero, false);
			Check(chase.Phase == Phase.Dead, $"Ghost from corner {corner + 1} physically navigates into cave and catches player");
		}
		foreach (float scale in new[] { 1f, .5f })
		{
			var lightChase = new World { Phase = Phase.Playing, GhostDefeated = true, HasRelic = true, Player = World.At(new(19, 36)) };
			var darkChase = new World { Phase = Phase.Playing, GhostDefeated = true, HasRelic = true, Flashlight = false, Player = lightChase.Player };
			Vector3 start = lightChase.Player - Vector3.UnitZ * 6;
			lightChase.FamilyGhosts.Add(new(start, scale) { WanderTime = 100 });
			darkChase.FamilyGhosts.Add(new(start, scale) { WanderTime = 100 });
			lightChase.Update(.02f, Vector2.Zero, false); darkChase.Update(.02f, Vector2.Zero, false);
			float ratio = Vector3.Distance(start, lightChase.FamilyGhosts[0].Position) / Vector3.Distance(start, darkChase.FamilyGhosts[0].Position);
			Check(Math.Abs(ratio - .8f) < .001f, $"Flashlight retains 80 percent speed for ghost scale {scale}");
		}
		var smallTarget = new World { Phase = Phase.Playing, GhostDefeated = true, HasShotgun = true, Player = World.At(new(19, 36)) };
		smallTarget.FamilyGhosts.Add(new(smallTarget.Player - Vector3.UnitZ * 3, .5f));
		smallTarget.Fire(); smallTarget.Update(.12f, Vector2.Zero, false);
		Check(smallTarget.FamilyGhosts[0].Alive, "Shot above half-height ghost misses its scaled hitbox");
		World WitnessEncounter()
		{
			var encounter = new World { Phase = Phase.Playing, GhostDefeated = true, HasShotgun = true,
				HasRelic = true, CaveOpen = true, Player = World.At(new(9, 20)) };
			foreach (FamilyMember person in encounter.Family) person.Alive = false;
			encounter.Update(.01f, Vector2.Zero, false);
			Check(!encounter.WitnessAppeared, "Witness waits while family ghosts are alive");
			foreach (FamilyGhost ghost in encounter.FamilyGhosts) ghost.Alive = false;
			encounter.Player = World.At(World.Spawn);
			encounter.Update(.01f, Vector2.Zero, false);
			return encounter;
		}
		var witness = WitnessEncounter();
		Vector3 watching = witness.WitnessPosition;
		Check(witness.Walkable(watching) && witness.FindPath(World.Spawn, World.WitnessSpawn).Count > 0, "Witness spawns on reachable ground outside east wall");
		witness.Update(2, Vector2.Zero, false);
		Check(witness.WitnessPosition == watching && !witness.WitnessFleeing, "Distant witness watches without moving");
		Check(Vector3.Dot(witness.WitnessFacing, Vector3.Normalize(witness.Player - watching)) > .99f, "Watching witness faces player");
		witness.Player = World.At(new(30, 31));
		witness.Update(.1f, Vector2.Zero, false);
		Check(!witness.WitnessFleeing, "Manor wall hides nearby player from witness");
		witness.Yaw = MathF.PI / 2; witness.Pitch = 0;
		Check(witness.Fire() && witness.WitnessFleeing, "Gunshot through wall triggers flight immediately");
		witness.Update(.2f, Vector2.Zero, false);
		Check(witness.WitnessActive && witness.Shots.Count == 0, "Wall blocks projectile aimed at witness");
		witness.Player = World.At(World.Spawn);
		float traveled = 0;
		var explored = new HashSet<Cell>();
		for (int frame = 0; frame < 1200; frame++)
		{
			Vector3 last = witness.WitnessPosition;
			witness.Update(.02f, Vector2.Zero, false);
			traveled += Vector3.Distance(last, witness.WitnessPosition);
			explored.Add(World.ToCell(witness.WitnessPosition));
			if (!witness.Walkable(witness.WitnessPosition)) throw new InvalidOperationException("Witness crossed an obstacle");
		}
		Check(traveled > 35 && explored.Count > 10, "Witness runs through navigable terrain to reach cover");
		Check(witness.Phase == Phase.Playing && witness.WitnessHiding && !witness.WitnessFleeing, "Distant witness stops running and hides");
		Check(Vector3.Distance(witness.Player, witness.WitnessPosition) >= World.WitnessSafeDistance && !witness.Sight(witness.Player, witness.WitnessPosition), "Witness hides out of sight at safe distance");
		Vector3 hidden = witness.WitnessPosition;
		witness.Update(2, Vector2.Zero, false);
		Check(witness.WitnessPosition == hidden, "Hidden witness waits motionless");
		Vector3 pausedWitness = witness.WitnessPosition; float pausedRun = witness.WitnessRunTime;
		witness.Phase = Phase.Paused; witness.Update(1, Vector2.Zero, false);
		Check(!witness.Fire() && witness.WitnessPosition == pausedWitness && witness.WitnessRunTime == pausedRun, "Pause freezes witness movement and animation");
		witness = WitnessEncounter();
		witness.Player = watching + Vector3.UnitZ * 5;
		witness.Update(.02f, Vector2.Zero, false);
		Check(witness.WitnessFleeing && witness.WitnessPosition != watching, "Visible player approaching within seven meters triggers flight");
		// 発射直前に毎回照準を合わせても、最初の5秒間は確実に回避する。
		witness.Player = witness.WitnessPosition + Vector3.UnitX;
		witness.Yaw = -MathF.PI / 2; witness.Pitch = 0;
		Check(witness.Fire(), "Shotgun can fire at fleeing witness");
		witness.Update(.05f, Vector2.Zero, false);
		Check(witness.WitnessActive && witness.WitnessDodging && witness.WitnessEscapeTime < 5, "Point blank shot is visibly dodged during first five seconds");
		for (int attempt = 0; attempt < 7; attempt++)
		{
			witness.Player = World.At(World.Spawn); witness.Update(.55f, Vector2.Zero, false);
			witness.Player = witness.WitnessPosition + Vector3.UnitX;
			witness.Fire(); witness.Update(.05f, Vector2.Zero, false);
			Check(witness.WitnessActive && witness.WitnessEscapeTime < 5, "Repeated aimed shot cannot defeat witness in evasion window");
		}
		witness.Player = World.At(World.Spawn); witness.Flashlight = false;
		for (int frame = 0; frame < 1500 && !witness.WitnessHiding; frame++) witness.Update(.02f, Vector2.Zero, false);
		Check(witness.WitnessHiding && witness.WitnessEscapeTime >= 5, "Witness seeks cover after initial evasion ends");
		// 隠れ場所への通行可能な接近位置を選び、ライト・足音・静かな歩行を比較。
		Vector3 NearWitness(World target, float minimum, float maximum)
		{
			for (int z = 1; z < World.Depth - 1; z++) for (int x = 1; x < World.Width - 1; x++)
			{
				Vector3 p = World.At(new(x, z)); float gap = Vector3.Distance(p, target.WitnessPosition);
				if (gap >= minimum && gap <= maximum && target.Walkable(p) && target.Sight(p, target.WitnessPosition)) return p;
			}
			throw new InvalidOperationException("No clear approach to witness cover");
		}
		Vector3 close = NearWitness(witness, 2, 4);
		witness.Player = close; witness.Update(.1f, new(.1f, 0), false);
		Check(witness.WitnessHiding && !witness.WitnessFleeing, "Walking in darkness reaches close range without detection");
		witness.Phase = Phase.Paused; float frozenEscape = witness.WitnessEscapeTime;
		witness.Update(10, Vector2.Zero, false);
		Check(witness.WitnessEscapeTime == frozenEscape, "Pause freezes initial evasion clock");
		witness.Phase = Phase.Playing; witness.Player = close;
		witness.Flashlight = true; witness.Update(.02f, Vector2.Zero, false);
		Check(witness.WitnessFleeing && !witness.WitnessHiding, "Light immediately alerts hidden witness at close range");
		Check(witness.WitnessEscapeTime > 5, "Reawakening never renews the first five seconds of evasion");
		witness.Flashlight = false; witness.Player = World.At(World.Spawn);
		for (int frame = 0; frame < 1500 && !witness.WitnessHiding; frame++) witness.Update(.02f, Vector2.Zero, false);
		Check(witness.WitnessHiding, "Witness hides again after escaping flashlight approach");
		witness.Player = NearWitness(witness, 4, 8); witness.Update(.02f, new(.1f, 0), true);
		Check(witness.WitnessFleeing && !witness.WitnessHiding, "Sprinting alerts hidden witness even with flashlight off");
		witness.Player = World.At(World.Spawn);
		for (int frame = 0; frame < 1500 && !witness.WitnessHiding; frame++) witness.Update(.02f, Vector2.Zero, false);
		Check(witness.WitnessHiding, "Witness finds cover again after hearing sprint");
		witness.Player = witness.WitnessPosition + Vector3.UnitX * .8f;
		witness.Update(.02f, Vector2.Zero, false);
		Check(witness.WitnessFleeing, "Darkness does not conceal player at point blank distance");
		witness.Player = World.At(World.Spawn);
		for (int frame = 0; frame < 1500 && !witness.WitnessHiding; frame++) witness.Update(.02f, Vector2.Zero, false);
		witness.Player = NearWitness(witness, 2, 4);
		witness.Update(.6f, Vector2.Zero, false);
		Vector3 aim = witness.WitnessPosition - witness.Player;
		witness.Yaw = MathF.Atan2(aim.X, -aim.Z); witness.Pitch = 0;
		Check(witness.WitnessHiding && witness.Fire(), "Quiet close approach allows a shot at hidden witness");
		witness.Update(.15f, Vector2.Zero, false);
		Check(witness.WitnessDefeated && !witness.WitnessActive, "One close shot defeats witness after evasion window");
		Vector3 fallen = witness.WitnessPosition;
		witness.Update(3, Vector2.Zero, false);
		Check(witness.WitnessDefeated && witness.WitnessPosition == fallen && !witness.Objective.Contains("目撃者"), "Defeated witness stops moving and never respawns");
		Check(!witness.SilentWitnessEnding, "Witness death alone does not start ending before altar interaction");
		witness.Player = World.At(World.Exit); witness.Interact();
		Check(witness.Phase == Phase.Escaped && witness.SilentWitnessEnding && witness.BadEnding && !witness.ConvictionEnding,
			"Escaping after killing witness selects silent witness instead of conviction ending");
		Check(EndingAchievements.FromWorld(witness) == EndingId.SilentWitness, "Silent witness achievement takes priority over conviction");
		float endTime = witness.Time; Vector3 endPosition = witness.Player;
		witness.Update(10, Vector2.One, true);
		Check(witness.Time == endTime && witness.Player == endPosition && !witness.Fire(), "Ending freezes gameplay and firing");
		Check(!new World().SilentWitnessEnding, "Restart clears silent witness ending");
		var ending = new WitnessEnding();
		ending.Next(); ending.Update(WitnessEnding.FadeSeconds / 2);
		Check(!ending.Reading && ending.Page == 0 && ending.WorldFade == .5f, "Fade starts before pages and ignores premature NEXT");
		ending.Update(WitnessEnding.FadeSeconds / 2);
		Check(ending.Reading && ending.Page == 0 && ending.WorldFade == 1, "Full darkness opens first ending page");
		ending.Update(60);
		Check(ending.Page == 0 && !ending.Finale, "Reading page waits for NEXT without timeout");
		ending.Next(); Check(ending.Page == 1 && ending.Reading, "NEXT opens second page");
		ending.Next(); Check(ending.Page == 2 && ending.Reading, "NEXT opens third page");
		ending.Next(); ending.Next();
		Check(ending.Finale && ending.Page == 2 && !ending.Complete, "Last NEXT starts finale and repeated clicks cannot skip it");
		ending.Update(WitnessEnding.TextFadeSeconds);
		Check(ending.TextOpacity == 0 && ending.EyeOpacity == 0, "Final text disappears before eyes appear");
		ending.Update(WitnessEnding.DarknessSeconds + WitnessEnding.EyeFadeSeconds);
		Check(ending.EyeOpacity > .99f && !ending.Complete, "White eyes fade in and hold their gaze");
		Check(ending.SmileOpacity == 0, "Eyes appear before the grin");
		ending.Update(WitnessEnding.SmileDelaySeconds + WitnessEnding.SmileFadeSeconds / 2);
		Check(ending.SmileOpacity > .49f && ending.SmileOpacity < .51f && ending.EyeOpacity > .99f,
			"Grin gradually appears while eyes keep watching");
		ending.Update(WitnessEnding.SmileFadeSeconds / 2);
		Check(ending.SmileOpacity > .99f && !ending.Complete, "Full grin holds before title return");
		ending.Update(WitnessEnding.EyeHoldSeconds - WitnessEnding.SmileDelaySeconds - WitnessEnding.SmileFadeSeconds + WitnessEnding.EyeOutSeconds + .01f);
		Check(ending.Complete && ending.EyeOpacity == 0 && ending.SmileOpacity == 0, "Eyes and grin fade out together for title return");
		var escaping = WitnessEncounter();
		escaping.Player = escaping.WitnessPosition + Vector3.UnitZ * 5;
		escaping.Update(.02f, Vector2.Zero, false);
		float initialGap = Vector3.Distance(escaping.Player, escaping.WitnessPosition);
		escaping.Update(1, Vector2.Zero, false);
		Check(Vector3.Distance(escaping.Player, escaping.WitnessPosition) > initialGap + 5, "Escape route primarily increases distance from player");
		var longFrame = WitnessEncounter();
		longFrame.Player = longFrame.WitnessPosition + Vector3.UnitX;
		longFrame.Yaw = -MathF.PI / 2;
		longFrame.Fire(); longFrame.Update(6, Vector2.Zero, false);
		Check(longFrame.WitnessActive && longFrame.WitnessEscapeTime >= 5, "Long frame cannot kill witness with an impact occurring in first five seconds");
		World SteadyWitnessShot(float wait = 1)
		{
			World target = WitnessEncounter();
			target.Player = target.WitnessPosition + Vector3.UnitZ * 10;
			target.Flashlight = false; target.Yaw = 0; target.Pitch = 0;
			target.Update(0, Vector2.Zero, false);
			target.Update(wait, Vector2.Zero, false);
			return target;
		}
		var steady = SteadyWitnessShot();
		Check(steady.Fire() && !steady.WitnessFleeing && steady.Shots[0].UnnoticedByWitness,
			"Direct shot after exactly one second standing in darkness does not alert witness");
		steady.Update(.4f, Vector2.Zero, false);
		Check(steady.WitnessDefeated && !steady.WitnessHasFled, "Unnoticed direct shot hits without triggering flight or dodge");
		var tooSoon = SteadyWitnessShot(.99f);
		Check(tooSoon.Fire() && tooSoon.WitnessFleeing && !tooSoon.Shots[0].UnnoticedByWitness,
			"Standing for less than one second still alerts witness");
		var litShot = SteadyWitnessShot(); litShot.Flashlight = true;
		Check(litShot.Fire() && litShot.WitnessFleeing, "Flashlight on prevents unnoticed shot");
		var movingShot = SteadyWitnessShot();
		Check(movingShot.Fire(true) && movingShot.WitnessFleeing, "Movement on firing frame prevents unnoticed shot");
		var movedShot = SteadyWitnessShot();
		movedShot.Update(.01f, new(.1f, 0), false);
		movedShot.Update(.99f, Vector2.Zero, false);
		Check(movedShot.Fire() && movedShot.WitnessFleeing, "Movement resets one second stationary requirement");
		var missShot = SteadyWitnessShot(); missShot.Yaw = MathF.PI;
		Check(missShot.Fire() && missShot.WitnessFleeing, "Off-target shot still alerts witness in darkness");
		var blockedShot = SteadyWitnessShot();
		Cell obstruction = World.ToCell((blockedShot.Player + blockedShot.WitnessPosition) / 2);
		blockedShot.Map[obstruction.X, obstruction.Z] = 'W';
		Check(blockedShot.Fire() && blockedShot.WitnessFleeing, "Wall prevents direct-course exception");
		var pausedSteadyShot = SteadyWitnessShot(.5f); pausedSteadyShot.Phase = Phase.Paused;
		pausedSteadyShot.Update(10, Vector2.Zero, false); pausedSteadyShot.Phase = Phase.Playing;
		Check(pausedSteadyShot.Fire() && pausedSteadyShot.WitnessFleeing, "Pause does not count toward stationary requirement");
		var evadingShot = SteadyWitnessShot();
		evadingShot.Yaw = MathF.PI; evadingShot.Fire();
		evadingShot.Update(.6f, Vector2.Zero, false);
		Vector3 directAim = evadingShot.WitnessPosition + Vector3.UnitY * 1.65f
			- (evadingShot.Player + Vector3.UnitY * 1.65f);
		evadingShot.Yaw = MathF.Atan2(directAim.X, -directAim.Z);
		evadingShot.Pitch = 0;
		Check(evadingShot.Fire() && evadingShot.Shots[^1].UnnoticedByWitness, "Unnoticed shot can target witness during initial evasion");
		evadingShot.Update(.5f, Vector2.Zero, false);
		Check(evadingShot.WitnessDefeated && evadingShot.WitnessEscapeTime < World.WitnessEvasionSeconds,
			"Unnoticed direct hit bypasses first five seconds of dodge");
		Check(EndingAchievements.FromWorld(new World()) == null &&
			EndingAchievements.FromWorld(new World { Phase = Phase.Dead }) == null,
			"Title and game over do not unlock ending achievements");
#if DEBUG
		Check(File.Exists(Path.Combine(Path.GetDirectoryName(SaveDataPaths.DirectoryPath)!, "Horror.sln")) &&
			Path.GetFileName(SaveDataPaths.DirectoryPath) == "SaveData", "Debug saves beside solution in SaveData");
#else
		Check(SaveDataPaths.DirectoryPath == AppContext.BaseDirectory, "Release saves beside executable");
#endif
		string testDirectory = Path.Combine(SaveDataPaths.DirectoryPath, "SelfTest-" + Guid.NewGuid().ToString("N"));
		string testFile = Path.Combine(testDirectory, "ending-achievements.json");
		try
		{
			var saved = new EndingAchievements(testFile);
			Check(saved.Count == 0 && EndingAchievements.Endings.All(id => saved.Label(id) == "？？？") && !File.Exists(testFile),
				"Missing save shows only unknown endings without creating a file");
			saved.Unlock(EndingId.SilentWitness);
			var reloaded = new EndingAchievements(testFile);
			Check(reloaded.Count == 1 && reloaded.Seen(EndingId.SilentWitness) && !reloaded.Seen(EndingId.Conviction) &&
				reloaded.Label(EndingId.SilentWitness).Contains("消えない目撃者") && reloaded.Label(EndingId.Conviction) == "？？？",
				"Restart restores viewed ending and keeps other names hidden");
			reloaded.Unlock(EndingId.Dawn); reloaded.Unlock(EndingId.Dawn);
			Check(new EndingAchievements(testFile).Count == 2 && !File.Exists(testFile + ".tmp"),
				"Saving merges past achievements without duplicates or leftover temporary file");
			File.WriteAllText(testFile, "[\"Dawn\",\"Dawn\",\"FutureEnding\",\"999\"]");
			Check(new EndingAchievements(testFile).Count == 1, "Load ignores unknown IDs and duplicates");
			File.WriteAllText(testFile, "broken json");
			var damaged = new EndingAchievements(testFile);
			Check(damaged.Count == 0 && damaged.StorageError != null, "Damaged save reports error without crashing game");
			damaged.Unlock(EndingId.Revenge);
			Check(damaged.StorageError == null && new EndingAchievements(testFile).Seen(EndingId.Revenge),
				"New achievement can recover damaged save");
			var blockedSave = new EndingAchievements(Path.Combine(testFile, "blocked.json"));
			blockedSave.Unlock(EndingId.Dawn);
			Check(blockedSave.StorageError != null && blockedSave.Seen(EndingId.Dawn),
				"Write failure preserves achievement in memory and reports error");
		}
		finally
		{
			if (File.Exists(testFile)) File.Delete(testFile);
			if (File.Exists(testFile + ".tmp")) File.Delete(testFile + ".tmp");
			if (Directory.Exists(testDirectory)) Directory.Delete(testDirectory);
		}
		Console.WriteLine($"All {checks} gameplay checks passed.");
	}
}
