using Avalonia.Threading;
using LibVLCSharp.Shared;
using Nashi.Core;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Nashi.Linux;

internal static class MediaPlaybackChecks
{
    internal static async Task<(string File, MediaInfo Info)?> Prepare(string root)
    {
        if (!File.Exists("/usr/lib64/libvlc.so.5") || Environment.GetEnvironmentVariable("NASHI_TEST_FULL_MEDIA") != "1")
        { Console.WriteLine("SKIP: native playback checks need system libVLC and NASHI_TEST_FULL_MEDIA=1."); return null; }
        var tools = new Toolchain(Environment.GetEnvironmentVariable("NASHI_VERIFY_TOOLS_DIRECTORY") ?? Path.Combine(root, "native-playback-tools"), "/usr/bin");
        var ffmpeg = tools.Executable("ffmpeg"); var ffprobe = tools.Executable("ffprobe");
        var file = Path.Combine(root, "Medya önizleme ' test.mp4");
        await ProcessRunner.RunAsync(ffmpeg, ["-v", "error", "-f", "lavfi", "-i", "testsrc2=size=64x48:rate=10:duration=3", "-f", "lavfi", "-i", "sine=frequency=440:duration=3", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", file]);
        await ProcessRunner.RunAsync(ffmpeg, ["-v", "error", "-i", file, "-map", "0:a:0", "-c:a", "pcm_s16le", Path.ChangeExtension(file, ".wav")]);
        return (file, await new MediaEngine(ffmpeg, ffprobe).InspectAsync(file));
    }
    internal static void Run((string File, MediaInfo Info) fixture, string preview)
    {
        using var playback = new MediaPlayback(fixture.Info, silent: true);
        var updated = 0; string? error = null;
        playback.FrameUpdated += () => updated++; playback.Failed += message => error = message;
        void Wait(Func<bool> condition, string label)
        {
            var deadline = Stopwatch.StartNew();
            while (!condition() && error is null && deadline.Elapsed < TimeSpan.FromSeconds(10)) { Thread.Sleep(10); Dispatcher.UIThread.RunJobs(); }
            if (!condition()) throw new Exception("Playback: " + label + (error is null ? "" : " · " + error));
        }
        playback.Open(fixture.File, startPaused: false);
        Wait(() => updated >= 2, "decoded video reaches the Avalonia bitmap");
        using (var frame = playback.Frame!.Lock())
        {
            var pixels = new byte[frame.RowBytes * frame.Size.Height]; Marshal.Copy(frame.Address, pixels, 0, pixels.Length);
            if (pixels.Distinct().Count() < 5) throw new Exception("Playback: decoded picture is empty.");
        }
        playback.Toggle(); Wait(() => playback.Player.State == VLCState.Paused, "pause state");
        playback.Seek(1.5, fixture.Info.Duration); Wait(() => playback.Player.Time is >= 1400 and <= 1700, "seek while paused");
        using (var media = playback.Player.Media) if (media is null || media.Statistics.DecodedAudio < 1) throw new Exception("Playback: audio was not decoded.");
        playback.Frame!.Save(Path.ChangeExtension(preview, ".video-frame.png"));
        playback.Toggle(); Wait(() => playback.Player.IsPlaying, "resume state");
        playback.Player.SetPause(true);
        using var paused = new MediaPlayback(fixture.Info, silent: true); var pausedFrames = 0;
        paused.FrameUpdated += () => pausedFrames++; paused.Failed += message => error = message;
        paused.Open(fixture.File);
        Wait(() => paused.Player.State == VLCState.Paused, "initial preview stays paused");
        paused.Toggle(); Wait(() => pausedFrames > 0, "initial paused media decodes on play");
        try { using var rejected = new MediaPlayback(fixture.Info with { Container = "hls" }, silent: true); throw new Exception("Playback: playlist accepted."); }
        catch (InvalidDataException) { }
        Console.WriteLine("PASS: real libVLC video/audio decode, Avalonia frame delivery, pause, seek, resume and non-ASCII local path; audio uses a silent test sink.");
    }
}
