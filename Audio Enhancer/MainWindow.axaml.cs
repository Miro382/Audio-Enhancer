using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Audio_Enhancer
{
    public partial class MainWindow : Window
    {
        private static readonly FilePickerFileType WavFileType = new("WAV audio")
        {
            Patterns = new[] { "*.wav" },
        };

        private string? _inputPath;
        private bool _busy;

        public MainWindow()
        {
            InitializeComponent();
            AddHandler(DragDrop.DragOverEvent, OnDragOver);
            AddHandler(DragDrop.DropEvent, OnDrop);
        }

        private async void OnBrowseClick(object? sender, RoutedEventArgs e)
        {
            if (_busy) return;
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select a WAV file",
                AllowMultiple = false,
                FileTypeFilter = new[] { WavFileType },
            });
            if (files.Count == 1 && files[0].TryGetLocalPath() is { } path)
                LoadFile(path);
        }

        private void OnDragOver(object? sender, DragEventArgs e)
        {
            e.DragEffects = !_busy && TryGetWavPath(e) is not null
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        }

        private void OnDrop(object? sender, DragEventArgs e)
        {
            if (!_busy && TryGetWavPath(e) is { } path)
                LoadFile(path);
        }

        private static string? TryGetWavPath(DragEventArgs e)
        {
            if (e.DataTransfer.TryGetFiles() is { } items)
                foreach (var item in items)
                    if (item.TryGetLocalPath() is { } path &&
                        path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                        return path;
            return null;
        }

        private void LoadFile(string path)
        {
            try
            {
                var (format, duration, sizeBytes) = AudioProcessor.ReadInfo(path);
                _inputPath = path;
                FileNameText.Text = Path.GetFileName(path);
                FileInfoText.Text =
                    $"{AudioProcessor.DescribeFormat(format)} • {duration:mm\\:ss} • {sizeBytes / 1024.0 / 1024.0:0.#} MB";
                InfoPanel.IsVisible = true;
                EnhanceButton.IsEnabled = true;
                StatusText.Text = "";
            }
            catch (Exception ex)
            {
                _inputPath = null;
                InfoPanel.IsVisible = false;
                EnhanceButton.IsEnabled = false;
                StatusText.Text = $"Could not load the file: {ex.Message}";
            }
        }

        private async void OnEnhanceClick(object? sender, RoutedEventArgs e)
        {
            if (_busy || _inputPath is null) return;

            var inputDir = Path.GetDirectoryName(_inputPath);
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save enhanced WAV",
                SuggestedFileName = Path.GetFileNameWithoutExtension(_inputPath) + " (24-bit 48 kHz).wav",
                DefaultExtension = "wav",
                FileTypeChoices = new[] { WavFileType },
                SuggestedStartLocation = inputDir is null
                    ? null
                    : await StorageProvider.TryGetFolderFromPathAsync(inputDir),
            });
            if (file?.TryGetLocalPath() is not { } outputPath)
                return;

            if (string.Equals(Path.GetFullPath(outputPath), Path.GetFullPath(_inputPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                StatusText.Text = "The output file cannot be the same as the input — choose a different name.";
                return;
            }

            var options = new EnhanceOptions
            {
                NormalizeLoudness = NormalizeCheck.IsChecked == true,
                RemoveDcOffset = DcOffsetCheck.IsChecked == true,
            };

            SetBusy(true);
            StatusText.Text = "Processing…";
            var progress = new Progress<double>(v => Progress.Value = v);
            var inputPath = _inputPath;
            try
            {
                var result = await Task.Run(
                    () => AudioProcessor.Enhance(inputPath, outputPath, options, progress, CancellationToken.None));

                string gainInfo = options.NormalizeLoudness
                    ? $" Loudness adjusted by {result.AppliedGainDb:+0.0;−0.0;0} dB."
                    : "";
                StatusText.Text = $"Done ✅ Saved as \"{Path.GetFileName(outputPath)}\".{gainInfo}";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Processing failed: {ex.Message}";
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            BrowseButton.IsEnabled = !busy;
            EnhanceButton.IsEnabled = !busy && _inputPath is not null;
            NormalizeCheck.IsEnabled = !busy;
            DcOffsetCheck.IsEnabled = !busy;
            Progress.IsVisible = busy;
            if (busy) Progress.Value = 0;
        }
    }
}
