using Avalonia.Platform;
using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Components;
using SoundFlow.Interfaces;
using SoundFlow.Providers;
using SoundFlow.Structs;

namespace AJCC.Desktop.Services;

public static class AudioFeedbackService
{
    private static readonly object Sync = new();
    private static DateTime _lastButtonTickUtc = DateTime.MinValue;
    private static AudioEngine? _engine;
    private static AudioPlaybackDevice? _playbackDevice;
    private static ISoundDataProvider? _buttonTickProvider;
    private static SoundPlayer? _buttonTickPlayer;
    private static MemoryStream? _buttonTickStream;

    public static bool Enabled { get; set; } = true;

    public static void PlayButtonTick()
    {
        if (!Enabled)
            return;

        DateTime now = DateTime.UtcNow;
        lock (Sync)
        {
            if (now - _lastButtonTickUtc < TimeSpan.FromMilliseconds(60))
                return;

            _lastButtonTickUtc = now;
        }

        _ = Task.Run(PlayButtonTickCore);
    }

    private static void PlayButtonTickCore()
    {
        try
        {
            lock (Sync)
            {
                if (!Enabled)
                    return;

                EnsureButtonTickPlayer();
                _buttonTickPlayer?.Stop();
                _buttonTickPlayer?.Play();
            }
        }
        catch
        {
            // Audiofeedback darf niemals UI, Login, Core-Kommunikation oder Downloads blockieren.
        }
    }

    private static void EnsureButtonTickPlayer()
    {
        if (_engine is not null
            && _playbackDevice is not null
            && _buttonTickProvider is not null
            && _buttonTickPlayer is not null
            && _buttonTickStream is not null)
        {
            return;
        }

        using Stream resource = AssetLoader.Open(
            new Uri("avares://AJCC.Desktop/Assets/Sounds/button_tick.wav"));

        MemoryStream stream = new();
        resource.CopyTo(stream);
        stream.Position = 0;

        AudioEngine engine = new MiniAudioEngine();
        AudioFormat format = AudioFormat.Cd;
        ISoundDataProvider provider = new StreamDataProvider(engine, format, stream);
        AudioPlaybackDevice playbackDevice = engine.InitializePlaybackDevice(null, format);
        SoundPlayer player = new(engine, format, provider);

        playbackDevice.MasterMixer.AddComponent(player);
        playbackDevice.Start();

        _buttonTickStream = stream;
        _engine = engine;
        _buttonTickProvider = provider;
        _playbackDevice = playbackDevice;
        _buttonTickPlayer = player;
    }
}
