using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Windows.ApplicationModel.Resources;

namespace DIEditor.Localization;

/// <summary>
/// 全应用统一的取词入口。
/// 基于 MRT Core：构造 ResourceManager 只传 .pri 文件名，
/// 资源子树（Resources）通过 MainResourceMap.GetSubtree 指定。
/// </summary>
public static class L
{
    // ① 与 <resw 所在文件夹> 同名。Strings\zh-CN\Resources.resw → "Resources"。
    //    若你的 resw 直接放在项目根目录（不带 Strings 层级），这里同样写 "Resources"。
    private const string SubtreeName = "Resources";

    // ② ResourceMap 依赖 ResourceManager 的生存期，必须一起存成静态字段，
    //    否则管理器被回收后取词会失败。
    private static readonly ResourceManager Manager;
    private static readonly ResourceMap RootMap;
    private static readonly ResourceMap? Subtree;

    static L()
    {
        Manager = new ResourceManager(ResolvePriFile());   // 单参构造
        RootMap = Manager.MainResourceMap;

        // ③ GetSubtree 在子树不存在时会抛异常，这里降级为 null，
        //    让 Lookup 退回根映射继续尝试，避免整个静态构造失败。
        try
        {
            Subtree = RootMap.GetSubtree(SubtreeName);
        }
        catch
        {
            Subtree = null;
        }
    }

    /// <summary>
    /// 找出可用的 PRI 文件。构建产物的名字可能是 resources.pri，
    /// 也可能是与程序集同名的 xxx.pri，直接写死会静默查不到任何键。
    /// </summary>
    private static string ResolvePriFile()
    {
        var dir = AppContext.BaseDirectory;

        var preferred = Path.Combine(dir, "resources.pri");
        if (File.Exists(preferred))
        {
            return preferred;
        }

        var asmName = (Assembly.GetEntryAssembly() ?? typeof(L).Assembly).GetName().Name;
        var byAssembly = Path.Combine(dir, $"{asmName}.pri");
        if (File.Exists(byAssembly))
        {
            return byAssembly;
        }

        // ④ 兜底：拿目录里第一个 PRI。多 PRI 场景下顺序不确定，
        //    但总比抛“文件不存在”导致的全面失效要好，也方便你在调试时看出问题。
        var any = Directory.EnumerateFiles(dir, "*.pri", SearchOption.TopDirectoryOnly)
                           .FirstOrDefault();
        return any ?? preferred;
    }

    /// <summary>在当前语言下查找一条资源，找不到返回 null。</summary>
    private static string? Lookup(string key)
    {
        // ⑤ 先按子树查（键形如 KVE_AddLabel），
        //    再退回根映射查（键形如 Resources/KVE_AddLabel）。
        if (Subtree != null && TryGet(Subtree, key, out var fromSubtree))
        {
            return fromSubtree;
        }

        if (TryGet(RootMap, key, out var fromRoot))
        {
            return fromRoot;
        }

        if (TryGet(RootMap, $"{SubtreeName}/{key}", out var prefixed))
        {
            return prefixed;
        }

        return null;
    }

    /// <summary>
    /// 包一层 try/catch：GetValue 在未命中时会抛 COMException，
    /// 用它判断“有没有这条键”比依赖 TryGetValue 的版本差异更稳。
    /// </summary>
    private static bool TryGet(ResourceMap map, string key, out string? value)
    {
        try
        {
            value = map.GetValue(key)?.ValueAsString;
            return !string.IsNullOrEmpty(value);
        }
        catch
        {
            value = null;
            return false;
        }
    }

    /// <summary>取一条不需要格式化的文案。</summary>
    public static string Get(string key)
    {
        var value = Lookup(key);

#if DEBUG
        // ⑥ 只在 Debug 下抛：Release 里宁可显示键名也不要崩，
        //    开发期则必须立刻炸出来，否则漏翻会一直藏到界面上。
        if (string.IsNullOrEmpty(value))
        {
            throw new InvalidOperationException(
                $"缺少资源键：{key}（PRI：{ResolvePriFile()}，子树：{SubtreeName}）");
        }
#endif

        // ⑦ 兜底返回键名，界面上能一眼看出哪条没翻译。
        return string.IsNullOrEmpty(value) ? key : value;
    }

    /// <summary>取一条带占位符的文案。不要用字符串插值再翻译。</summary>
    public static string Format(string key, params object?[] args)
        => string.Format(Get(key), args);
}
