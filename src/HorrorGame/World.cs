using System.Numerics;

namespace HorrorGame;

public enum Phase { Title, Playing, Paused, Dead, Escaped }
public readonly record struct Cell(int X, int Z);
public readonly record struct MagicShot(Vector3 Position, Vector3 Direction, float Life);
public sealed class FamilyMember(string name, Vector3 position, float height)
{
    public string Name { get; } = name;
    public Vector3 Position { get; } = position;
    public float Height { get; } = height;
    public bool Alive { get; set; } = true;
}

/// <summary>描画から独立した固定マップ、衝突判定、進行状態、幽霊の経路探索。</summary>
public sealed class World
{
    public const int Width = 37, Depth = 53;
    public const float Tile = 2.5f;
    public readonly char[,] Map = new char[Width, Depth];
    public static readonly Cell Spawn = new(19, 45), Key = new(28, 23), Relic = new(6, 5);
    public static readonly Cell CaveDoor = new(9, 18), Exit = new(19, 51);
    public static readonly Cell Shotgun = new(35, 1);
    public readonly List<MagicShot> Shots = new();
    public bool HasShotgun, GhostDefeated;
    public bool GhostActive => HasKey && !GhostDefeated;
    public bool ShotgunAvailable => HasRelic && !HasShotgun;
    private float shotCooldown;
    public readonly FamilyMember[] Family =
    [
        new("Mother", At(new(21, 27)), 1.8f),
        new("Son", At(new(22, 27)), 1.4f),
        new("Daughter", At(new(23, 27)), 1.3f),
        new("Father", At(new(24, 27)), 1.95f)
    ];
    public bool FamilyFrightened => GhostDefeated && Family.Any(person => !person.Alive);
    public bool Cursed => GhostDefeated && Family.All(person => !person.Alive);
    public bool RevengeEnding => Phase == Phase.Escaped && FamilyFrightened && !Cursed;
    public string FamilyDialogue => FamilyFrightened
        ? "Stop! Please help us!\nPlease spare us!"
        : "Thank you for defeating the ghost!\nYou saved our family!";
    public Vector3 Player = At(Spawn), Ghost = At(new(25, 36));
    public float Yaw, Pitch, Stamina = 100, Battery = 100, Time, Grace = 5, Threat;
    public bool HasKey, HasRelic, CaveOpen, Flashlight = true, Running;
    public Phase Phase = Phase.Title;
    public string Message = "館に残された鍵を探せ。";
    public float MessageTime = 6;
    private float repath;
    private Queue<Cell> ghostPath = new();

    public World()
    {
        for (int z = 0; z < Depth; z++)
        for (int x = 0; x < Width; x++)
            Map[x, z] = x == 0 || z == 0 || x == Width - 1 || z == Depth - 1 ? '#' : '.';

        // 洞窟は岩の塊を掘り抜いた曲がる通路。入口以外からは侵入できない。
        for (int z = 1; z <= 18; z++)
        for (int x = 2; x <= 15; x++) Map[x, z] = 'R';
        Carve(8, 13, 10, 18); Carve(5, 11, 10, 14); Carve(4, 4, 7, 12);
        Carve(5, 3, 12, 7); Carve(10, 6, 12, 10);
        Map[CaveDoor.X, CaveDoor.Z] = 'D';
        Map[8, 18] = Map[10, 18] = 'R';

        // 館：南向き玄関、中央廊下、書斎と寝室。
        for (int z = 20; z <= 32; z++)
        for (int x = 18; x <= 31; x++)
            Map[x, z] = x == 18 || x == 31 || z == 20 || z == 32 ? 'W' : 'I';
        Map[24, 32] = Map[25, 32] = 'I';
        for (int z = 21; z < 32; z++) Map[26, z] = 'W';
        Map[26, 24] = Map[26, 29] = 'I';
        for (int x = 27; x < 31; x++) Map[x, 26] = 'W';
        Map[29, 26] = 'I';
        Map[20, 23] = Map[21, 23] = Map[28, 30] = 'F';

        var random = new Random(731);
        for (int i = 0; i < 290; i++)
        {
            int x = random.Next(2, Width - 2), z = random.Next(2, Depth - 3);
            bool path = Math.Abs(x - 19) <= 2 || Math.Abs(z - 36) <= 2 ||
                        (Math.Abs(x - 9) <= 2 && z >= 18 && z <= 38) ||
                        (x >= 22 && x <= 26 && z >= 30 && z <= 38);
            if (Map[x, z] == '.' && !path) Map[x, z] = 'T';
        }
    }

    private void Carve(int x1, int z1, int x2, int z2)
    {
        for (int z = z1; z <= z2; z++) for (int x = x1; x <= x2; x++) Map[x, z] = 'C';
    }

