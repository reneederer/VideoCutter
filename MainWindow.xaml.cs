using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace VideoCutter
{
    public partial class MainWindow : Window
    {
        private DispatcherTimer _timer;
        private TimeSpan _playRangeStart = TimeSpan.Zero;
        private TimeSpan _playRangeEnd = TimeSpan.Zero;
        private bool _isPlayingRange = false;
        private string? _videoPath;
        private bool _suppressTextEvent = false;

        public MainWindow()
        {
            InitializeComponent();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _timer.Tick += Timer_Tick;
        }

        private void BtnOpen_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog();
            ofd.Filter = "Video files|*.mp4;*.mkv;*.avi;*.mov;*.wmv|All files|*.*";
            if (ofd.ShowDialog() == true)
            {
                _videoPath = ofd.FileName;
                txtFile.Text = _videoPath;
                media.Source = new Uri(_videoPath);
                lblStatus.Text = "Loaded: " + Path.GetFileName(_videoPath);
            }
        }

        private void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            if (media.Source == null) return;
            media.Play();
            _timer.Start();
            _isPlayingRange = false;
            lblStatus.Text = "Playing full video";
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            media.Stop();
            _timer.Stop();
            _isPlayingRange = false;
            progress.Value = 0;
            lblTime.Text = "00:00:00.000 / 00:00:00.000";
            lblStatus.Text = "Stopped";
        }

        private void BtnPlayRange_Click(object sender, RoutedEventArgs e)
        {
            if (media.Source == null) return;
            if (!TryParseTimes(out var start, out var end)) return;

            if (end <= start)
            {
                end = start + TimeSpan.FromSeconds(15);
                SetTextSilently(txtEnd, FormatTimeSpanWithMs(end));
            }

            _playRangeStart = start;
            _playRangeEnd = end;
            media.Position = _playRangeStart;
            media.Play();
            _isPlayingRange = true;
            _timer.Start();
            lblStatus.Text = $"Looping range {FormatTimeSpanWithMs(start)} - {FormatTimeSpanWithMs(end)}";
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (media.NaturalDuration.HasTimeSpan && media.NaturalDuration.TimeSpan.TotalMilliseconds > 0)
            {
                var position = media.Position;
                var duration = media.NaturalDuration.TimeSpan;
                progress.Value = position.TotalSeconds / Math.Max(1, duration.TotalSeconds);

                lblTime.Text = $"{FormatTimeSpanWithMs(position)} / {FormatTimeSpanWithMs(duration)}";

                if (_isPlayingRange && position >= _playRangeEnd)
                {
                    media.Position = _playRangeStart; // loop
                }
            }
        }

        private bool TryParseTimes(out TimeSpan start, out TimeSpan end)
        {
            start = TimeSpan.Zero;
            end = TimeSpan.Zero;

            bool okStart = TryParseTime(txtStart.Text.Trim(), out start);
            bool okEnd =
                txtEnd != null && TryParseTime(txtEnd.Text.Trim(), out end);

            if (!okStart || !okEnd) return false;
            return true;
        }

        private static bool TryParseTime(string input, out TimeSpan result)
        {
            if (!input.Contains(".")) input += ".000";
            return TimeSpan.TryParseExact(input, @"hh\:mm\:ss\.fff", null, out result);
        }

        private async void BtnCut_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_videoPath)) return;
            if (!TryParseTimes(out var start, out var end)) return;

            if (end <= start)
            {
                end = start + TimeSpan.FromSeconds(15);
                SetTextSilently(txtEnd, FormatTimeSpanWithMs(end));
            }

            var sfd = new SaveFileDialog();
            sfd.Filter = "MP4 file|*.mp4|All files|*.*";
            sfd.FileName = Path.GetFileNameWithoutExtension(_videoPath) + $"_{FormatFileSafe(start)}_{FormatFileSafe(end)}.mp4";
            if (sfd.ShowDialog() != true) return;

            var outPath = sfd.FileName;

            lblStatus.Text = "Cutting...";
            btnCut.IsEnabled = false;

            try
            {
                var ffmpegName = "ffmpeg.exe";
                var duration = end - start;
                var args = $"-ss {FormatTimeSpanWithMs(start)} -i \"{_videoPath}\" -t {FormatTimeSpanWithMs(duration)} -c copy \"{outPath}\" -y";

                var psi = new ProcessStartInfo(ffmpegName, args)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                };

                var proc = Process.Start(psi);
                if (proc == null)
                    throw new InvalidOperationException("Failed to start ffmpeg. Ensure ffmpeg.exe is available.");

                await proc.StandardError.ReadToEndAsync();
                proc.WaitForExit();

                lblStatus.Text = proc.ExitCode == 0 ? "Cut saved: " + outPath : $"ffmpeg failed (code {proc.ExitCode})";
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Error: " + ex.Message;
            }
            finally
            {
                btnCut.IsEnabled = true;
            }
        }

        private void ApplyStartEndTimes()
        {
            if (!TryParseTimes(out var start, out var end)) return;

            if (end <= start)
                end = start + TimeSpan.FromSeconds(15);

            SetTextSilently(txtEnd, FormatTimeSpanWithMs(end));

            _playRangeStart = start;
            _playRangeEnd = end;

            if (media.Source != null)
            {
                media.Position = _playRangeStart;
                media.Play();
                _isPlayingRange = true;
                _timer.Start();
            }
        }


        // progress bar click -> set start, end = start+10s
        private void Progress_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!media.NaturalDuration.HasTimeSpan) return;
            var duration = media.NaturalDuration.TimeSpan;

            var pos = e.GetPosition(progress);
            double ratio = pos.X / progress.ActualWidth;
            if (ratio < 0) ratio = 0;
            if (ratio > 1) ratio = 1;

            var newStart = TimeSpan.FromSeconds(duration.TotalSeconds * ratio);
            var newEnd = newStart + TimeSpan.FromSeconds(10);

            SetTextSilently(txtStart, FormatTimeSpanWithMs(newStart));
            SetTextSilently(txtEnd, FormatTimeSpanWithMs(newEnd));

            _playRangeStart = newStart;
            _playRangeEnd = newEnd;
            media.Position = _playRangeStart;
            media.Play();
            _isPlayingRange = true;
            _timer.Start();
        }

        // nudge buttons
        private void BtnStartMinus_Click(object sender, RoutedEventArgs e) => NudgeTime(txtStart, -0.5);
        private void BtnStartPlus_Click(object sender, RoutedEventArgs e) => NudgeTime(txtStart, 0.5);
        private void BtnEndMinus_Click(object sender, RoutedEventArgs e) => NudgeTime(txtEnd, -0.5);
        private void BtnEndPlus_Click(object sender, RoutedEventArgs e) => NudgeTime(txtEnd, 0.5);

        private void NudgeTime(System.Windows.Controls.TextBox box, double seconds)
        {
            if (TryParseTime(box.Text, out var ts))
            {
                ts = ts.Add(TimeSpan.FromSeconds(seconds));
                if (ts < TimeSpan.Zero) ts = TimeSpan.Zero;
                SetTextSilently(box, FormatTimeSpanWithMs(ts));

                ApplyStartEndTimes();
            }
        }

        // react to manual text change
        private void TimeBox_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_suppressTextEvent) return;
            ApplyStartEndTimes();
        }

        private void SetTextSilently(System.Windows.Controls.TextBox box, string text)
        {
            _suppressTextEvent = true;
            box.Text = text;
            _suppressTextEvent = false;
        }

        private static string FormatTimeSpanWithMs(TimeSpan ts) =>
            string.Format("{0:00}:{1:00}:{2:00}.{3:000}", (int)ts.TotalHours, ts.Minutes, ts.Seconds, ts.Milliseconds);

        private static string FormatFileSafe(TimeSpan ts) =>
            $"{(int)ts.TotalHours:00}-{ts.Minutes:00}-{ts.Seconds:00}-{ts.Milliseconds:000}";
    }
}
