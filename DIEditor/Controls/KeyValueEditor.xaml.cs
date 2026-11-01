using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using DIEditor.Dialogs;
using DIEditor.Localization;
using Windows.Storage.Pickers;
using WinRT.Interop;
using FToolkit.WinUI3.Control.Attributes;

namespace DIEditor.Controls;

/// <summary>
/// 键值对编辑器。每行：浏览… | 键 | 竖线 | 值 | 删除。
/// 对外通过 ToDictionary / TryToDictionary 转成 Dictionary&lt;string, string&gt;。
/// </summary>
public sealed partial class KeyValueEditor : UserControl
{
    // ===================================================================
    // 1. 数据源
    // ===================================================================

    public ObservableCollection<KeyValueItem> Items
    {
        get => (ObservableCollection<KeyValueItem>)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public static readonly DependencyProperty ItemsProperty =
        DependencyProperty.Register(
            nameof(Items),
            typeof(ObservableCollection<KeyValueItem>),
            typeof(KeyValueEditor),
            new PropertyMetadata(null, OnItemsPropertyChanged));

    private static void OnItemsPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var editor = (KeyValueEditor)d;

        if (e.OldValue is ObservableCollection<KeyValueItem> oldItems)
        {
            oldItems.CollectionChanged -= editor.OnItemsCollectionChanged;
            foreach (var item in oldItems)
            {
                item.PropertyChanged -= editor.OnItemPropertyChanged;
            }
        }

        if (e.NewValue is ObservableCollection<KeyValueItem> newItems)
        {
            newItems.CollectionChanged += editor.OnItemsCollectionChanged;
            foreach (var item in newItems)
            {
                item.PropertyChanged += editor.OnItemPropertyChanged;
            }
        }

        editor.RefreshStatus();
        editor.DictionaryChanged?.Invoke(editor, EventArgs.Empty);
    }

    /// <summary>删掉最后一条后是否自动补一条空白行。</summary>
    public bool KeepAtLeastOneItem { get; set; } = true;

    // ===================================================================
    // 2. 浏览行为配置
    // ===================================================================

    /// <summary>
    /// 浏览按钮锁定的目录。
    /// 有值：用 LockedFolderPickerDialog，只能挑该目录下的直接子文件；
    /// 为空：退化为系统 FileOpenPicker，可选任意位置的文件。
    /// </summary>
    public string? BrowseFolder { get; set; }

    /// <summary>
    /// 浏览时允许的扩展名（带不带点都行，内部会规范化）。
    /// 空集合表示不限制。
    /// </summary>
    public IReadOnlyList<string> BrowseExtensions { get; set; } = Array.Empty<string>();

    /// <summary>
    /// 防止对话框重入：WinUI 同一时刻只允许一个 ContentDialog，
    /// 连点两次 “浏览…” 会抛 “Only a single ContentDialog can be open”。
    /// </summary>
    private bool _isPicking;

    // ===================================================================
    // 3. 对外事件
    // ===================================================================

    public event EventHandler? ItemsChanged;

    public event EventHandler? DictionaryChanged;

    // ===================================================================
    // 4. 构造函数
    // ===================================================================

    public KeyValueEditor()
    {
        // 必须在 InitializeComponent 之前赋值：x:Bind 的首次求值发生在它内部
        Items = new ObservableCollection<KeyValueItem>();

        InitializeComponent();

        AddItem();
    }