    public static Vector3 At(Cell cell) => new((cell.X + .5f) * Tile, 0, (cell.Z + .5f) * Tile);
    public static Cell ToCell(Vector3 p) => new((int)MathF.Floor(p.X / Tile), (int)MathF.Floor(p.Z / Tile));
    public bool Solid(int x, int z) => x < 0 || z < 0 || x >= Width || z >= Depth ||
        Map[x, z] is '#' or 'R' or 'W' or 'T' or 'F' || (Map[x, z] == 'D' && !CaveOpen);
    public bool Walkable(Vector3 p, float radius = .28f)
    {
        for (int z = (int)MathF.Floor((p.Z - radius) / Tile); z <= (int)MathF.Floor((p.Z + radius) / Tile); z++)
        for (int x = (int)MathF.Floor((p.X - radius) / Tile); x <= (int)MathF.Floor((p.X + radius) / Tile); x++)
        {
            if (!Solid(x, z)) continue;
            float dx = p.X - Math.Clamp(p.X, x * Tile, (x + 1) * Tile);
            float dz = p.Z - Math.Clamp(p.Z, z * Tile, (z + 1) * Tile);
            if (dx * dx + dz * dz < radius * radius) return false;
        }
        return true;
    }
    public Vector3 Move(Vector3 p, Vector3 delta)
    {
        // 小分け移動により低FPSでも壁をすり抜けない。
        int steps = Math.Max(1, (int)MathF.Ceiling(delta.Length() / .15f));
        delta /= steps;
        for (int i = 0; i < steps; i++)
        {
            Vector3 candidate = p + new Vector3(delta.X, 0, 0);
            if (Walkable(candidate)) p = candidate;
            candidate = p + new Vector3(0, 0, delta.Z);
            if (Walkable(candidate)) p = candidate;
        }
        return p;
    }

