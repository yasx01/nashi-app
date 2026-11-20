using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using LibVLCSharp.Shared;
using Nashi.Core;
using System.Runtime.InteropServices;

namespace Nashi.Linux;

internal sealed class MediaPlayback : IDisposable
{
    private readonly LibVLC vlc;
    private readonly SemaphoreSlim bufferGate = new(1, 1);
    private readonly IntPtr buffer;
    private readonly IntPtr allocation;
    private readonly byte[] pixels = [];
    private readonly int width, height, pitch;
    private Media? media;
    private int pending, disposed;
    internal MediaPlayer Player { get; }
    internal WriteableBitmap? Frame { get; }
    internal long FramesDecoded { get; private set; }
    internal event Action? FrameUpdated;
    internal event Action<string>? Failed;

    static MediaPlayback()
    {
        // Fedora's runtime package supplies the versioned SONAME, not the development symlink.
        NativeLibrary.SetDllImportResolver(typeof(LibVLC).Assembly, (name, assembly, flags) => name switch
        {
            "libvlc" or "libvlc.so" or "libvlc.dll" => NativeLibrary.Load("libvlc.so.5", assembly, flags),
            "libvlccore" or "libvlccore.so" => NativeLibrary.Load("libvlccore.so.9", assembly, flags),
            _ => IntPtr.Zero
        });
        LibVLCSharp.Shared.Core.Initialize();
    }
    internal MediaPlayback(MediaInfo info, bool silent = false)
    {
        // Playlist demuxers may follow URLs even when the chosen filename looks local.
        var containers = new[] { "mov", "mp4", "m4a", "3gp", "3g2", "mj2", "matroska", "webm", "asf", "wav", "flac", "mp3", "ogg", "aac", "aiff", "avi", "gif", "mpeg", "mpegts", "m4v" };
        if (string.IsNullOrWhiteSpace(info.Container) || info.Container.Split(',').Any(name => !containers.Contains(name)))
            throw new InvalidDataException("Önizleme yalnızca doğrulanmış yerel medya kapsayıcılarını açabilir; oynatma listeleri açılmaz.");
        var options = new List<string> { "--no-video-title-show", "--no-osd", "--no-sub-autodetect-file", "--no-metadata-network-access", "--avcodec-hw=none" };
        if (silent) options.Add("--aout=dummy");
        options.Add(info.Video is null ? "--no-video" : "--vout=vmem");
        vlc = new LibVLC(options.ToArray()); Player = new MediaPlayer(vlc);
        try
        {
            if (info.Video is { } video)
            {
                var sourceWidth = (video.Width ?? 1) * video.SampleAspect; var sourceHeight = video.Height ?? 1;
                if (!double.IsFinite(sourceWidth) || sourceWidth <= 0 || sourceHeight <= 0) throw new InvalidDataException("Geçersiz video boyutu.");
                var scale = Math.Min(1, 1280 / Math.Max(sourceWidth, sourceHeight));
                width = Math.Clamp(((int)Math.Ceiling(sourceWidth * scale) + 7) / 8 * 8, 8, 1280);
                height = Math.Clamp(((int)Math.Ceiling(sourceHeight * scale) + 7) / 8 * 8, 8, 1280);
                pitch = checked(width * 4); pixels = new byte[checked(pitch * height)]; allocation = Marshal.AllocHGlobal(pixels.Length + 31);
                buffer = new IntPtr((allocation.ToInt64() + 31) & ~31L);
                Frame = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
                Player.SetVideoFormat("RV32", (uint)width, (uint)height, (uint)pitch);
                Player.SetVideoCallbacks(Lock, Unlock, Display);
            }
            Player.EncounteredError += (_, _) => Dispatcher.UIThread.Post(() => { if (disposed == 0) Failed?.Invoke("Medya oynatılamadı. Dosyayı ve kurulu medya araçlarını kontrol et."); });
        }
        catch { Dispose(); throw; }
    }
    internal void Open(string path, bool startPaused = true)
    {
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path)) throw new ArgumentException("Yerel ve var olan bir medya dosyası seç.");
        Player.Stop(); media?.Dispose(); media = new Media(vlc, path, FromType.FromPath);
        media.AddOption(":demux=avformat");
        if (startPaused) media.AddOption(":start-paused");
        Player.Media = media;
        if (!Player.Play()) throw new IOException("Medya oynatıcısı başlatılamadı.");
    }
    internal void Toggle()
    {
        if (Player.IsPlaying) Player.SetPause(true);
        else if (Player.State == VLCState.Paused) Player.SetPause(false);
        else if (!Player.Play()) throw new IOException("Medya oynatıcısı başlatılamadı.");
    }
    internal async Task OpenReadyAsync(string path, CancellationToken token)
    {
        Open(path);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (Volatile.Read(ref disposed) != 0) throw new OperationCanceledException(token);
                var state = Player.State;
                if (state is VLCState.Paused or VLCState.Playing) return;
                if (state == VLCState.Error) throw new IOException("Medya oynatıcısı dosyayı açamadı.");
                await Task.Delay(20, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && disposed == 0) { throw new TimeoutException("Medya önizlemesi zamanında başlatılamadı."); }
    }
    internal void Seek(double seconds, double duration)
    {
        if (!double.IsFinite(seconds) || !double.IsFinite(duration) || duration <= 0) throw new ArgumentException("Geçerli zaman ve süre seç.");
        Player.Time = (long)(Math.Clamp(seconds, 0, duration) * 1000);
    }
    private IntPtr Lock(IntPtr opaque, IntPtr planes)
    {
        bufferGate.Wait(); Marshal.WriteIntPtr(planes, buffer); return IntPtr.Zero;
    }
    private void Unlock(IntPtr opaque, IntPtr picture, IntPtr planes) => bufferGate.Release();
    private void Display(IntPtr opaque, IntPtr picture)
    {
        FramesDecoded++;
        if (Volatile.Read(ref disposed) != 0 || Interlocked.Exchange(ref pending, 1) != 0) return;
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (disposed != 0 || Frame is null) return;
                bufferGate.Wait();
                try { Marshal.Copy(buffer, pixels, 0, pixels.Length); } finally { bufferGate.Release(); }
                using (var locked = Frame.Lock())
                {
                    if (locked.RowBytes == pitch) Marshal.Copy(pixels, 0, locked.Address, pixels.Length);
                    else for (var row = 0; row < height; row++) Marshal.Copy(pixels, row * pitch, locked.Address + row * locked.RowBytes, pitch);
                }
                FrameUpdated?.Invoke();
            }
            finally { Interlocked.Exchange(ref pending, 0); }
        });
    }
    internal static async Task<byte[]> ArtworkAsync(Toolchain tools, string directory, string path, MediaInfo media, CancellationToken token)
    {
        var output = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".png");
        string[] args = media.Video is not null ? ["-i", path, "-map", "0:v:0", "-frames:v", "1", "-vf", "scale=1280:720:force_original_aspect_ratio=decrease"] :
            ["-i", path, "-filter_complex", "aformat=channel_layouts=mono,showwavespic=s=1200x140:colors=0xe5bc59", "-frames:v", "1"];
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromMinutes(2));
        await ProcessRunner.RunAsync(tools.Executable("ffmpeg"), ["-v", "error", "-nostdin", .. args, output], timeout.Token);
        return await File.ReadAllBytesAsync(output, token);
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Player.Stop(); Player.Dispose(); media?.Dispose(); vlc.Dispose(); Frame?.Dispose();
        if (allocation != IntPtr.Zero) Marshal.FreeHGlobal(allocation);
        bufferGate.Dispose();
    }
}
