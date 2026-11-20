using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Nashi.Core;
using ImageMagick;
using System.Globalization;

namespace Nashi.Linux;

internal sealed class MediaEditorWindow : Window
{
    private readonly MainWindow host;
    private readonly Toolchain tools;
    private readonly MediaEngine engine;
    private readonly string action;
    private readonly string[] inputs;
    private readonly MediaInfo info;
    private readonly bool silentPlayback;
    private readonly ImageCanvas canvas;
    private readonly StackPanel settings = new() { Spacing = 8 };
    private readonly Dictionary<string, NumericUpDown> numbers = [];
    private readonly Dictionary<string, ComboBox> choices = [];
    private readonly Dictionary<string, TextBox> texts = [];
    private readonly TextBlock message = MainWindow.Label("Önizleme hazırlanıyor…", 12, true);
    private readonly Image timelineArtwork = new() { Height = 68, Stretch = Stretch.Fill };
    private Bitmap? timelineBitmap;
    private readonly TextBlock clock = MainWindow.Label("0:00", 12);
    private readonly Slider position;
    private readonly ListBox ranges = new() { MaxHeight = 140 };
    private readonly List<MediaRange> audioRanges = [];
    private readonly List<MediaRegion> videoRegions = [];
    private readonly ListBox inputList = new() { MaxHeight = 140 };
    private readonly ListBox previewFiles = new() { MaxHeight = 120 };
    private readonly CheckBox preserve = new() { Content = "Üst verileri koru", IsChecked = true };
    private readonly CheckBox silence = new() { Content = "Başlangıç ve bitişteki sessizliği kaldır" };
    private readonly CheckBox join = new() { Content = "Tutulan parçaları birleştir" };
    private readonly CheckBox removeMetadata = new() { Content = "Mevcut üst verileri temizle", IsChecked = true };
    private readonly CancellationTokenSource lifetime = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly string temporary = Path.Combine(Path.GetTempPath(), "nashi-media-" + Guid.NewGuid().ToString("N"));
    private MediaPlayback? playback;
    private Task? work;
    private string? visualImage;
    private bool closed, updating, processed;
    private double? rangeEnd;
    private double playbackDuration;
    internal MediaOptions? Result { get; private set; }
    internal string Format => C("format");
    internal string[] Inputs => inputs;
    internal ImageCanvas EditorCanvas => canvas;
    internal bool Busy => work is { IsCompleted: false };

