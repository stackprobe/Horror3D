using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace HorrorGame;

public sealed class Game : IDisposable
{
    private const int Width = 1280, Height = 800;

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint
    {
        public int X, Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out CursorPoint point);

    private readonly Color paper = new(222, 228, 216, 255), amber = new(201, 168, 108, 255);
    private readonly Color muted = new(127, 153, 158, 255), dark = new(6, 11, 14, 255);
    private World world = new();
    private readonly Font font;
    private readonly Shader shader;
    private readonly Soundscape sound;
    private Camera3D camera;
    private bool mapVisible, quit;
    private bool borderlessFullscreen;
    private Vector2 windowedPosition;
    private int windowedWidth, windowedHeight;
    private float footstep;
    private readonly int eyeLocation, beamLocation, lampLocation, exitGlowLocation, curseLocation;

    public Game()
    {
        SetConfigFlags(ConfigFlags.Msaa4xHint | ConfigFlags.VSyncHint | ConfigFlags.HiddenWindow);
        InitWindow(Width, Height, "HOLLOW PINES | 夜霧の館");
        PlaceWindowOnCursorMonitor();
        ClearWindowState(ConfigFlags.HiddenWindow);
        SetTargetFPS(60);
        SetExitKey(KeyboardKey.Null);
        string fontPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "meiryo.ttc");
        if (!File.Exists(fontPath)) fontPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "YuGothM.ttc");
        if (!File.Exists(fontPath)) throw new FileNotFoundException("メイリオまたは游ゴシックが必要です。", fontPath);
        var assembly = Assembly.GetExecutingAssembly();
        string glyphs = string.Concat(assembly.GetManifestResourceNames().Where(n => n.EndsWith(".cs"))
            .Select(n => { using var reader = new StreamReader(assembly.GetManifestResourceStream(n)!); return reader.ReadToEnd(); }));
        int[] codes = glyphs.Select(c => (int)c).Concat(Enumerable.Range(32, 95)).Distinct().ToArray();
        font = LoadFontFromMemory(".ttf", FontLoader.ReadTrueType(fontPath), 32, codes, codes.Length);
        SetTextureFilter(font.Texture, TextureFilter.Bilinear);
        shader = LoadShader(Path.Combine(AppContext.BaseDirectory, "Shaders/world.vs"), Path.Combine(AppContext.BaseDirectory, "Shaders/world.fs"));
        eyeLocation = GetShaderLocation(shader, "eye");
        beamLocation = GetShaderLocation(shader, "beam");
        lampLocation = GetShaderLocation(shader, "lamp");
        exitGlowLocation = GetShaderLocation(shader, "exitGlow");
        curseLocation = GetShaderLocation(shader, "curse");
        if (curseLocation < 0) throw new InvalidOperationException("呪いシェーダーの読み込みに失敗しました。");
        if (eyeLocation < 0 || beamLocation < 0 || lampLocation < 0 || exitGlowLocation < 0) throw new InvalidOperationException("照明シェーダーの読み込みに失敗しました。");
        camera = new Camera3D { Up = Vector3.UnitY, FovY = 70, Projection = CameraProjection.Perspective };
        sound = new Soundscape();
    }
    private static void PlaceWindowOnCursorMonitor()
    {
        if (!GetCursorPos(out CursorPoint cursor)) return;

        for (int monitor = 0; monitor < GetMonitorCount(); monitor++)
        {
            Vector2 position = GetMonitorPosition(monitor);
            int width = GetMonitorWidth(monitor), height = GetMonitorHeight(monitor);
            if (cursor.X < position.X || cursor.X >= position.X + width ||
                cursor.Y < position.Y || cursor.Y >= position.Y + height) continue;

            int windowWidth = Math.Min(Width, width);
            int windowHeight = Math.Min(Height, height);
            SetWindowSize(windowWidth, windowHeight);
            SetWindowPosition((int)position.X + (width - windowWidth) / 2,
                (int)position.Y + (height - windowHeight) / 2);
            return;
        }
    }

    public void Run(bool smokeTest)
    {
        int frames = 0;
        if (smokeTest) { world.Phase = Phase.Playing; world.Player = World.At(new(24, 37)); }
        while (!WindowShouldClose() && !quit)
        {
            float dt = Math.Min(GetFrameTime(), .05f);
            if (!smokeTest) Update(dt);
            UpdateCamera();
            BeginDrawing();
            ClearBackground(world.Cursed ? new Color(30, 2, 4, 255) : new Color(6, 11, 14, 255));
            RenderWorld();
            DrawFamilyDialogue();
            DrawAtmosphere();
            if (world.Phase != Phase.Playing)
                DrawRectangle(0, 0, GetScreenWidth(), GetScreenHeight(),
                    new(3, 8, 11, world.Phase == Phase.Title ? 170 : 225));
            if (world.RevengeEnding)
            {
                int width = GetScreenWidth(), height = GetScreenHeight();
                DrawRectangleGradientV(0, 0, width, height, new(65, 2, 8, 245), new(17, 1, 4, 245));
                DrawRectangleGradientH(0, 0, width / 3, height, new(140, 5, 13, 120), new(85, 0, 0, 0));
                DrawRectangleGradientH(width * 2 / 3, 0, width / 3, height, new(85, 0, 0, 0), new(140, 5, 13, 120));
            }
            float uiScale = Math.Min(GetScreenWidth() / (float)Width, GetScreenHeight() / (float)Height);
            BeginMode2D(new Camera2D
            {
                Offset = new(GetScreenWidth() / 2f, GetScreenHeight() / 2f),
                Target = new(Width / 2f, Height / 2f),
                Zoom = uiScale
            });
            DrawHud();
            if (mapVisible && world.Phase == Phase.Playing) DrawMap();
            if (world.Phase != Phase.Playing) DrawMenu();
            EndMode2D();
            EndDrawing();
            if (smokeTest)
            {
                frames++;
                if (frames % 3 == 0 && frames <= 51)
                {
                    string? folder = Environment.GetEnvironmentVariable("HORROR_SCREENSHOT_DIR");
                    if (!string.IsNullOrWhiteSpace(folder))
                    {
                        Directory.CreateDirectory(folder);
                        var capture = LoadImageFromScreen();
                        ExportImage(capture, Path.Combine(folder, $"scene-{frames / 3}.png"));
                        UnloadImage(capture);
                    }
                    if (frames == 3) { world.Player = World.At(new(24, 28)); world.Yaw = .55f; }
                    if (frames == 6) { world.Player = World.At(new(6, 10)); world.Yaw = 0; }
                    if (frames == 9) { world.Ghost = world.Player + new Vector3(0, 0, -4); world.HasKey = true; world.Threat = .8f; }
                    if (frames == 12) world.Phase = Phase.Title;
                    if (frames == 15) world.Phase = Phase.Dead;
                    if (frames == 18)
                    {
                        world = new World { Phase = Phase.Playing, HasKey = true, CaveOpen = true,
                            Flashlight = false, MessageTime = 0,
                            Player = World.At(new(19, 45)), Yaw = MathF.PI };
                    }
                    if (frames == 21) world.HasRelic = true;
                    if (frames == 24)
                    {
                        world.Player = World.At(new(35, 2)) - Vector3.UnitZ * .3f; world.Yaw = 0; world.Pitch = -.5f;
                        world.Flashlight = true;
                    }
                    if (frames == 27)
                    {
                        world.Player = World.At(World.Shotgun); world.Interact();
                        world.Player = World.At(new(19, 36)); world.Pitch = 0;
                        world.Ghost = world.Player - Vector3.UnitZ * 8; world.Grace = 100;
                        world.Fire(); world.Update(.1f, Vector2.Zero, false);
                    }
                    if (frames == 30) world.Update(.3f, Vector2.Zero, false);
                    if (frames == 33) { world.Player = new(57.5f, 0, 78.75f); world.MessageTime = 0; }
                    if (frames == 36) { SmokeShootFamily(0); world.Player = new(57.5f, 0, 78.75f); world.Pitch = 0; }
                    if (frames == 39)
                    {
                        for (int i = 1; i < 4; i++) SmokeShootFamily(i);
                        world.Player = new(57.5f, 0, 78.75f); world.Pitch = 0;
                    }
                    if (frames == 42) { world.Player = World.At(new(19, 45)); world.Yaw = MathF.PI; }
                    if (frames == 45)
                    {
                        world = new World { Phase = Phase.Playing, HasKey = true, HasRelic = true,
                            CaveOpen = true, HasShotgun = true, GhostDefeated = true,
                            Player = World.At(World.Exit), Yaw = MathF.PI };
                        world.Interact();
                    }
                    if (frames == 48)
                    {
                        world.Phase = Phase.Playing;
                        SmokeShootFamily(0);
                        world.Player = World.At(World.Exit); world.Yaw = MathF.PI; world.Pitch = 0;
                        world.Interact();
                    }
                    if (frames == 51) break;
                }
            }
        }
    }
    private void SmokeShootFamily(int index)
    {
        FamilyMember person = world.Family[index];
        world.Player = person.Position + Vector3.UnitZ * 6;
        world.Yaw = 0;
        world.Pitch = MathF.Atan2(person.Height * .65f - 1.65f, 6);
        world.Update(.6f, Vector2.Zero, false);
        if (!world.Fire()) throw new InvalidOperationException("Smoke test could not fire.");
        world.Update(.6f, Vector2.Zero, false);
        if (person.Alive) throw new InvalidOperationException("Smoke test projectile missed family member.");
    }
    private void Start()
    {
        world = new World { Phase = Phase.Playing }; mapVisible = false;
        DisableCursor();
    }
    private void Update(float dt)
    {
        if (world.Cursed && world.Phase == Phase.Playing && IsKeyPressed(KeyboardKey.R)) { Start(); return; }
        if (IsKeyPressed(KeyboardKey.Enter) &&
            (IsKeyDown(KeyboardKey.LeftAlt) || IsKeyDown(KeyboardKey.RightAlt)))
        {
            ToggleBorderlessFullscreen();
            return;
        }
        if (IsKeyPressed(KeyboardKey.M)) sound.Muted = !sound.Muted;
        if (world.Phase == Phase.Title)
        {
#if DEBUG
            if (IsKeyPressed(KeyboardKey.F10))
            {
                Start();
                world.HasKey = true;
                world.HasRelic = true;
                world.HasShotgun = true;
                world.CaveOpen = true;
                world.GhostDefeated = true;
                world.Say("テストプレイ：全アイテム取得・幽霊撃破済み。館に家族がいます。");
                return;
            }
#endif
            if (IsKeyPressed(KeyboardKey.Enter)) Start();
            if (IsKeyPressed(KeyboardKey.Escape)) quit = true;
            return;
        }
        if (world.Phase is Phase.Dead or Phase.Escaped)
        {
            if (IsKeyPressed(KeyboardKey.R) || IsKeyPressed(KeyboardKey.Enter)) Start();
            if (IsKeyPressed(KeyboardKey.Escape)) { world = new World(); EnableCursor(); }
            return;
        }
        if (IsKeyPressed(KeyboardKey.Escape))
        {
            world.Phase = world.Phase == Phase.Paused ? Phase.Playing : Phase.Paused;
            if (world.Phase == Phase.Playing) DisableCursor(); else EnableCursor();
        }
        if (!IsWindowFocused() && world.Phase == Phase.Playing) { world.Phase = Phase.Paused; EnableCursor(); }
        if (world.Phase == Phase.Paused)
        {
            if (IsKeyPressed(KeyboardKey.Q)) quit = true;
            return;
        }
        Vector2 mouse = GetMouseDelta();
        world.Yaw += mouse.X * .0025f;
        world.Pitch = Math.Clamp(world.Pitch - mouse.Y * .0025f, -1.35f, 1.35f);
        if (IsKeyPressed(KeyboardKey.F) && world.Battery > 3) world.Flashlight = !world.Flashlight;
        if (IsKeyPressed(KeyboardKey.Tab)) mapVisible = !mapVisible;
        Vector2 movement = new((IsKeyDown(KeyboardKey.D) ? 1 : 0) - (IsKeyDown(KeyboardKey.A) ? 1 : 0),
            (IsKeyDown(KeyboardKey.W) ? 1 : 0) - (IsKeyDown(KeyboardKey.S) ? 1 : 0));
        Vector3 oldPosition = world.Player;
        if (IsKeyPressed(KeyboardKey.Space) && world.Fire()) sound.Shot();
        world.Update(dt, movement, IsKeyDown(KeyboardKey.LeftShift));
        if (world.Phase == Phase.Playing && IsKeyPressed(KeyboardKey.E))
        {
            string old = world.Message;
            world.Interact();
            if (old != world.Message || world.Phase == Phase.Escaped) sound.Chime();
        }
        footstep -= dt;
        if (Vector3.Distance(oldPosition, world.Player) > .01f && footstep <= 0)
        { sound.Step(); footstep = world.Running ? .3f : .52f; }
        sound.Update(dt, world.Threat);
        if (world.Phase is Phase.Dead or Phase.Escaped)
        {
            EnableCursor();
            if (world.Phase == Phase.Dead) sound.Scare();
        }
    }
    private void ToggleBorderlessFullscreen()
    {
        if (!borderlessFullscreen)
        {
            int monitor = GetCurrentMonitor();
            windowedPosition = GetWindowPosition();
            windowedWidth = GetScreenWidth();
            windowedHeight = GetScreenHeight();
            Vector2 monitorPosition = GetMonitorPosition(monitor);
            SetWindowState(ConfigFlags.UndecoratedWindow);
            SetWindowPosition((int)monitorPosition.X, (int)monitorPosition.Y);
            SetWindowSize(GetMonitorWidth(monitor), GetMonitorHeight(monitor));
            borderlessFullscreen = true;
        }
        else
        {
            ClearWindowState(ConfigFlags.UndecoratedWindow);
            SetWindowSize(windowedWidth, windowedHeight);
            SetWindowPosition((int)windowedPosition.X, (int)windowedPosition.Y);
            borderlessFullscreen = false;
        }
    }
    private void UpdateCamera()
    {
        float bob = world.Phase == Phase.Playing && world.Running ? MathF.Sin(world.Time * 15) * .045f : 0;
        camera.Position = world.Player + new Vector3(0, 1.65f + bob, 0);
        camera.Target = camera.Position + world.Forward;
        SetShaderValue(shader, eyeLocation, camera.Position, ShaderUniformDataType.Vec3);
        SetShaderValue(shader, beamLocation, world.Forward, ShaderUniformDataType.Vec3);
        SetShaderValue(shader, lampLocation, world.Flashlight ? 1f : 0f, ShaderUniformDataType.Float);
        float glow = world.HasRelic && !world.Cursed ? .8f + .12f * MathF.Sin(world.Time * 1.6f) : 0f;
        SetShaderValue(shader, exitGlowLocation, new Vector4(World.At(World.Exit), glow), ShaderUniformDataType.Vec4);
        SetShaderValue(shader, curseLocation, world.Cursed ? 1f : 0f, ShaderUniformDataType.Float);
    }

    private void RenderWorld()
    {
        BeginMode3D(camera);
        DrawSphere(new Vector3(15, 42, 6), 2.5f, world.Cursed ? new Color(180, 12, 20, 255) : new Color(157, 179, 183, 255));
        BeginShaderMode(shader);
        for (int z = 0; z < World.Depth; z++)
        for (int x = 0; x < World.Width; x++)
        {
            Vector3 p = World.At(new(x, z));
            if (Vector3.DistanceSquared(p, world.Player) > 60 * 60) continue;
            char tile = world.Map[x, z];
            bool manor = x >= 18 && x <= 31 && z >= 20 && z <= 32;
            bool cave = x >= 2 && x <= 15 && z <= 18;
            int variation = (x * 19 + z * 31) % 16;
            Color ground = manor ? new(65 + variation, 55 + variation, 47 + variation, 255) : cave ?
                new(53 + variation, 59 + variation, 62 + variation, 255) : new(37 + variation, 49 + variation, 40 + variation, 255);
            DrawCube(p + new Vector3(0, -.12f, 0), World.Tile, .24f, World.Tile, ground);
            if (manor && tile != 'W')
            {
                DrawCube(p + new Vector3(0, 3.6f, 0), World.Tile, .15f, World.Tile, new(43, 42, 42, 255));
                for (int plank = 0; plank < 4; plank++) DrawCube(p + new Vector3(-.94f + plank * .625f, .012f, 0), .02f, .01f, 2.5f, new(25, 24, 23, 255));
            }
            switch (tile)
            {
                case '#': DrawCube(p + new Vector3(0, 3, 0), 2.5f, 6, 2.5f, new(29, 36, 40, 255)); break;
                case 'T':
                    DrawCylinder(p, .20f, .4f, 5.2f + variation * .08f, 7, new(66, 56, 49, 255));
                    DrawCylinder(p + new Vector3(0, 3, 0), 0, 2.1f, 5.2f, 7, new(28, 52, 45, 255));
                    DrawCylinder(p + new Vector3(0, 5, 0), 0, 1.5f, 4, 7, new(34, 62, 53, 255));
                    break;
                case 'R':
                    DrawCube(p + new Vector3(0, 2.2f, 0), 2.5f, 4.4f, 2.5f, new(63 + variation, 70 + variation, 76 + variation, 255));
                    DrawSphere(p + new Vector3(0, 4, 0), 1.7f, new(60, 69, 76, 255));
                    break;
                case 'W':
                    DrawCube(p + new Vector3(0, 1.8f, 0), 2.5f, 3.6f, 2.5f, new(99 + variation, 92 + variation, 79 + variation, 255));
                    DrawCube(p + new Vector3(0, .2f, 0), 2.54f, .4f, 2.54f, new(48, 39, 36, 255));
                    DrawCube(p + new Vector3(0, 3.4f, 0), 2.54f, .25f, 2.54f, new(49, 40, 37, 255));
                    if (z == 32 && x % 3 == 0)
                    {
                        DrawCube(p + new Vector3(0, 2, 1.27f), 1.15f, 1.6f, .06f, new(14, 25, 28, 255));
                        DrawCube(p + new Vector3(0, 2, 1.32f), .09f, 1.7f, .06f, amber);
                        DrawCube(p + new Vector3(0, 2, 1.33f), 1.2f, .08f, .06f, amber);
                    }
                    break;
                case 'F':
                    DrawCube(p + new Vector3(0, .55f, 0), 2.3f, 1.1f, 1.8f, new(77, 47, 32, 255));
                    DrawCube(p + new Vector3(0, 1.13f, 0), 2.4f, .14f, 1.9f, new(110, 76, 49, 255));
                    break;
                case 'D':
                    if (!world.CaveOpen)
                        for (int bar = 0; bar < 7; bar++) DrawCube(p + new Vector3(-1.1f + bar * .36f, 1.7f, 0), .08f, 3.4f, .12f, new(118, 130, 127, 255));
                    break;
            }
            if (tile == 'C' || tile == 'D')
            {
                DrawCube(p + new Vector3(0, 3.9f, 0), 2.5f, .5f, 2.5f, new(47, 56, 63, 255));
                if ((x + z) % 3 == 0) DrawCylinder(p + new Vector3(.7f, 2.8f, -.3f), .45f, 0, 1, 6, new(71, 83, 85, 255));
            }
        }
        // 館の上階風ファサードと屋根。
        DrawCube(new(62.5f, 4.6f, 65), 35, 2, 32.5f, new(64, 62, 60, 255));
        DrawCube(new(62.5f, 5.8f, 65), 36, .5f, 34, new(28, 35, 40, 255));
        for (int i = 0; i < 6; i++) DrawCube(new(50 + i * 5, 4.5f, 81.3f), 1.4f, 1.3f, .12f, new(18, 30, 32, 255));
        // 玄関のランナー。洞窟と館への小道を石で示す。
        DrawCube(new(62.5f, .03f, 70), 2.8f, .03f, 20, new(83, 29, 27, 255));
        for (int z = 34; z <= 47; z += 2) DrawCube(World.At(new(19, z)) + new Vector3(0, .02f, 0), 1.1f, .04f, .8f, new(93, 101, 97, 255));
        for (int x = 9; x <= 25; x += 2) DrawCube(World.At(new(x, 36)) + new Vector3(0, .02f, 0), .9f, .04f, 1.2f, new(88, 97, 94, 255));
        DrawAltar(World.Exit, new(92, 108, 109, 255));
        DrawAltar(World.Relic, new(65, 83, 85, 255));
        if (world.ShotgunAvailable)
            DrawShotgun(World.At(World.Shotgun) + new Vector3(0, .22f, 0), Vector3.UnitX, 1);
        if (!world.HasKey)
        {
            Vector3 k = World.At(World.Key) + new Vector3(0, .85f, 0);
            DrawCube(k - new Vector3(0, .45f, 0), 1.2f, .8f, 1.2f, new(82, 51, 33, 255));
            DrawCylinder(k, .08f, .08f, .55f, 8, amber);
            DrawSphere(k + new Vector3(0, .55f, 0), .17f, amber);
        }
        EndShaderMode();
        if (world.GhostDefeated)
            for (int i = 0; i < world.Family.Length; i++)
                if (world.Family[i].Alive) DrawFamilyMember(world.Family[i], i);
        if (world.Cursed) BeginShaderMode(shader);
        if (!world.HasRelic) DrawSphere(World.At(World.Relic) + new Vector3(0, 1.3f + MathF.Sin(world.Time * 2) * .07f, 0), .24f, new(90, 205, 199, 255));
        if (!world.HasKey) DrawSphere(World.At(World.Key) + new Vector3(0, 1.47f, 0), .085f, amber);
        DrawLantern(World.At(new(24, 33)));
        DrawLantern(World.At(new(9, 19)));
        if (world.GhostActive) DrawGhost();
        if (world.ShotgunAvailable && world.Near(World.Shotgun, 4))
            DrawSphere(World.At(World.Shotgun) + new Vector3(0, .27f, 0), .035f, new(116, 155, 150, 255));
        foreach (MagicShot shot in world.Shots)
        {
            DrawSphere(shot.Position, .10f, new(188, 245, 227, 255));
            DrawCylinderEx(shot.Position - shot.Direction * .7f, shot.Position, .025f, .075f, 8, new(99, 192, 171, 255));
        }
        if (world.HasShotgun && world.Phase == Phase.Playing)
        {
            Vector3 right = Vector3.Normalize(Vector3.Cross(world.Forward, Vector3.UnitY));
            Vector3 up = Vector3.Cross(right, world.Forward);
            DrawShotgun(camera.Position + world.Forward * .65f + right * .24f - up * .23f, world.Forward, .45f);
        }
        if (world.Cursed) EndShaderMode();
        EndMode3D();
    }
    private void DrawFamilyMember(FamilyMember person, int index)
    {
        Vector3 p = person.Position;
        float h = person.Height;
        Color skin = new(192, 157, 125, 255);
        Color clothes = index switch { 0 => new(112, 99, 132, 255), 1 => new(77, 112, 143, 255),
            2 => new(145, 104, 99, 255), _ => new(105, 120, 95, 255) };
        DrawCylinder(p + Vector3.UnitY * (h * .35f), .23f, .26f, h * .4f, 10, clothes);
        DrawSphere(p + Vector3.UnitY * (h - .21f), .22f, new(58, 44, 36, 255));
        DrawSphere(p + new Vector3(0, h - .23f, .065f), .19f, skin);
        foreach (int sign in new[] { -1, 1 })
        {
            DrawCylinderEx(p + new Vector3(sign * .12f, .06f, 0), p + new Vector3(sign * .12f, h * .4f, 0), .085f, .1f, 8, new(58, 62, 70, 255));
            Vector3 hand = p + new Vector3(sign * .32f, h * (world.FamilyFrightened ? .85f : .38f), .08f);
            DrawCylinderEx(p + new Vector3(sign * .23f, h * .69f, 0), hand, .08f, .055f, 8, clothes);
            DrawSphere(hand, .065f, skin);
            DrawSphere(p + new Vector3(sign * .065f, h - .2f, .237f), .022f, new(35, 30, 27, 255));
        }
    }
    private void DrawFamilyDialogue()
    {
        if (!world.GhostDefeated || world.Cursed || world.Phase != Phase.Playing) return;
        var visible = world.Family.Where(person => person.Alive &&
            Vector3.Distance(camera.Position, person.Position) < 18 &&
            Vector3.Dot(Vector3.Normalize(person.Position + Vector3.UnitY - camera.Position), world.Forward) > .5f &&
            world.Sight(world.Player, person.Position)).ToArray();
        if (visible.Length == 0) return;
        float scale = Math.Min(GetScreenWidth() / (float)Width, GetScreenHeight() / (float)Height);
        foreach (FamilyMember person in visible)
        {
            Vector2 label = GetWorldToScreen(person.Position + Vector3.UnitY * (person.Height + .2f), camera);
            float width = MeasureTextEx(font, person.Name, 17 * scale, 1).X;
            DrawTextEx(font, person.Name, label - new Vector2(width / 2, 0), 17 * scale, 1, paper);
        }
        Vector3 anchor = visible.Aggregate(Vector3.Zero, (sum, person) => sum + person.Position) / visible.Length;
        Vector2 tip = GetWorldToScreen(anchor + Vector3.UnitY * 2.3f, camera);
        float boxWidth = 390 * scale, boxHeight = 72 * scale;
        float x = Math.Clamp(tip.X - boxWidth / 2, 8, GetScreenWidth() - boxWidth - 8);
        float y = Math.Clamp(tip.Y - boxHeight - 20 * scale, 180 * scale, GetScreenHeight() - boxHeight - 8);
        Color bubble = world.FamilyFrightened ? new(242, 218, 207, 245) : new(225, 234, 221, 245);
        DrawRectangleRounded(new(x, y, boxWidth, boxHeight), .2f, 8, bubble);
        DrawTriangle(new(x + boxWidth / 2 - 8 * scale, y + boxHeight - 1), tip,
            new(x + boxWidth / 2 + 8 * scale, y + boxHeight - 1), bubble);
        string[] lines = world.FamilyDialogue.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            float width = MeasureTextEx(font, lines[i], 20 * scale, 1).X;
            DrawTextEx(font, lines[i], new(x + (boxWidth - width) / 2, y + (12 + i * 26) * scale), 20 * scale, 1, dark);
        }
    }
    private static void DrawShotgun(Vector3 p, Vector3 forward, float scale)
    {
        Vector3 side = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        DrawCylinderEx(p - forward * (.5f * scale), p, .10f * scale, .075f * scale, 6, new(78, 59, 45, 255));
        foreach (int sign in new[] { -1, 1 })
        {
            Vector3 barrel = p + side * (sign * .055f * scale);
            DrawCylinderEx(barrel, barrel + forward * (.75f * scale), .045f * scale, .045f * scale, 8, new(100, 111, 113, 255));
        }
        DrawCylinderEx(p - Vector3.UnitY * (.04f * scale), p - Vector3.UnitY * (.22f * scale),
            .055f * scale, .07f * scale, 6, new(67, 51, 40, 255));
    }
    private void DrawAltar(Cell cell, Color color)
    {
        Vector3 p = World.At(cell);
        DrawCube(p + new Vector3(0, .4f, 0), 1.4f, .8f, 1.4f, color);
        DrawCube(p + new Vector3(-1.05f, 1.7f, 0), .35f, 3.4f, .55f, color);
        DrawCube(p + new Vector3(1.05f, 1.7f, 0), .35f, 3.4f, .55f, color);
        DrawCube(p + new Vector3(0, 3.3f, 0), 2.45f, .45f, .55f, color);
    }
    private void DrawLantern(Vector3 p)
    {
        DrawCube(p + new Vector3(0, 1, 0), .09f, 2, .09f, new(67, 69, 65, 255));
        DrawCube(p + new Vector3(0, 2, 0), .24f, .38f, .24f, new(237, 163, 71, 255));
    }
    private void DrawGhost()
    {
        Vector3 p = world.Ghost + new Vector3(0, .25f + MathF.Sin(world.Time * 3) * .13f, 0);
        DrawCylinder(p, .24f, .50f, 1.65f, 14, new(90, 110, 114, 235));
        DrawSphere(p + new Vector3(0, 1.85f, 0), .28f, new(174, 183, 170, 255));
        Vector3 facing = world.Player - world.Ghost;
        if (facing.LengthSquared() < .001f) facing = -Vector3.UnitZ;
        facing = Vector3.Normalize(facing);
        Vector3 side = Vector3.Cross(facing, Vector3.UnitY);
        Vector3 face = p + new Vector3(0, 1.88f, 0) + facing * .24f;
        DrawSphere(face + side * .105f, .055f, new(15, 9, 13, 255));
        DrawSphere(face - side * .105f, .055f, new(15, 9, 13, 255));
        DrawSphere(face + side * .105f + facing * .045f, .016f, new(249, 77, 66, 255));
        DrawSphere(face - side * .105f + facing * .045f, .016f, new(249, 77, 66, 255));
        DrawCylinderEx(face + new Vector3(0, -.08f, 0), face + new Vector3(0, -.30f, 0), .05f, .035f, 8, new(19, 9, 16, 255));
        // 顔の両脇を覆う濡れた髪、長い腕と指、裂けた裾。
        for (int i = 0; i < 10; i++)
        {
            float angle = i / 9f * MathF.PI;
            Vector3 hair = side * (MathF.Cos(angle) * .28f) - facing * (MathF.Sin(angle) * .22f);
            DrawCylinderEx(p + hair + new Vector3(0, 2.01f, 0), p + hair * 1.3f + new Vector3(0, .9f + (i % 3) * .17f, 0), .073f, .028f, 6, new(19, 25, 30, 255));
            float a = i * MathF.Tau / 10;
            Vector3 hem = new(MathF.Cos(a) * .4f, .15f, MathF.Sin(a) * .4f);
            DrawCylinderEx(p + hem, p + hem * 1.3f - new Vector3(0, .25f + (i % 3) * .08f, 0), .12f, 0, 5, new(77, 96, 100, 220));
        }
        foreach (int sign in new[] { -1, 1 })
        {
            Vector3 hand = p + side * (.7f * sign) + new Vector3(0, .37f, 0) + facing * .25f;
            DrawCylinderEx(p + new Vector3(0, 1.45f, 0) + side * (.25f * sign), hand, .11f, .04f, 8, new(133, 151, 147, 255));
            for (int finger = 0; finger < 4; finger++)
                DrawCylinderEx(hand + side * (finger * .025f), hand + side * (finger * .045f) + new Vector3(0, -.23f, 0) + facing * .09f, .015f, .005f, 5, new(151, 158, 142, 255));
        }
    }

    private void DrawAtmosphere()
    {
        int width = GetScreenWidth(), height = GetScreenHeight();
        DrawRectangleGradientV(0, 0, width, 170, new(0, 0, 0, 150), new(0, 0, 0, 0));
        DrawRectangleGradientV(0, height - 200, width, 200, new(0, 0, 0, 0), new(0, 0, 0, 200));
        DrawRectangleGradientH(0, 0, 180, height, new(0, 0, 0, 150), new(0, 0, 0, 0));
        DrawRectangleGradientH(width - 180, 0, 180, height, new(0, 0, 0, 0), new(0, 0, 0, 150));
        if (world.Threat > .1f && world.Phase == Phase.Playing)
            DrawRectangleLinesEx(new(3, 3, width - 6, height - 6), 6 + world.Threat * 8,
                new Color(153, 34, 39, (int)(world.Threat * (90 + MathF.Sin(world.Time * 8) * 45))));
    }
    private void Text(string text, float x, float y, float size, Color color) => DrawTextEx(font, text, new(x, y), size, 1, color);
    private void Center(string text, float y, float size, Color color) => Text(text, (Width - MeasureTextEx(font, text, size, 1).X) / 2, y, size, color);
    private void DrawHud()
    {
        if (world.Phase == Phase.Title) return;
        Text("H O L L O W   P I N E S", 38, 25, 18, amber);
        Text(world.Area, 38, 54, 29, paper);
        Text(world.Objective, 38, 98, 20, paper);
        Text($"{(int)world.Time / 60:00}:{(int)world.Time % 60:00}", 1145, 34, 24, muted);
        if (world.Phase == Phase.Playing)
        {
            DrawCircle(Width / 2, Height / 2, 2, paper);
            DrawCircleLines(Width / 2, Height / 2, 7, new(190, 202, 195, 75));
            if (world.Prompt.Length > 0)
            {
                DrawRectangle(260, 520, 760, 48, new(5, 10, 13, 210));
                Center(world.Prompt, 531, 21, amber);
            }
            if (world.MessageTime > 0) Center(world.Message, 615, 20, paper);
            if (world.Threat > .25f) Center("近くに、何かがいる。光を向けて足止めしろ。", 155, 20, new(215, 138, 132, 255));
        }
        Meter("STAMINA", 38, 681, world.Stamina, muted);
        Meter(world.Flashlight ? "LIGHT / ON" : "LIGHT / CHARGING", 38, 725, world.Battery, amber);
        Text($"鍵  {(world.HasKey ? "取得済" : "未取得")}     封印の石  {(world.HasRelic ? "取得済" : "未取得")}", 430, 710, 20, paper);
        if (world.HasShotgun) Text("魔法のショットガン  [SPACE] 発射", 430, 746, 18, muted);
        Text("WASD 移動   SHIFT 走る   E 調べる   F ライト", 785, 695, 16, muted);
        Text($"TAB 地図   ESC 一時停止   M 音 {(sound.Muted ? "OFF" : "ON")}", 785, 726, 16, muted);
    }
    private void Meter(string label, int x, int y, float value, Color color)
    {
        Text(label, x, y, 13, color);
        DrawRectangle(x, y + 22, 260, 4, new(43, 54, 56, 255));
        DrawRectangle(x, y + 22, (int)(value * 2.6f), 4, color);
    }
    private void DrawMap()
    {
        const int scale = 7, left = 972, top = 195;
        DrawRectangle(left - 18, top - 38, 295, 441, new(5, 12, 15, 240));
        Text("森の手描き地図 / 北 ↑", left, top - 29, 17, paper);
        for (int z = 0; z < World.Depth; z++) for (int x = 0; x < World.Width; x++)
        {
            Color c = world.Solid(x, z) ? new(57, 71, 70, 255) : new(18, 31, 32, 255);
            if (world.Map[x, z] == 'I') c = new(81, 69, 50, 255);
            if (world.Map[x, z] == 'C') c = new(39, 75, 81, 255);
            DrawRectangle(left + x * scale, top + z * scale, scale - 1, scale - 1, c);
        }
        Cell goal = !world.HasKey ? World.Key : !world.CaveOpen ? World.CaveDoor : !world.HasRelic ? World.Relic : World.Exit;
        if (!world.Cursed) DrawCircle(left + goal.X * scale + 3, top + goal.Z * scale + 3, 4, amber);
        Cell player = World.ToCell(world.Player);
        Vector2 p = new(left + player.X * scale + 3, top + player.Z * scale + 3);
        DrawCircleV(p, 4, paper);
        DrawLineEx(p, p + new Vector2(MathF.Sin(world.Yaw), -MathF.Cos(world.Yaw)) * 12, 2, paper);
        Text("白：現在地   金：次の目的地", left, top + 382, 15, muted);
    }
    private void DrawMenu()
    {
        if (world.Phase == Phase.Title)
        {
            Text("A  F I R S T - P E R S O N  H O R R O R", 95, 130, 18, amber);
            Text("HOLLOW", 88, 190, 86, paper);
            Text("PINES", 88, 281, 86, paper);
            DrawRectangle(95, 397, 56, 2, amber);
            Text("夜霧の館", 95, 430, 30, paper);
            Text("森で目を覚ました。帰り道は、封じられていた。", 95, 485, 21, muted);
            Text("館の鍵。洞窟の石。そして、振り返ってはいけない影。", 95, 521, 21, muted);
            Text("[ ENTER ]  森へ入る", 95, 605, 26, amber);
            Text("ALT + ENTER 全画面 / ウィンドウ切り替え", 95, 646, 17, muted);
            Text("WASD 移動 / マウス 視点 / SHIFT ダッシュ / E 調べる", 95, 680, 17, muted);
            Text("F 懐中電灯 / TAB 地図 / M ミュート / ESC 終了", 95, 710, 17, muted);
            Text("01 / 館の鍵     02 / 洞窟の石     03 / 南の出口", 755, 730, 16, amber);
        }
        else if (world.Phase == Phase.Paused)
        {
            Center("PAUSED", 260, 58, paper);
            Center("[ ESC ] 再開     [ Q ] 終了", 380, 25, amber);
            Center("懐中電灯を幽霊に向けると動きを遅くできる。", 463, 22, muted);
            Center("電池は消灯中に回復。地図を開いても時間は進む。", 500, 22, muted);
            Center("ALT + ENTER 全画面 / ウィンドウ切り替え", 550, 18, muted);
        }
        else if (world.Phase == Phase.Dead)
        {
            DrawEllipse(640, 248, 65, 92, new(160, 172, 170, 255));
            DrawEllipse(616, 230, 14, 19, dark); DrawEllipse(664, 230, 14, 19, dark);
            DrawEllipse(640, 288, 14, 29, dark);
            Center("影に、追いつかれた。", 380, 42, paper);
            Center("光を向けて足止めし、ダッシュで距離を取ろう。", 454, 21, muted);
            Center("[ R / ENTER ] 再挑戦     [ ESC ] タイトル", 540, 24, amber);
        }
        else
        {
            Center(world.RevengeEnding ? "BAD END" : "GOOD END", 200, 24,
                world.RevengeEnding ? new Color(240, 158, 156, 255) : amber);
            Center(world.RevengeEnding ? "血塗られた朝" : "夜明け", 245, 68, paper);
            Center("封印は解けた。森の向こうに、朝の光が見える。", 369, 24, muted);
            Center($"脱出時間  {(int)world.Time / 60:00}:{(int)world.Time % 60:00}", 440, 24, amber);
            if (world.RevengeEnding)
            {
                DrawRectangle(550, 485, 180, 1, new(159, 48, 54, 255));
                Color warning = new(240, 158, 156, 255);
                Center("……しかし後日、生き残った家族があなたを探し出した。", 512, 23, warning);
                Center("逃れたはずの恐怖は、復讐という形であなたの命を奪った。", 550, 23, warning);
            }
            Center("[ R / ENTER ] もう一度     [ ESC ] タイトル", world.RevengeEnding ? 620 : 550, 23, paper);
        }
    }
    public void Dispose()
    {
        EnableCursor(); sound.Dispose(); UnloadShader(shader); UnloadFont(font); CloseWindow();
    }
}
