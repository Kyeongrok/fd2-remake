using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Fd2.Data;
using Microsoft.Win32;

namespace Fd2.Editor;

/// <summary>
/// 배경음악 탭 — FDMUS.DAT 의 곡(XMIDI)을 MIDI 로 바꿔(%LOCALAPPDATA%\Fd2\music, 게임과 같은 곳) MCI 시퀀서로 듣는다.
/// 곡마다 길이와 쓰인 악기(GM 이름)를 보인다.
/// </summary>
public partial class MainWindow
{
    private sealed record MusicItem(int Index, string Length, string Instruments);

    private const string MusicAlias = "fd2edbgm";
    private readonly DispatcherTimer _musicTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private int _musicPlaying = -1;

    private static string MusicCache =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fd2", "music");

    private void FillMusic()
    {
        StopMusic();
        var items = new List<MusicItem>();
        int count = Music.Count(_dir);
        for (int i = 0; i < count; i++)
        {
            if (!Music.Has(_dir, i)) continue;
            var xmi = Dat.Load(Path.Combine(_dir, "FDMUS.DAT"), i);
            double sec = Music.Seconds(xmi);
            string inst = string.Join(", ", Music.Programs(xmi).Select(p => Music.GmNames[p]));
            items.Add(new MusicItem(i, $"{(int)sec / 60}:{(int)sec % 60:D2}", inst));
        }
        MusicList.ItemsSource = items;
        if (items.Count > 0) MusicList.SelectedIndex = 0;
        if (!_musicTimerHooked)
        {
            _musicTimer.Tick += OnMusicTick;
            _musicTimerHooked = true;
            Closed += (_, _) => StopMusic();
        }
    }

    private bool _musicTimerHooked;

    private void OnMusicPlay(object sender, RoutedEventArgs e)
    {
        if (MusicList.SelectedItem is not MusicItem item) return;
        StopMusic();
        string? file;
        try { file = Music.MidiFile(_dir, item.Index, MusicCache); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or IndexOutOfRangeException or UnauthorizedAccessException)
        {
            MusicNow.Text = $"곡 {item.Index} 을 바꾸지 못했습니다: {ex.Message}";
            return;
        }
        if (file == null) return;
        int err = Mci($"open \"{file}\" type sequencer alias {MusicAlias}");
        if (err != 0)
        {
            MusicNow.Text = $"MIDI 장치를 열지 못했습니다(MCI 오류 {err}).";
            return;
        }
        Mci($"set {MusicAlias} time format milliseconds");
        Mci($"play {MusicAlias} from 0");
        _musicPlaying = item.Index;
        _musicTimer.Start();
        OnMusicTick(null, EventArgs.Empty);
    }

    private void OnMusicStop(object sender, RoutedEventArgs e) => StopMusic();

    private void OnMusicListKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { OnMusicPlay(sender, e); e.Handled = true; }
        else if (e.Key == Key.Escape) { StopMusic(); e.Handled = true; }
    }

    private void OnMusicTick(object? sender, EventArgs e)
    {
        if (_musicPlaying < 0) return;
        string mode = MciStatus("mode");
        if (mode == "stopped" && MusicLoop.IsChecked == true) { Mci($"play {MusicAlias} from 0"); mode = "playing"; }
        int.TryParse(MciStatus("position"), out int pos);
        int.TryParse(MciStatus("length"), out int len);
        static string Clock(int ms) => $"{ms / 60000}:{ms / 1000 % 60:D2}";
        MusicNow.Text = mode == "playing" ? $"♪ 곡 {_musicPlaying}  {Clock(pos)} / {Clock(len)}" : $"곡 {_musicPlaying} 끝";
    }

    private void StopMusic()
    {
        _musicTimer.Stop();
        if (_musicPlaying < 0) return;
        Mci($"stop {MusicAlias}");
        Mci($"close {MusicAlias}");
        _musicPlaying = -1;
        MusicNow.Text = "";
    }

    private void OnMusicSave(object sender, RoutedEventArgs e)
    {
        if (MusicList.SelectedItem is not MusicItem item || Music.ToMidi(_dir, item.Index) is not { } midi) return;
        var dlg = new SaveFileDialog { FileName = $"FDMUS {item.Index:D2}.mid", Filter = "MIDI 파일 (*.mid)|*.mid" };
        if (dlg.ShowDialog(this) != true) return;
        File.WriteAllBytes(dlg.FileName, midi);
        MusicNow.Text = $"저장했습니다: {dlg.FileName}";
    }

    private static string MciStatus(string what)
    {
        var sb = new StringBuilder(64);
        return mciSendStringW($"status {MusicAlias} {what}", sb, sb.Capacity, IntPtr.Zero) == 0 ? sb.ToString() : "";
    }

    private static int Mci(string command) => mciSendStringW(command, null, 0, IntPtr.Zero);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int mciSendStringW(string command, StringBuilder? buffer, int bufferSize, IntPtr callback);
}
