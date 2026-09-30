using Raylib_cs;
using static Raylib_cs.Raylib;

namespace HorrorGame;

/// <summary>外部音源を必要としない合成効果音。音声デバイスなしでもプレイ可能。</summary>
public sealed class Soundscape : IDisposable
{
    private readonly bool ready;
    private readonly Sound step, pulse, chime, scare, shot;
    private float heartbeat;
    private bool muted;
    public bool Muted { get => muted; set { muted = value; if (ready) SetMasterVolume(value ? 0 : .65f); } }
    public Soundscape()
    {
        InitAudioDevice(); ready = IsAudioDeviceReady();
        if (!ready) return;
        step = Tone(.14f, 65, .24f, .6f);
        pulse = Tone(.25f, 47, .35f, .04f);
        chime = Tone(.7f, 523, .2f, .01f);
        scare = Tone(.9f, 93, .42f, .65f);
        shot = Tone(.32f, 110, .45f, .8f);
        SetMasterVolume(.65f);
    }
    private static Sound Tone(float duration, float frequency, float gain, float noise)
    {
        const int rate = 22050;
        int count = (int)(rate * duration);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, true))
        {
            writer.Write("RIFF"u8); writer.Write(36 + count * 2); writer.Write("WAVEfmt "u8);
            writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate);
            writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
            writer.Write("data"u8); writer.Write(count * 2);
            var random = new Random(17);
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)rate;
                float envelope = Math.Min(1, t * 100) * MathF.Exp(-t / duration * 6) * Math.Min(1, (duration - t) * 100);
                float sample = MathF.Sin(t * frequency * MathF.Tau) * (1 - noise) + ((float)random.NextDouble() * 2 - 1) * noise;
                writer.Write((short)(sample * envelope * gain * short.MaxValue));
            }
        }
        byte[] bytes = stream.ToArray();
        var wave = LoadWaveFromMemory(".wav", bytes);
        var result = LoadSoundFromWave(wave); UnloadWave(wave); return result;
    }
    public void Step() { if (ready && !Muted) PlaySound(step); }
    public void Chime() { if (ready && !Muted) PlaySound(chime); }
    public void Scare() { if (ready && !Muted) PlaySound(scare); }
    public void Shot() { if (ready && !Muted) PlaySound(shot); }
    public void Update(float dt, float threat)
    {
        heartbeat -= dt;
        if (ready && !Muted && threat > .15f && heartbeat <= 0)
        { SetSoundVolume(pulse, threat); PlaySound(pulse); heartbeat = 1.2f - threat * .85f; }
    }
    public void Dispose()
    {
        if (!ready) return;
        UnloadSound(step); UnloadSound(pulse); UnloadSound(chime); UnloadSound(scare); UnloadSound(shot); CloseAudioDevice();
    }
}