    // ===================================================================
    // 5. 集合与元素变化
    // ===================================================================

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (KeyValueItem item in e.OldItems)
            {
                item.PropertyChanged -= OnItemPropertyChanged;
            }
        }

        if (e.NewItems != null)
        {
            foreach (KeyValueItem item in e.NewItems)
            {
                item.PropertyChanged += OnItemPropertyChanged;
            }
        }

        // 行数变化会改变“键是否重复”的判定，必须全量重算
        RevalidateAll();
        RefreshStatus();
        ItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // ValidationMessage 是本控件自己写回的，响应它会形成无限递归
        if (e.PropertyName == nameof(KeyValueItem.ValidationMessage))
        {
            return;
        }

        RevalidateAll();
        RefreshStatus();
        DictionaryChanged?.Invoke(this, EventArgs.Empty);
    }

    // ===================================================================
    // 6. 增删行
    // ===================================================================

    public KeyValueItem AddItem(string key = "", string value = "")
    {
        EnsureItems();

        var item = new KeyValueItem { Key = key, Value = value };
        Items.Add(item);
        FocusItemKeyBox(item);
        return item;
    }

    public void RemoveItem(KeyValueItem item)
    {
        if (Items == null || !Items.Contains(item))
        {
            return;
        }

        Items.Remove(item);

        if (KeepAtLeastOneItem && Items.Count == 0)
        {
            AddItem();
        }
    }

    public void ClearItems()
    {
        if (Items == null)
        {
            return;
        }

        // Reset 动作的 OldItems 为 null，回调里无法逐个退订，所以先手动退订
        foreach (var item in Items)
        {
            item.PropertyChanged -= OnItemPropertyChanged;
        }

        Items.Clear();

        if (KeepAtLeastOneItem)
        {
            AddItem();
        }
    }

    // ===================================================================
    // 7. 按钮事件
    // ===================================================================

    private void AddItem_Click(object sender, RoutedEventArgs e) => AddItem();

    private void DeleteItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not KeyValueItem item)
        {
            return;
        }

        RemoveItem(item);
    }

    /// <summary>
    /// 行内的“浏览…”按钮：选一个文件，把“文件名 + 扩展名”填进本行的键框。
    /// </summary>
    private async void BrowseItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: KeyValueItem item })
        {
            return;
        }

        if (_isPicking)
        {
            return;
        }

        _isPicking = true;
        try
        {
            var path = string.IsNullOrWhiteSpace(BrowseFolder)
                ? await PickWithSystemDialogAsync()
                : await PickWithLockedDialogAsync();

            if (string.IsNullOrEmpty(path))
            {
                // 用户取消：什么都不动，值框尤其不能被清掉
                return;
            }

            // 只取文件名和扩展名，不含任何目录部分；值框保持原样
            item.Key = Path.GetFileName(path);

            // Key 赋值会触发 PropertyChanged → RevalidateAll，
            // 因此“是否与其它行重复”的红字提示会自动出现，这里无需再手动校验
            FocusItemValueBox(item);
        }
        finally
        {
            _isPicking = false;
        }
    }

    /// <summary>
    /// 路径一：锁定目录对话框。保留“只能选该目录直接子文件”的强约束。
    /// 注意必须设置 XamlRoot，否则在 XAML 岛 / UserControl 内 ShowAsync 会抛异常。
    /// </summary>
    private async Task<string?> PickWithLockedDialogAsync()
    {
        var dialog = new LockedFolderPickerDialog(BrowseFolder!, BrowseExtensions)
        {
            XamlRoot = XamlRoot,
            // ① 原来写死的 "选择文件" 改为资源键。
            //    对话框标题不是依赖属性，x:Uid 够不着，只能代码取词。
            Title = L.Get("KVE_PickFileDialogTitle"),
        };

        var result = await dialog.ShowAsync();

        return result == ContentDialogResult.Primary && !string.IsNullOrEmpty(dialog.SelectedPath)
            ? dialog.SelectedPath
            : null;
    }

    /// <summary>
    /// 路径二：系统文件选择器。适合没有固定目录约束的场景。
    /// </summary>
    private async Task<string?> PickWithSystemDialogAsync()
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };

        // WinUI 3 的 Picker 是桌面应用形态，不继承窗口上下文，必须显式绑定 HWND，
        // 否则 PickSingleFileAsync 直接抛 “COMException / 无法确定窗口”
        InitializeWithWindow.Initialize(picker, GetWindowHandle());

        foreach (var ext in NormalizeExtensions(BrowseExtensions))
        {
            picker.FileTypeFilter.Add(ext);
        }

        // 没有任何过滤条件时必须给通配符，空过滤器会导致对话框不可用
        if (picker.FileTypeFilter.Count == 0)
        {
            picker.FileTypeFilter.Add("*");
        }

        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    /// <summary>把 "json" / ".json" 统一成 ".json"。</summary>
    private static IEnumerable<string> NormalizeExtensions(IEnumerable<string>? raw)
    {
        if (raw == null)
        {
            yield break;
        }

        foreach (var e in raw)
        {
            if (string.IsNullOrWhiteSpace(e))
            {
                continue;
            }

            var value = e.Trim().ToLowerInvariant();
            yield return value.StartsWith('.') ? value : "." + value;
        }
    }

    /// <summary>
    /// 从当前控件的 XamlRoot 反推窗口句柄，避免依赖 App.MainWindow 这样的全局变量。
    /// 需要 Windows App SDK 1.4 及以上；更低版本请改为
    /// WindowNative.GetWindowHandle(App.MainWindow)。
    /// </summary>
    private nint GetWindowHandle()
    {
        var windowId = XamlRoot.ContentIslandEnvironment.AppWindowId;
        return Win32Interop.GetWindowFromWindowId(windowId);
    }

    // ===================================================================
    // 8. 校验
    // ===================================================================

    private void RevalidateAll()
    {
        if (Items == null)
        {
            return;
        }

        // 比较器与 ToDictionary 保持一致，否则会出现“界面无错、转换抛异常”
        var keyCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var item in Items)
        {
            var key = item.Key.Trim();
            if (key.Length == 0)
            {
                continue;
            }

            keyCounts[key] = keyCounts.TryGetValue(key, out var count) ? count + 1 : 1;
        }

        foreach (var item in Items)
        {
            var key = item.Key.Trim();

            if (key.Length == 0)
            {
                // ② 只填了值没填键才算错，两栏都空属于“待填”的正常态
                item.ValidationMessage = item.Value.Length == 0
                    ? string.Empty
                    : L.Get("KVE_ErrKeyRequired");
            }
            else if (keyCounts[key] > 1)
            {
                // ③ 带变量：必须 Format，不能字符串拼接
                item.ValidationMessage = L.Format("KVE_ErrKeyDuplicate", key);
            }
            else
            {
                item.ValidationMessage = string.Empty;
            }
        }
    }

    public bool HasErrors => Items != null && Items.Any(i => i.HasError);

    // ===================================================================
    // 9. 转字典
    // ===================================================================

    public Dictionary<string, string> ToDictionary()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        if (Items == null)
        {
            return result;
        }

        foreach (var item in Items)
        {
            if (item.IsEmpty)
            {
                continue;
            }

            var key = item.Key.Trim();

            if (key.Length == 0)
            {
                // ④ 异常消息同样本地化：它会通过 TryToDictionary 的 out 参数
                //    显示到界面上，属于用户可见文本。
                throw new InvalidOperationException(L.Get("KVE_ErrValueWithoutKey"));
            }

            if (!result.TryAdd(key, item.Value))
            {
                throw new ArgumentException(
                    L.Format("KVE_ErrKeyDuplicateToDict", key), nameof(Items));
            }
        }

        return result;
    }

    public bool TryToDictionary(out Dictionary<string, string>? result, out string? error)
    {
        RevalidateAll();

        var invalid = Items?.FirstOrDefault(i => i.HasError);
        if (invalid != null)
        {
            result = null;
            // ⑤ 嵌套格式化：外层给“第几行”，内层已是完整句子。
            //    不要用 $"第 {n} 行：" + msg，语序在日文里会反。
            error = L.Format("KVE_ErrRowPrefix", Items!.IndexOf(invalid) + 1, invalid.ValidationMessage);
            return false;
        }

        try
        {
            result = ToDictionary();
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            result = null;
            error = ex.Message;
            return false;
        }
    }

    public bool TryToDictionary(out Dictionary<string, string>? result)
        => TryToDictionary(out result, out _);

    public void LoadFromDictionary(IDictionary<string, string> source)
    {
        EnsureItems();
        ClearItems();   // KeepAtLeastOneItem 为 true 时会先塞一条空白行

        // 摘掉这条占位空白行，否则它会停在第一行，真实数据全被挤到下面
        var placeholder = Items!.FirstOrDefault(i => i.IsEmpty);
        if (placeholder != null)
        {
            Items.Remove(placeholder);
        }

        if (source is not null)
        {
            foreach (var pair in source)
            {
                // 直接 Items.Add，不用 AddItem：批量载入不该逐行抢焦点
                Items.Add(new KeyValueItem { Key = pair.Key, Value = pair.Value });
            }
        }

        // 末尾补一条空白行，方便继续追加
        if (KeepAtLeastOneItem && (Items.Count == 0 || Items.All(i => !i.IsEmpty)))
        {
            Items.Add(new KeyValueItem());
        }
    }

    // ===================================================================
    // 10. 界面联动
    // ===================================================================

    private void EnsureItems()
    {
        if (Items == null)
        {
            Items = new ObservableCollection<KeyValueItem>();
        }
    }

    private void RefreshStatus()
    {
        // 构造函数里 Items 赋值早于 InitializeComponent，那时该元素还不存在
        if (StatusTextBlock == null)
        {
            return;
        }

        var total = Items?.Count ?? 0;
        var filled = Items?.Count(i => !i.IsEmpty) ?? 0;

        // ⑥ 带两个占位符，交给资源文件决定语序
        StatusTextBlock.Text = L.Format("KVE_StatusFormat", total, filled);
    }

    /// <summary>
    /// 语言切换后由宿主调用，重新生成所有“代码侧”文案：
    /// 校验红字、状态栏计数、以及后续打开对话框的标题。
    ///
    /// 之所以必须显式调用：x:Uid 的文案在 XAML 解析时就已写入属性，
    /// 是“一次性”的；而本控件有一半文案是运行时算出来的。
    /// 两者刷新时机不同步，不调这个方法就会出现
    /// “按钮变成英文、红字还是中文”的割裂状态。
    ///
    /// 注意本方法不会刷新 XAML 里的 x:Uid 文案——
    /// 那需要重建控件或重新导航到页面。
    /// </summary>
    public void Relocalize()
    {
        // ① 校验逻辑本身与语言无关，重算即可得到新语言的句子；
        //    因为 SetProperty 只在值变化时才通知，换语言后文本必然不同，
        //    绑定会照常刷新。
        RevalidateAll();

        // ② 状态栏同理
        RefreshStatus();
    }

    /// <summary>让某行的“键”框获得焦点（新加行时用）。</summary>
    private void FocusItemKeyBox(KeyValueItem item) => FocusNthTextBox(item, 0);

    /// <summary>让某行的“值”框获得焦点（浏览填充完成后用，接着就能敲值）。</summary>
    private void FocusItemValueBox(KeyValueItem item) => FocusNthTextBox(item, 1);

    /// <summary>
    /// ItemsControl 延迟生成容器，刚改完数据时容器还不存在，
    /// 所以排到下一帧再找控件。
    /// </summary>
    private void FocusNthTextBox(KeyValueItem item, int index)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (RowItemsControl.ContainerFromItem(item) is not DependencyObject container)
            {
                return;
            }

            var boxes = FindDescendants<TextBox>(container).ToList();
            if (index < boxes.Count)
            {
                boxes[index].Focus(FocusState.Programmatic);
            }
        });
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in FindDescendants<T>(child))
            {
                yield return nested;
            }
        }
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
        => FindDescendants<T>(root).FirstOrDefault();
}
