using DIEditor.Dialogs;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace DIEditor.Dialogs;

/// <summary>
/// 只允许在指定目录的“直接子文件”中选择的对话框。
/// 结构上不提供任何导航入口：没有文件夹项、没有上级按钮、路径栏只读。
/// </summary>
public sealed partial class LockedFolderPickerDialog : ContentDialog
{
    private readonly string _lockedFolder;
    private readonly HashSet<string> _extensions;
    private readonly DispatcherQueueTimer? _filterTimer;

    private List<FileItem> _allFiles = [];
    private bool _isClosed;

    /// <summary>用户最终选中的完整路径；未确定则为 null。</summary>
    public string? SelectedPath { get; private set; }

    /// <summary>被锁定的目录（规范化后的绝对路径）。</summary>
    public string LockedFolder => _lockedFolder;

    /// <param name="lockedFolder">锁定目录，可为相对路径，构造时会被规范化。</param>
    /// <param name="extensions">
    /// 允许的扩展名，可带点或不带点；传 null 或空集合表示不限制。
    /// </param>
    public LockedFolderPickerDialog(string lockedFolder, IEnumerable<string>? extensions = null)
    {
        InitializeComponent();

        if (string.IsNullOrWhiteSpace(lockedFolder))
        {
            throw new ArgumentException("必须提供目录路径。", nameof(lockedFolder));
        }

        // GetFullPath 吃掉 .. 和混合分隔符；TrimEndingDirectorySeparator 保证后续比较一致
        _lockedFolder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(lockedFolder));

        _extensions = new HashSet<string>(
            (extensions ?? Enumerable.Empty<string>())
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(NormalizeExtension),
            StringComparer.OrdinalIgnoreCase);

        FolderText.Text = _lockedFolder;
        ToolTipService.SetToolTip(FolderText, _lockedFolder);

        IsPrimaryButtonEnabled = false;

        // 200ms 防抖：目录内文件上千时，逐键过滤会明显发涩
        _filterTimer = DispatcherQueue?.CreateTimer();
        if (_filterTimer is not null)
        {
            _filterTimer.Interval = TimeSpan.FromMilliseconds(200);
            _filterTimer.IsRepeating = false;
            _filterTimer.Tick += OnFilterTimerTick;
        }

        Opened += OnDialogOpened;
        Closed += OnDialogClosed;
        PrimaryButtonClick += OnPrimaryButtonClick;
    }

    private static string NormalizeExtension(string raw)
    {
        var value = raw.Trim().ToLowerInvariant();
        return value.StartsWith('.') ? value : "." + value;
    }

    private async void OnDialogOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        _isClosed = false;
        SelectedPath = null;
        await LoadFilesAsync();
    }

    private void OnDialogClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        // 枚举是异步的，关闭后回调仍可能落到 UI 线程，用这个标志丢弃过期结果
        _isClosed = true;
        _filterTimer?.Stop();
    }

    /// <summary>枚举锁定目录的直接子文件。绝不递归，绝不触碰子目录。</summary>
    private async Task LoadFilesAsync()
    {
        LoadingRing.IsActive = true;
        EmptyText.Visibility = Visibility.Collapsed;

        try
        {
            if (!Directory.Exists(_lockedFolder))
            {
                _allFiles = [];
                ShowEmpty("目录不存在或已被移动");
                return;
            }

            var options = new EnumerationOptions
            {
                // 结构性约束：不递归，子目录里的文件永远不会进入列表
                RecurseSubdirectories = false,

                // 遇到无权限的子项直接跳过，而不是整批抛异常
                IgnoreInaccessible = true,

                // 注意：显式赋值会覆盖默认值（Hidden | System），这里只跳系统文件
                AttributesToSkip = FileAttributes.System,
            };

            // 网络路径或大目录同步枚举会卡住 UI 线程，ProgressRing 也转不起来
            var files = await Task.Run(() =>
                Directory.EnumerateFiles(_lockedFolder, "*", options)
                    .Where(MatchesExtension)
                    .Select(TryCreateItem)
                    .OfType<FileItem>()
                    .OrderByDescending(i => i.ModifiedTime)
                    .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList());

            if (_isClosed)
            {
                return;
            }

            _allFiles = files;
            ApplyFilter(SearchBox.Text);
        }
        catch (UnauthorizedAccessException)
        {
            _allFiles = [];
            ShowEmpty("没有访问该目录的权限");
        }
        catch (DirectoryNotFoundException)
        {
            _allFiles = [];
            ShowEmpty("目录不存在或已被移动");
        }
        catch (IOException ex)
        {
            _allFiles = [];
            ShowEmpty($"读取目录失败：{ex.Message}");
        }
        finally
        {
            LoadingRing.IsActive = false;
        }
    }

    private bool MatchesExtension(string path)
    {
        if (_extensions.Count == 0)
        {
            return true;
        }

        return _extensions.Contains(Path.GetExtension(path));
    }

    private static FileItem? TryCreateItem(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return null;
            }

            return new FileItem
            {
                Name = info.Name,
                FullPath = info.FullName,
                Extension = info.Extension.ToLowerInvariant(),
                Size = info.Length,
                ModifiedTime = info.LastWriteTime,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void ApplyFilter(string? keyword)
    {
        var trimmed = keyword?.Trim();

        var list = string.IsNullOrEmpty(trimmed)
            ? _allFiles
            : _allFiles
                .Where(i => i.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
                .ToList();

        FileList.ItemsSource = list;

        // 任何一次过滤都清空选中态，避免用户“看到的”和“将要提交的”不一致
        IsPrimaryButtonEnabled = false;
        SelectedPath = null;

        if (list.Count == 0)
        {
            EmptyText.Text = _allFiles.Count == 0 ? "此目录下没有可选文件" : "没有匹配的文件";
            EmptyText.Visibility = Visibility.Visible;
        }
        else
        {
            EmptyText.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowEmpty(string message)
    {
        FileList.ItemsSource = Array.Empty<FileItem>();
        IsPrimaryButtonEnabled = false;
        SelectedPath = null;
        EmptyText.Text = message;
        EmptyText.Visibility = Visibility.Visible;
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (_filterTimer is null)
        {
            ApplyFilter(SearchBox.Text);
            return;
        }

        _filterTimer.Stop();
        _filterTimer.Start();
    }

    private void OnFilterTimerTick(DispatcherQueueTimer sender, object args)
    {
        ApplyFilter(SearchBox.Text);
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        IsPrimaryButtonEnabled = FileList.SelectedItem is FileItem;
    }

    private void OnListDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (FileList.SelectedItem is FileItem item)
        {
            SelectedPath = item.FullPath;
            Hide();
        }
    }

    private void OnListKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter && FileList.SelectedItem is FileItem item)
        {
            e.Handled = true;
            SelectedPath = item.FullPath;
            Hide();
        }
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
        await LoadFilesAsync();
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (FileList.SelectedItem is not FileItem item)
        {
            args.Cancel = true;
            return;
        }

        SelectedPath = item.FullPath;
    }
}