    internal MediaEditorWindow(MainWindow host, Toolchain tools, string action, string[] inputs, MediaInfo info, bool silentPlayback = false)
    {
        playbackDuration = info.Duration; this.silentPlayback = silentPlayback;
        this.host = host; this.tools = tools; this.action = action; this.inputs = inputs.ToArray(); this.info = info;
        engine = new(tools.Executable("ffmpeg"), tools.Executable("ffprobe"));
        canvas = new(Math.Max(1, info.Video?.Width ?? 1200), Math.Max(1, info.Video?.Height ?? 140)) { Mode = action is "cropVideo" or "redactVideo" ? "crop" : "view", PixelAspect = info.Video?.SampleAspect ?? 1 };
        Title = MainWindow.ToolTitle(action) + " · Nashi"; Width = 1120; Height = 850; MinWidth = 820; MinHeight = 600;
        Background = host.Background; Foreground = host.Foreground; RequestedThemeVariant = host.RequestedThemeVariant; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Directory.CreateDirectory(temporary);
        var root = new DockPanel { Margin = new Thickness(20) }; Content = root;
        var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var accept = host.Button("Kullan", () => { Accept(); return Task.CompletedTask; }, true);
        DockPanel.SetDock(accept, Dock.Right); heading.Children.Add(accept); heading.Children.Add(MainWindow.Label(MainWindow.ToolTitle(action), 25));
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        DockPanel.SetDock(message, Dock.Bottom); message.Margin = new Thickness(0, 12, 0, 0); root.Children.Add(message);
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,320"), ColumnSpacing = 16 }; root.Children.Add(columns);
        var view = new DockPanel(); columns.Children.Add(view);
        var transport = new StackPanel { Spacing = 8 };
        var buttons = new WrapPanel();
        buttons.Children.Add(host.Button("Oynat / duraklat", () => { playback?.Toggle(); rangeEnd = null; return Task.CompletedTask; }));
        buttons.Children.Add(host.Button("Seçimi oynat", PlayRange)); clock.Margin = new Thickness(12, 0, 0, 0); buttons.Children.Add(clock);
        transport.Children.Add(buttons); transport.Children.Add(timelineArtwork);
        position = new Slider { Minimum = 0, Maximum = Math.Max(.001, info.Duration), TickFrequency = 1, IsEnabled = info.Duration > 0 };
        AutomationProperties.SetName(position, "Oynatma konumu (saniye)"); transport.Children.Add(position);
        position.PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty && !updating) { rangeEnd = null; playback?.Seek(position.Value, playbackDuration); } };
        transport.Children.Add(MainWindow.Label("Başlangıç ve bitiş tutamaçları", 12));
        var startSlider = new Slider { Minimum = 0, Maximum = Math.Max(.001, info.Duration), IsEnabled = info.Duration >= .05 };
        var endSlider = new Slider { Minimum = 0, Maximum = Math.Max(.001, info.Duration), Value = info.Duration, IsEnabled = info.Duration >= .05 };
        AutomationProperties.SetName(startSlider, "Seçim başlangıcı"); AutomationProperties.SetName(endSlider, "Seçim bitişi");
        transport.Children.Add(startSlider); transport.Children.Add(endSlider);
        DockPanel.SetDock(transport, Dock.Bottom); view.Children.Add(transport);
        view.Children.Add(new Border { Child = canvas, ClipToBounds = true, CornerRadius = new CornerRadius(16) });
        Grid.SetColumn(settings, 1); columns.Children.Add(new ScrollViewer { Content = settings, [Grid.ColumnProperty] = 1 });
        settings.Children.Add(MainWindow.Label(Path.GetFileName(inputs[0]), 12, true));
        if (action == "convert") Choice("format", "Çıktı biçimi", Operations.Formats(inputs));
        Number("start", "Başlangıç (saniye)", 0, 0, (decimal)info.Duration, .01m);
        Number("end", "Bitiş (saniye)", (decimal)info.Duration, 0, (decimal)info.Duration, .01m);
        startSlider.PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty && !updating) numbers["start"].Value = (decimal)Math.Min(startSlider.Value, Math.Max(0, D("end") - .05)); };
        endSlider.PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty && !updating) numbers["end"].Value = (decimal)Math.Max(endSlider.Value, Math.Min(info.Duration, D("start") + .05)); };
        void SyncRange() { updating = true; startSlider.Value = D("start"); endSlider.Value = D("end"); updating = false; }
        numbers["start"].ValueChanged += (_, _) => SyncRange(); numbers["end"].ValueChanged += (_, _) => SyncRange();
        var marks = new WrapPanel();
        marks.Children.Add(host.Button("Başlangıcı buraya al", () => { if (processed) throw new ArgumentException("Aralık seçmek için önce özgünü göster."); numbers["start"].Value = (decimal)position.Value; return Task.CompletedTask; }));
        marks.Children.Add(host.Button("Bitişi buraya al", () => { if (processed) throw new ArgumentException("Aralık seçmek için önce özgünü göster."); numbers["end"].Value = (decimal)position.Value; return Task.CompletedTask; })); settings.Children.Add(marks);
        BuildSettings();
        Number("quality", "Kalite (%)", 85, 1, 100);
        Number("bitrate", "Ses bit hızı (kbps)", 256, 8, 512);
        Choice("depth", "Ses örnek derinliği", ["matchSource", "pcm16", "pcm24", "pcm32", "float32"]);
        if (info.Video is not null)
        {
            Number("maxHeight", "En fazla video yüksekliği (0: özgün)", 0, 0, 8192);
            Number("gifFps", "GIF kare hızı", 15, 1, 60); Number("gifWidth", "GIF genişliği", 960, 16, 8192);
        }
        settings.Children.Add(preserve);
        settings.Children.Add(host.Button("İşlenmiş çıktıyı önizle", ProcessPreview));
        settings.Children.Add(host.Button("Özgünü göster", () => StartWork(LoadOriginal)));
        settings.Children.Add(host.Button("Önizlemeyi iptal et", () => { previewCancellation?.Cancel(); return Task.CompletedTask; }));
        settings.Children.Add(previewFiles);
        previewFiles.SelectionChanged += async (_, _) =>
        {
            if (previewFiles.SelectedItem is string path && !Busy) await StartWork(() => OpenPreview(path));
        };
        KeyDown += (_, e) =>
        {
            if (e.Source is TextBox or NumericUpDown) return;
            if (e.Key == Key.Space) { playback?.Toggle(); rangeEnd = null; e.Handled = true; }
            if (e.Key is Key.Left or Key.Right && !processed && info.Duration > 0)
            { playback?.Seek(Math.Clamp(position.Value + (e.Key == Key.Left ? -1 : 1) * (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 1 / Math.Max(1, info.Video?.FrameRate ?? 10) : 1), 0, info.Duration), info.Duration); e.Handled = true; }
            if (e.Key == Key.M && action == "splitVideo") { AddTime("points"); e.Handled = true; }
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        };
        timer.Tick += (_, _) => Tick();
        Opened += async (_, _) => { timer.Start(); await StartWork(LoadOriginal); };
        Closing += (_, e) => { if (Busy) { previewCancellation?.Cancel(); message.Text = "İşlem iptal ediliyor; ardından pencereyi kapatabilirsin."; e.Cancel = true; } };
        Closed += (_, _) => { closed = true; lifetime.Cancel(); timer.Stop(); canvas.Dispose(); playback?.Dispose(); timelineBitmap?.Dispose(); lifetime.Dispose(); try { Directory.Delete(temporary, true); } catch (IOException error) { Console.Error.WriteLine("Önizleme temizlenemedi: " + error.Message); } };
    }
    private void BuildSettings()
    {
        switch (action)
        {
            case "trimAudio": settings.Children.Add(silence); break;
            case "cropVideo":
                settings.Children.Add(MainWindow.Label("Kare üzerinde sürükleyerek alan seç. Alanı taşı; sağ alt köşeden boyutlandır. Shift oranı korur.", 12, true)); break;
            case "changeVideoSpeed": Number("speed", "Hız çarpanı", 1, .05m, 100, .05m); break;
            case "normalizeAudio":
                Number("lufs", "Hedef ses yüksekliği (LUFS)", -16, -70, -5, .1m); Number("lra", "Ses yüksekliği aralığı (LU)", 11, 1, 50, .1m); Number("peak", "Gerçek tepe (dBTP)", -1.5m, -9, 0, .1m); break;
            case "compress": Number("target", "Hedef boyut (KB; 0: otomatik)", 0, 0, 20000000); Number("reduction", "Boyut azaltma (%; 0: otomatik)", 0, 0, 99); break;
            case "audioChannels": Number("channels", "Kanal (1: mono, 2: stereo)", 2, 1, 2); Number("left", "Sol kanal kazancı", 1, 0, 4, .1m); Number("right", "Sağ kanal kazancı", 1, 0, 4, .1m); break;
            case "videoSnapshots": Text("snapshots", "Kare zamanları (saniye; noktalı virgülle)", "0"); settings.Children.Add(host.Button("Bu kareyi ekle", () => { AddTime("snapshots"); return Task.CompletedTask; })); break;
            case "splitVideo":
                Choice("split", "Bölme yöntemi", ["İşaretler", "Eşit parçalar", "Sabit süre"]); Text("points", "Bölme zamanları (saniye; noktalı virgülle)", "");
                settings.Children.Add(host.Button("Buraya işaret ekle (M)", () => { AddTime("points"); return Task.CompletedTask; }));
                Number("clips", "Eşit parça sayısı", 2, 2, 1000); Number("interval", "Parça süresi (saniye)", 30, .05m, 864000, .1m);
                Text("discard", "Çıkarılacak parça numaraları (1;3;…)", ""); settings.Children.Add(join); break;
            case "joinVideos":
                inputList.ItemsSource = inputs.Select(Path.GetFileName).ToArray(); settings.Children.Add(inputList);
                foreach (var (label, delta) in new[] { ("Yukarı", -1), ("Aşağı", 1) }) settings.Children.Add(host.Button(label, () =>
                {
                    var index = inputList.SelectedIndex; var next = index + delta;
                    if (index >= 0 && next >= 0 && next < inputs.Length) { (inputs[index], inputs[next]) = (inputs[next], inputs[index]); inputList.ItemsSource = inputs.Select(Path.GetFileName).ToArray(); inputList.SelectedIndex = next; }
                    return Task.CompletedTask;
                })); break;
            case "audioToVideo":
                Choice("visualization", "Görselleştirme", ["waveform", "spectrum", "bars", "image"]);
                Number("width", "Video genişliği", 1280, 16, 8192); Number("height", "Video yüksekliği", 720, 16, 8192);
                settings.Children.Add(host.Button("Görsel seç", async () =>
                {
                    var paths = await StorageProvider.OpenFilePickerAsync(new() { Title = "Görselleştirme görseli" });
                    if (paths.FirstOrDefault()?.TryGetLocalPath() is { } path) { using var image = PhotoCodec.Read(path); visualImage = path; choices["visualization"].SelectedIndex = 3; }
                })); break;
            case "redactAudio": case "redactVideo":
                Choice("redaction", "Sansür türü", action == "redactAudio" ? ["bleep", "mute"] : ["solid", "blur", "pixelate"]);
                settings.Children.Add(host.Button("Seçili aralığı / alanı ekle", () => { AddRedaction(); return Task.CompletedTask; })); settings.Children.Add(ranges);
                settings.Children.Add(host.Button("Seçileni sil", () =>
                {
                    var index = ranges.SelectedIndex; if (index >= 0) { if (action == "redactAudio") audioRanges.RemoveAt(index); else videoRegions.RemoveAt(index); ReloadRanges(); } return Task.CompletedTask;
                }));
                ranges.SelectionChanged += (_, _) =>
                {
                    var index = ranges.SelectedIndex;
                    if (index < 0) return;
                    if (action == "redactVideo" && index < videoRegions.Count) { var r = videoRegions[index]; canvas.Crop = new(r.X, r.Y, r.Width, r.Height); numbers["start"].Value = (decimal)r.Start; numbers["end"].Value = (decimal)(r.End ?? info.Duration); }
                    else if (index < audioRanges.Count) { numbers["start"].Value = (decimal)audioRanges[index].Start; numbers["end"].Value = (decimal)audioRanges[index].End; }
                }; break;
            case "removeMetadata":
                settings.Children.Add(removeMetadata); Text("metadata", "Üst veri (satır başına anahtar=değer)", ""); texts["metadata"].AcceptsReturn = true; texts["metadata"].MinHeight = 100; break;
        }
    }
    private NumericUpDown Number(string key, string label, decimal value, decimal min, decimal max, decimal step = 1)
    {
        settings.Children.Add(MainWindow.Label(label, 12)); var field = new NumericUpDown { Value = value, Minimum = min, Maximum = max, Increment = step }; AutomationProperties.SetName(field, label); numbers.Add(key, field); settings.Children.Add(field); return field;
    }
    private void Choice(string key, string label, string[] values)
    {
        settings.Children.Add(MainWindow.Label(label, 12)); var field = new ComboBox { ItemsSource = values.Select(value => new ComboBoxItem { Tag = value, Content = value switch
        { "solid" => "Siyah alan", "blur" => "Bulanıklaştır", "pixelate" => "Pikselleştir", "bleep" => "Bip sesi", "mute" => "Sessiz", "matchSource" => "Kaynakla aynı", "pcm16" => "16 bit PCM", "pcm24" => "24 bit PCM", "pcm32" => "32 bit PCM", "float32" => "32 bit kayan nokta", "waveform" => "Ses dalgası", "spectrum" => "Spektrum", "bars" => "Çubuklar", "image" => "Görsel", _ => value } }).ToArray(), SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch }; AutomationProperties.SetName(field, label); choices.Add(key, field); settings.Children.Add(field);
    }
    private void Text(string key, string label, string value)
    {
        settings.Children.Add(MainWindow.Label(label, 12)); var field = new TextBox { Text = value }; AutomationProperties.SetName(field, label); texts.Add(key, field); settings.Children.Add(field);
    }
    private double D(string key, double fallback = 0) => numbers.TryGetValue(key, out var field) ? (double)(field.Value ?? throw new ArgumentException("Sayısal alan boş bırakılamaz.")) : fallback;
    private string C(string key, string fallback = "") => choices.TryGetValue(key, out var field) ? (field.SelectedItem as ComboBoxItem)?.Tag as string ?? fallback : fallback;
    private double[] Values(string key)
    {
        if (!texts.TryGetValue(key, out var field) || string.IsNullOrWhiteSpace(field.Text)) return [];
        return field.Text.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(value => double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) ? n : throw new ArgumentException("Zamanları saniye cinsinden yaz; ondalık ayracı nokta, liste ayracı noktalı virgül olmalı.")).ToArray();
    }
    private void AddTime(string key)
    {
        if (processed) throw new ArgumentException("İşaret eklemek için önce özgünü göster.");
        var times = Values(key).Append(position.Value).Distinct().Order().Select(n => n.ToString("0.###", CultureInfo.InvariantCulture)); texts[key].Text = string.Join(';', times);
    }
    private MediaRange Range()
    {
        var start = D("start"); var end = D("end");
        if (start < 0 || end > info.Duration || Math.Round(end - start, 6) < .05) throw new ArgumentException("En az 0,05 saniyelik ve medya süresi içinde bir aralık seç.");
        return new(start, end);
    }
    internal void AddRedaction()
    {
        var range = Range();
        if (action == "redactAudio") audioRanges.Add(range);
        else { var area = canvas.Crop ?? throw new ArgumentException("Kare üzerinde bir alan çiz."); area.Validate(canvas.ImageWidth, canvas.ImageHeight); videoRegions.Add(new(area.X, area.Y, area.Width, area.Height, range.Start, range.End)); canvas.Crop = null; }
        ReloadRanges();
    }
    private void ReloadRanges()
    {
        ranges.ItemsSource = action == "redactAudio" ? audioRanges.Select((r, i) => $"{i + 1} · {r.Start:0.###}–{r.End:0.###} s").ToArray() : videoRegions.Select((r, i) => $"{i + 1} · {r.Width:0}×{r.Height:0} · {r.Start:0.###}–{r.End:0.###} s").ToArray();
    }
    internal MediaOptions Options()
    {
        var range = action is "trimAudio" or "trimVideo" ? Range() : new MediaRange(D("start"), D("end"));
        var regions = videoRegions.ToArray();
        if (action == "cropVideo") { var area = canvas.Crop ?? throw new ArgumentException("Kırpılacak alanı çiz."); area.Validate(canvas.ImageWidth, canvas.ImageHeight); regions = [new(area.X, area.Y, area.Width, area.Height)]; }
        if (action == "redactVideo" && regions.Length == 0 || action == "redactAudio" && audioRanges.Count == 0) throw new ArgumentException("Önce sansürlenecek aralığı / alanı ekle.");
        var discard = Values("discard"); if (discard.Any(n => n < 1 || n != Math.Truncate(n) || n > 10000)) throw new ArgumentException("Parça numaraları pozitif tam sayı olmalı.");
        if (action == "splitVideo" && C("split") == "İşaretler" && Values("points").Length == 0) throw new ArgumentException("En az bir bölme işareti ekle.");
        var metadata = new Dictionary<string, string>();
        if (texts.TryGetValue("metadata", out var tags)) foreach (var line in (tags.Text ?? "").Split('\n').Where(s => !string.IsNullOrWhiteSpace(s)))
        { var split = line.IndexOf('='); if (split <= 0) throw new ArgumentException("Üst verileri anahtar=değer biçiminde gir."); metadata[line[..split].Trim()] = line[(split + 1)..]; }
        return new()
        {
            Quality = D("quality", 85) / 100, AudioBitrate = (int)D("bitrate", 256), SampleDepth = C("depth", "matchSource"), GifFrameRate = (int)D("gifFps", 15), GifWidth = (int)D("gifWidth", 960),
            Start = range.Start, End = range.End, Speed = D("speed", 1), Width = numbers.ContainsKey("width") ? (int)D("width") : null,
            Height = numbers.ContainsKey("height") ? (int)D("height") : D("maxHeight") > 0 ? (int)D("maxHeight") : null,
            Channels = (int)D("channels", 2), LeftVolume = D("left", 1), RightVolume = D("right", 1), TargetLoudness = D("lufs", -16), LoudnessRange = D("lra", 11), TruePeak = D("peak", -1.5),
            TargetBytes = D("target") > 0 ? (long)D("target") * 1024 : null, ReductionPercent = D("reduction") > 0 ? D("reduction") : null,
            PreserveMetadata = preserve.IsChecked == true, TrimSilence = silence.IsChecked == true, SnapshotTimes = Values("snapshots"),
            SplitPoints = C("split") == "İşaretler" ? Values("points") : [], EqualClips = C("split") == "Eşit parçalar" ? (int)D("clips") : null,
            SplitInterval = C("split") == "Sabit süre" ? D("interval") : 30, DiscardedClips = discard.Select(n => (int)n - 1).ToArray(), JoinSplitClips = join.IsChecked == true,
            Visualization = C("visualization", "waveform"), VisualizationImage = visualImage, RedactionMode = C("redaction", "solid"), Regions = regions, AudioRedactions = audioRanges.ToArray(), RemoveAllMetadata = removeMetadata.IsChecked == true, Metadata = metadata
        };
    }
    private void Accept()
    {
        if (Busy) { message.Text = "Önce önizlemenin tamamlanmasını bekle veya iptal et."; return; }
        try { Result = Options(); Close(Result); } catch (ArgumentException error) { message.Text = error.Message; }
    }
    internal async Task PlayRange()
    {
        if (Busy) throw new ArgumentException("Önizlemenin hazırlanmasını bekle.");
        if (processed) throw new ArgumentException("Aralık seçmek için önce özgünü göster.");
        var r = Range(); var current = playback ?? throw new ArgumentException("Önce medya önizlemesini hazırla.");
        current.Player.SetPause(true); current.Seek(r.Start, info.Duration);
        for (var n = 0; n < 100 && Math.Abs(current.Player.Time / 1000d - r.Start) > .03; n++)
        { await Task.Delay(20, lifetime.Token); if (playback != current) return; }
        if (Math.Abs(current.Player.Time / 1000d - r.Start) > .03) throw new IOException("Seçili aralığın başlangıcına gidilemedi.");
        rangeEnd = r.End; current.Player.SetPause(false);
    }
    private void Tick()
    {
        if (playback is null) return;
        var seconds = Math.Max(0, playback.Player.Time / 1000d);
        updating = true; position.Value = Math.Clamp(seconds, 0, playbackDuration); updating = false;
        clock.Text = $"{TimeSpan.FromSeconds(seconds):hh\\:mm\\:ss\\.ff} / {TimeSpan.FromSeconds(playbackDuration):hh\\:mm\\:ss\\.ff}";
        if (rangeEnd is { } end && seconds >= end) { playback.Player.SetPause(true); rangeEnd = null; }
        canvas.Regions = processed ? [] : videoRegions.Where(r => seconds >= r.Start && seconds <= (r.End ?? info.Duration)).Select(r => new ImageArea(r.X, r.Y, r.Width, r.Height)).ToArray(); canvas.InvalidateVisual();
    }
    private CancellationTokenSource? previewCancellation;
    private async Task StartWork(Func<Task> job)
    {
        if (Busy || closed) return;
        var running = job(); work = running;
        try { await running; }
        catch (OperationCanceledException) { if (!closed) message.Text = "Önizleme iptal edildi. Orijinal dosya korundu."; }
        catch (Exception error) { if (!closed) message.Text = "Önizleme hazırlanamadı: " + error.Message; }
        finally { work = null; }
    }
    private Task ProcessPreview() => StartWork(async () =>
    {
        var options = Options(); playback?.Player.SetPause(true); rangeEnd = null;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); previewCancellation = cancellation;
        try
        {
            var destination = Path.Combine(temporary, Guid.NewGuid().ToString("N"));
            var results = await new Operations(tools).RunAsync(action, inputs, destination, Format, (int)(options.Quality * 100), 0, 0, 0, 0, options,
                new Progress<OperationProgress>(p => { if (!closed) message.Text = p.Label; }), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested(); if (closed) return;
            previewFiles.ItemsSource = results.ToArray();
            if (results.Count > 0) await OpenPreview(results[0]);
        }
        finally { previewCancellation = null; }
    });
    private async Task LoadOriginal()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); cancellation.CancelAfter(TimeSpan.FromMinutes(2)); previewCancellation = cancellation;
        try
        {
            canvas.ClearImage(); playback?.Dispose(); playback = null; processed = false; rangeEnd = null; canvas.Mode = action is "cropVideo" or "redactVideo" ? "crop" : "view";
            var still = await MediaPlayback.ArtworkAsync(tools, temporary, inputs[0], info, cancellation.Token);
            if (closed) return; canvas.SetImage(still);
            await OpenPlayer(inputs[0], info, cancellation.Token); position.IsEnabled = info.Duration > 0;
            message.Text = "Özgün dosya · Oynatmak için Space. Dışa aktarma özgün dosyayı kullanır.";
            if (timelineBitmap is null)
            {
                var strip = info.Video is null ? still : await Filmstrip(cancellation.Token);
                using var stream = new MemoryStream(strip); timelineBitmap = new Bitmap(stream); timelineArtwork.Source = timelineBitmap;
            }
        }
        finally { previewCancellation = null; }
    }
    private async Task OpenPreview(string path)
    {
        var token = previewCancellation?.Token ?? lifetime.Token;
        canvas.ClearImage(); playback?.Dispose(); playback = null; rangeEnd = null; processed = true; canvas.Mode = "view"; canvas.Regions = []; position.IsEnabled = false;
        if (FeatureCatalog.IsImage(path)) canvas.SetImage(await File.ReadAllBytesAsync(path, token));
        else
        {
            var previewInfo = await engine.InspectAsync(path, token);
            canvas.SetImage(await MediaPlayback.ArtworkAsync(tools, temporary, path, previewInfo, token)); await OpenPlayer(path, previewInfo, token);
        }
        message.Text = "İşlenmiş çıktı · " + Path.GetFileName(path) + " · oynatmak için Space.";
    }
    private async Task OpenPlayer(string path, MediaInfo media, CancellationToken token)
    {
        playbackDuration = media.Duration; updating = true; position.Maximum = Math.Max(.001, media.Duration); position.Value = 0; updating = false; position.IsEnabled = media.Duration > 0;
        playback = new MediaPlayback(media, silent: silentPlayback); var current = playback;
        current.FrameUpdated += () => { if (!closed && playback == current && current.Frame is { } frame) canvas.SetFrame(frame); };
        current.Failed += text => { if (!closed && playback == current) message.Text = text; };
        await current.OpenReadyAsync(path, token);
    }
    private async Task<byte[]> Filmstrip(CancellationToken token)
    {
        var files = new List<string>();
        for (var i = 0; i < 8; i++)
        {
            var output = Path.Combine(temporary, $"timeline-{i}.png");
            var time = (Math.Max(0, info.Duration - .1) * i / 8).ToString("0.######", CultureInfo.InvariantCulture);
            await ProcessRunner.RunAsync(tools.Executable("ffmpeg"), ["-v", "error", "-nostdin", "-ss", time, "-i", inputs[0], "-map", "0:v:0", "-frames:v", "1", "-vf", "scale=120:68:force_original_aspect_ratio=decrease,pad=120:68:(ow-iw)/2:(oh-ih)/2", output], token);
            if (File.Exists(output)) files.Add(output);
        }
        return await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested(); using var images = new MagickImageCollection(); foreach (var file in files) images.Add(file);
            if (images.Count == 0) throw new IOException("Zaman çizgisi için video karesi okunamadı.");
            using var strip = images.AppendHorizontally(); return strip.ToByteArray(MagickFormat.Png);
        }, token);
    }

}