    public Vector3 Forward => new(MathF.Sin(Yaw) * MathF.Cos(Pitch), MathF.Sin(Pitch), -MathF.Cos(Yaw) * MathF.Cos(Pitch));
    public string Area => ToCell(Player) is var c && c.X >= 2 && c.X <= 15 && c.Z <= 18 ? "忘却の洞窟" :
        c.X >= 18 && c.X <= 31 && c.Z >= 20 && c.Z <= 32 ? "朽ちた館" : "夜霧の森";
    public string Objective => Cursed ? "出口は永遠に閉ざされた。 [R] 最初からやり直す" : !HasKey ? "01  館の書斎で、錆びた鍵を探す" : !CaveOpen ? "02  森の西にある洞窟の鉄格子を開ける" :
        !HasRelic ? "03  洞窟の奥で封印の石を回収する" : "04  森の南端へ戻り、出口の封印を解く";
    public bool Near(Cell c, float distance = 3.1f) => Vector3.Distance(Player, At(c)) < distance;
    private bool CanTakeShotgun => ShotgunAvailable && Near(Shotgun, 2.5f) && Sight(Player, At(Shotgun));
    public string Prompt => CanTakeShotgun ? "[E] 魔法のショットガンを拾う" : !HasKey && Near(Key) ? "[E] 錆びた鍵を拾う" : !CaveOpen && Near(CaveDoor) ?
        HasKey ? "[E] 鍵で鉄格子を開ける" : "鍵が必要だ。館を探そう。" : !HasRelic && Near(Relic) ? "[E] 封印の石を取る" :
        Near(Exit) ? Cursed ? "出口は閉ざされている。もう、開くことはない。" : HasRelic ? "[E] 封印を解いて脱出する" : "出口は封じられている。洞窟の石が必要だ。" : "";
    public void Say(string text) { Message = text; MessageTime = 5; }
    public void Interact()
    {
        if (Phase != Phase.Playing) return;
        if (CanTakeShotgun) { HasShotgun = true; Say("魔法のショットガンを手に入れた。[SPACE] で発射。"); return; }
        if (!HasKey && Near(Key)) { HasKey = true; Grace = 4; Say("鍵を手に入れた。背後で、何かが目を覚ました。西の洞窟へ！"); }
        else if (!CaveOpen && Near(CaveDoor))
        {
            if (HasKey) { CaveOpen = true; Say("鉄格子が開いた。奥から冷たい風が吹いている。"); }
            else Say("鍵がかかっている。館の書斎を調べよう。");
        }
        else if (!HasRelic && Near(Relic)) { HasRelic = true; Say("封印の石を手に入れた。森の南端へ戻れ！"); }
        else if (Near(Exit))
        {
            if (Cursed) Say("祭壇は沈黙している。出口は永遠に閉ざされた。");
            else if (HasRelic) Phase = Phase.Escaped;
            else Say("石のない祭壇。洞窟の封印の石があれば……。");
        }
    }
    public void Update(float dt, Vector2 input, bool sprint)
    {
        if (Phase != Phase.Playing) return;
        Time += dt; MessageTime -= dt; Grace = Math.Max(0, Grace - dt);
        if (input.LengthSquared() > 1) input = Vector2.Normalize(input);
        Running = sprint && input.LengthSquared() > .01f && Stamina > 1;
        Stamina = Math.Clamp(Stamina + (Running ? -23 : 15) * dt, 0, 100);
        Battery = Math.Clamp(Battery + (Flashlight ? -2.5f : 7) * dt, 0, 100);
        if (Battery <= 0) { Flashlight = false; Say("電池が切れた。消灯中に充電される。"); }
        Vector3 forward = new(MathF.Sin(Yaw), 0, -MathF.Cos(Yaw));
        Vector3 right = new(MathF.Cos(Yaw), 0, MathF.Sin(Yaw));
        Player = Move(Player, (forward * input.Y + right * input.X) * (Running ? 6.8f : 3.5f) * dt);
        shotCooldown = Math.Max(0, shotCooldown - dt);
        UpdateShots(dt);
        float distance = Vector3.Distance(Player, Ghost);
        Threat = GhostActive ? Math.Clamp(1 - distance / 23, 0, 1) : 0;
        if (!GhostActive || Grace > 0) return;
        repath -= dt;
        if (repath <= 0) { ghostPath = new Queue<Cell>(FindPath(ToCell(Ghost), ToCell(Player))); repath = .5f; }
        Vector3 target = ghostPath.Count > 0 ? At(ghostPath.Peek()) : Player;
        Vector3 direction = target - Ghost;
        if (direction.Length() < .15f && ghostPath.Count > 0) ghostPath.Dequeue();
        bool lit = Flashlight && distance < 18 && distance > .01f &&
            Vector3.Dot(Forward, Vector3.Normalize(Ghost + new Vector3(0, 1.2f, 0) - (Player + new Vector3(0, 1.65f, 0)))) > .9f && Sight(Player, Ghost);
        float speed = lit ? .9f : HasRelic ? 4.15f : 3.7f;
        if (direction.Length() > .01f) Ghost = Move(Ghost, Vector3.Normalize(direction) * Math.Min(direction.Length(), speed * dt));
        if (Vector3.Distance(Player, Ghost) < .9f) Phase = Phase.Dead;
    }
    public bool Fire()
    {
        if (Phase != Phase.Playing || !HasShotgun || shotCooldown > 0) return false;
        Shots.Add(new(Player + new Vector3(0, 1.65f, 0), Forward, 2));
        shotCooldown = .55f;
        return true;
    }
    private void UpdateShots(float dt)
    {
        for (int i = Shots.Count - 1; i >= 0; i--)
        {
            MagicShot shot = Shots[i];
            float travel = Math.Min(dt, shot.Life) * 32;
            int steps = Math.Max(1, (int)MathF.Ceiling(travel / .1f));
            Vector3 p = shot.Position;
            bool ended = false;
            for (int step = 0; step < steps; step++)
            {
                p += shot.Direction * (travel / steps);
                if (p.Y <= .05f || !Walkable(p, .08f)) { ended = true; break; }
                // 胴体から頭までの縦長の当たり判定。小刻みに進めて弾のすり抜けを防ぐ。
                Vector3 closest = Ghost + new Vector3(0, Math.Clamp(p.Y - Ghost.Y, .35f, 2.35f), 0);
                if (GhostActive && Vector3.DistanceSquared(p, closest) < .55f * .55f)
                {
                    GhostDefeated = true; Threat = 0; ghostPath.Clear();
                    Say("影は消え去った。館の中から、人の声が聞こえる。");
                    ended = true; break;
                }
                if (GhostDefeated)
                {
                    foreach (FamilyMember person in Family)
                    {
                        if (!person.Alive) continue;
                        Vector3 body = person.Position + new Vector3(0, Math.Clamp(p.Y, .25f, person.Height - .2f), 0);
                        if (Vector3.DistanceSquared(p, body) >= .32f * .32f) continue;
                        person.Alive = false;
                        if (Cursed) Say("館は静まり返った。世界は赤く染まり、出口は閉ざされた。");
                        ended = true; break;
                    }
                    if (ended) break;
                }
            }
            if (ended || shot.Life <= dt) Shots.RemoveAt(i);
            else Shots[i] = shot with { Position = p, Life = shot.Life - dt };
        }
    }
    public bool Sight(Vector3 a, Vector3 b)
    {
        int samples = Math.Max(1, (int)(Vector3.Distance(a, b) / .4f));
        for (int i = 1; i < samples; i++) { Cell c = ToCell(Vector3.Lerp(a, b, i / (float)samples)); if (Solid(c.X, c.Z)) return false; }
        return true;
    }
    public List<Cell> FindPath(Cell from, Cell to)
    {
        var queue = new Queue<Cell>();
        var previous = new Dictionary<Cell, Cell> { [from] = from };
        queue.Enqueue(from);
        while (queue.TryDequeue(out Cell c))
        {
            if (c == to)
            {
                var path = new List<Cell>();
                while (c != from) { path.Add(c); c = previous[c]; }
                path.Reverse(); return path;
            }
            foreach (Cell n in new Cell[] { new(c.X - 1, c.Z), new(c.X + 1, c.Z), new(c.X, c.Z - 1), new(c.X, c.Z + 1) })
                if (!Solid(n.X, n.Z) && previous.TryAdd(n, c)) queue.Enqueue(n);
        }
        return [];
    }
}
