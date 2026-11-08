using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using IniParser.Model;

namespace DIEditor.Core
{
    /// <summary>
    /// desktop.ini 读取器，与 <see cref="DIGenerator"/> 互为逆操作。
    /// 声明即全限定名，避免 IniParser.Parser 命名空间缺失导致的编译错误。
    /// 本类无法映射到 DIContent 的节与键，会以原始纯文本形式保留在 DIContent.AdvancedSettings 中。
    /// </summary>
    public sealed class DIParser
    {
        public const string ShellClassInfoSection = ".ShellClassInfo";
        public const string LocalizedFileNamesSection = "LocalizedFileNames";

        private const string KeyLocalizedResourceName = "LocalizedResourceName";
        private const string KeyIconResource = "IconResource";
        private const string KeyInfoTip = "InfoTip";
        private const string KeyIconFile = "IconFile";     // Vista 之前的旧写法
        private const string KeyIconIndex = "IconIndex";

        /// <summary>统一换行符，UI 文本框与回写都用它。</summary>
        private const string NewLine = "\r\n";

        /// <summary>按 CRLF / LF / CR 统一切行。</summary>
        private static string[] SplitLines(string text)
            => text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        /// <summary>
        /// 键名能否当作真实键。用于排除两类脏数据：
        /// 一是被误当成键的节头残片（"[New]=1"），二是被压平进键名的多余内容。
        /// </summary>
        public static bool IsPlausibleKeyName(string keyName)
        {
            string key = (keyName ?? string.Empty).Trim();
            if (key.Length == 0) return false;
            if (key.IndexOf('[') >= 0 || key.IndexOf(']') >= 0) return false;
            if (key.IndexOf('=') >= 0) return false;
            if (key.IndexOf('\r') >= 0 || key.IndexOf('\n') >= 0) return false;
            return true;
        }


        /// <summary>本类能映射到 DIContent 的 .ShellClassInfo 键；其余键一律进高级设置。</summary>
        private static readonly HashSet<string> ShellClassInfoKeys =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                KeyLocalizedResourceName,
                KeyIconResource,
                KeyInfoTip,
                KeyIconFile,
                KeyIconIndex,
            };

        private static readonly Encoding Utf16LeWithBom = new UnicodeEncoding(false, true);
        private static Encoding _ansi;

        private readonly IniParser.Parser.IniDataParser _parser;

        public DIParser()
        {
            _parser = new IniParser.Parser.IniDataParser();

            var cfg = _parser.Configuration;
            cfg.CaseInsensitive = true;         // 默认 false，desktop.ini 的节名/键名并不区分大小写
            cfg.SkipInvalidLines = true;        // 非 [section] / key=value 的行直接跳过，不抛异常
            cfg.AllowDuplicateKeys = true;      // 重复键取最后一个值
            cfg.AllowDuplicateSections = true;  // 重复节合并
            cfg.AllowKeysWithoutSection = true; // 容忍节外的孤立键
        }

        /// <summary>最近一次解析得到的原始数据，用于保真回写。</summary>
        public IniData RawData { get; private set; } = new IniData();

        /// <summary>最近一次 Load 的绝对路径，Parse 时为 null。</summary>
        public string SourcePath { get; private set; }

        /// <summary>最近一次实际使用的编码。</summary>
        public Encoding LastEncoding { get; private set; } = Utf16LeWithBom;

        // ---------- 读取 ----------

        public DIContent Load(string path) => Load(path, null);

        /// <summary>从文件读取，encoding 传 null 表示自动探测。</summary>
        public DIContent Load(string path, Encoding encoding)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("路径不能为空", nameof(path));
            if (!File.Exists(path))
                throw new FileNotFoundException("找不到 desktop.ini", path);

            SourcePath = Path.GetFullPath(path);
            LastEncoding = encoding ?? DetectEncoding(SourcePath);

            // FileShare.ReadWrite | Delete：资源管理器/索引服务随时可能持有句柄
            using var fs = new FileStream(SourcePath, FileMode.Open, FileAccess.Read,
                                          FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs, LastEncoding,
                                                detectEncodingFromByteOrderMarks: true);
            return Parse(reader.ReadToEnd());
        }

        /// <summary>不抛异常的版本，失败时返回全空的 DIContent。</summary>
        public bool TryLoad(string path, out DIContent content, Encoding encoding = null)
        {
            try
            {
                content = Load(path, encoding);
                return true;
            }
            catch (Exception ex) when (ex is IOException
                                    || ex is UnauthorizedAccessException
                                    || ex is ArgumentException
                                    || ex is NotSupportedException)
            {
                content = new DIContent();
                return false;
            }
        }

        /// <summary>直接解析一段 ini 文本。</summary>
        public DIContent Parse(string iniText)
        {
            string text = iniText ?? string.Empty;
            RawData = _parser.Parse(text);

            DIContent content = ToContent(RawData);

            // 文本扫描比 IniData 重建更保真：注释、缩进、重复键、原始大小写都能留下
            string advanced = ExtractAdvancedSettings(text);
            content.AdvancedSettings = advanced;   // 可能为空串，交给 HasAdvancedSettings 判断

            return content;
        }

        /// <summary>IniData → DIContent。任何路径都不会返回 null，缺失字段保持 string.Empty。</summary>
        public static DIContent ToContent(IniData data)
        {
            var content = new DIContent();
            if (data == null) return content;

            SectionData shellInfo = FindSection(data, ShellClassInfoSection);
            if (shellInfo != null)
            {
                content.CustomName = GetValue(shellInfo, KeyLocalizedResourceName) ?? string.Empty;
                content.CustomTooltip = GetValue(shellInfo, KeyInfoTip) ?? string.Empty;

                string iconResource = GetValue(shellInfo, KeyIconResource);
                if (!string.IsNullOrWhiteSpace(iconResource))
                {
                    (string iconPath, int iconIndex) = SplitIconResource(iconResource);
                    content.CustomIconPath = iconPath;
                    content.CustomIconIndex = iconIndex;
                }

                // 兼容只有 IconFile / IconIndex 的旧格式
                if (string.IsNullOrEmpty(content.CustomIconPath))
                {
                    string iconFile = GetValue(shellInfo, KeyIconFile);
                    if (!string.IsNullOrWhiteSpace(iconFile))
                    {
                        content.CustomIconPath = Unquote(iconFile);
                        content.CustomIconIndex =
                            int.TryParse(GetValue(shellInfo, KeyIconIndex),
                                         NumberStyles.Integer,
                                         CultureInfo.InvariantCulture, out int idx) ? idx : 0;
                    }
                }
            }

            SectionData fileNames = FindSection(data, LocalizedFileNamesSection);
            if (fileNames != null)
            {
                foreach (KeyData keyData in fileNames.Keys)
                {
                    // 脏键不进 CustomFileName，它会由 BuildAdvancedSettings 送回高级设置
                    if (!IsPlausibleKeyName(keyData.KeyName)) continue;

                    string fileName = Unquote(keyData.KeyName);
                    if (string.IsNullOrEmpty(fileName)) continue;
                    content.CustomFileName[fileName] = keyData.Value ?? string.Empty;
                }
            }

            // 没有原始文本可用时，退化为从 IniData 重建（会丢注释与缩进）
            content.AdvancedSettings = BuildAdvancedSettings(data);

            return content;
        }

        // ---------- 识别范围判定 ----------

        /// <summary>该节是否整体被本类识别。</summary>
        private static bool IsSupportedSection(string sectionName)
        {
            string name = (sectionName ?? string.Empty).Trim();
            return string.Equals(name, ShellClassInfoSection, StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, LocalizedFileNamesSection, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>该键是否会被映射到 DIContent 的强类型字段。</summary>
        public static bool IsSupportedKey(string sectionName, string keyName)
        {
            string name = (sectionName ?? string.Empty).Trim();
            string key = (keyName ?? string.Empty).Trim();

            // 形如 "[xxx]" 的残片、含 "=" 或被压平的键名，一律不算受支持内容
            if (!IsPlausibleKeyName(key)) return false;

            // LocalizedFileNames 下任意“合法文件名”都是「文件名 → 显示名」映射
            if (string.Equals(name, LocalizedFileNamesSection, StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(name, ShellClassInfoSection, StringComparison.OrdinalIgnoreCase))
                return ShellClassInfoKeys.Contains(key);

            return false;   // 不认识的节里没有「受支持的键」
        }

        // ---------- 未识别内容 → 高级设置（纯文本） ----------

        /// <summary>
        /// 从原始 ini 文本中提取本类不认识的节与键，输出为纯文本。
        /// 未识别的整节连同节头、注释、空行、缩进一起原样保留；
        /// 已识别节中未识别的键按 "key=value" 归在该节名下；节外孤立键单独成块。
        /// </summary>
        public static string ExtractAdvancedSettings(string iniText)
        {
            if (string.IsNullOrEmpty(iniText)) return string.Empty;

            var sb = new StringBuilder();
            var pendingKeys = new List<string>();   // 已识别节里待写出的未知键行
            var trivia = new List<string>();        // 归属未定的注释/空行

            string section = null;                  // null 表示位于第一个节头之前
            bool wholeSectionUnknown = false;

            foreach (string rawLine in SplitLines(iniText))
            {
                string line = rawLine.TrimEnd();    // 只裁行尾，保留行首缩进
                string trimmed = line.Trim();

                // 空行与注释
                if (trimmed.Length == 0
                    || trimmed[0] == ';'
                    || trimmed[0] == '#')
                {
                    if (wholeSectionUnknown && section != null)
                        sb.Append(line).Append(NewLine);   // 未识别节内：原样保留
                    else
                        trivia.Add(line);                  // 先挂起，等下一行定性
                    continue;
                }

                // 节头
                if (trimmed[0] == '[' && trimmed[trimmed.Length - 1] == ']')
                {
                    FlushPendingBlock(sb, section, pendingKeys);
                    trivia.Clear();

                    section = trimmed.Substring(1, trimmed.Length - 2).Trim();
                    wholeSectionUnknown = !IsSupportedSection(section);

                    if (wholeSectionUnknown)
                        AppendSectionHeader(sb, section);
                    continue;
                }

                // 键值行
                int eq = trimmed.IndexOf('=');
                if (eq <= 0)
                {
                    // 既不是节头也不是键值对：未识别节里原样保留，否则视为噪声丢弃
                    if (wholeSectionUnknown && section != null)
                        sb.Append(line).Append(NewLine);
                    else
                        trivia.Clear();
                    continue;
                }

                string key = trimmed.Substring(0, eq).Trim();

                // "[New]=1" 这种行：行首是 [ 但行尾不是 ]，属于残缺节头，
                // 既不能当节头，也不能当受支持的键，原样留在高级设置里让用户自己修
                if (trimmed[0] == '[')
                {
                    FlushPendingBlock(sb, section, pendingKeys);
                    trivia.Clear();
                    AppendRawLine(sb, line);
                    continue;
                }

                if (wholeSectionUnknown && section != null)
                {
                    sb.Append(line).Append(NewLine);       // 整节未知，逐行搬运
                }
                else if (!IsSupportedKey(section, key))
                {
                    if (trivia.Count > 0)                  // 紧邻的注释一并带上
                    {
                        pendingKeys.AddRange(trivia);
                        trivia.Clear();
                    }
                    pendingKeys.Add(line);
                }
                else
                {
                    trivia.Clear();                        // 属于已知键，注释不搬
                }
            }

            FlushPendingBlock(sb, section, pendingKeys);

            return sb.ToString().TrimEnd('\r', '\n');
        }

        /// <summary>原样输出一行独立文本，块与块之间留空行。</summary>
        private static void AppendRawLine(StringBuilder sb, string line)
        {
            if (sb.Length > 0) sb.Append(NewLine);
            sb.Append(line).Append(NewLine);
        }

        /// <summary>把已识别节里攒下的未知键写成一个 [节名] 块。</summary>
        private static void FlushPendingBlock(StringBuilder sb, string section,
                                              List<string> pendingKeys)
        {
            if (pendingKeys.Count == 0) return;

            if (!string.IsNullOrEmpty(section))
                AppendSectionHeader(sb, section);          // 节外孤立键不写节头

            foreach (string line in pendingKeys)
                sb.Append(line).Append(NewLine);

            pendingKeys.Clear();
        }

        /// <summary>
        /// 从 IniData 重建高级设置文本，供 <see cref="ToContent"/> 单独调用时使用。
        /// 这条路会丢注释、缩进与重复键，只在拿不到原始文本时作为兜底。
        /// </summary>
        public static string BuildAdvancedSettings(IniData data)
        {
            if (data == null) return string.Empty;

            var sb = new StringBuilder();

            // 节外的孤立键先输出
            foreach (KeyData key in data.Global)
                sb.Append(FormatKeyLine(key)).Append(NewLine);

            foreach (SectionData section in data.Sections)
            {
                string name = (section.SectionName ?? string.Empty).Trim();

                if (!IsSupportedSection(name))
                {
                    AppendSectionHeader(sb, name);
                    foreach (KeyData key in section.Keys)
                        sb.Append(FormatKeyLine(key)).Append(NewLine);
                    continue;
                }

                bool headerWritten = false;
                foreach (KeyData key in section.Keys)
                {
                    if (IsSupportedKey(name, key.KeyName)) continue;

                    if (!headerWritten)
                    {
                        AppendSectionHeader(sb, name);
                        headerWritten = true;
                    }
                    sb.Append(FormatKeyLine(key)).Append(NewLine);
                }
            }

            return sb.ToString().TrimEnd('\r', '\n');
        }

        /// <summary>把一个 KeyData 还原成 "key=value" 纯文本行。</summary>
        private static string FormatKeyLine(KeyData key)
            => (key.KeyName ?? string.Empty).Trim() + "=" + (key.Value ?? string.Empty);

        private static void AppendSectionHeader(StringBuilder sb, string sectionName)
        {
            if (sb.Length > 0) sb.Append(NewLine);   // 块之间留一个空行
            sb.Append('[').Append((sectionName ?? string.Empty).Trim()).Append(']').Append(NewLine);
        }

        // ---------- 高级设置 → IniData（写回前合并） ----------

        /// <summary>
        /// 把高级设置文本合并回 IniData：先清掉旧的未识别节与键，再按文本内容重新并入。
        /// 用户在文本框里删掉一段，就等于真的把这段从 desktop.ini 里删掉。
        /// KeyDataCollection 没有 Clear() / Keys，这里只用 RemoveKey / AddKey / ContainsKey。
        /// </summary>
        public static void ApplyAdvancedSettings(IniData data, string advancedText)
        {
            if (data == null) return;

            // 1. 清除上一次解析出的未识别内容
            var sectionsToRemove = new List<string>();

            foreach (SectionData section in data.Sections)
            {
                string name = (section.SectionName ?? string.Empty).Trim();

                if (!IsSupportedSection(name))
                {
                    sectionsToRemove.Add(section.SectionName);
                    continue;
                }

                var keysToRemove = new List<string>();
                foreach (KeyData key in section.Keys)
                {
                    if (string.IsNullOrWhiteSpace(key.KeyName)) continue;
                    if (!IsSupportedKey(name, key.KeyName)) keysToRemove.Add(key.KeyName);
                }

                foreach (string keyName in keysToRemove)
                    section.Keys.RemoveKey(keyName);   // 先收集再删，避免边遍历边改
            }

            foreach (string name in sectionsToRemove)
                data.Sections.RemoveSection(name);

            // 节外孤立键同样整体清空
            if (data.Global != null)
            {
                var globalKeys = new List<string>();
                foreach (KeyData key in data.Global)
                    if (!string.IsNullOrWhiteSpace(key.KeyName)) globalKeys.Add(key.KeyName);

                foreach (string keyName in globalKeys)
                    data.Global.RemoveKey(keyName);
            }

            // 2. 高级设置为空，说明用户把额外内容都删干净了
            if (string.IsNullOrWhiteSpace(advancedText)) return;

            // 3. 用与 Load 完全相同的配置解析高级设置文本
            IniData advanced = CreateParser().Parse(advancedText);

            foreach (SectionData section in advanced.Sections)
            {
                string name = (section.SectionName ?? string.Empty).Trim();
                if (name.Length == 0) continue;

                SectionData target = FindOrAddSection(data, name);
                if (target == null) continue;

                bool supportedSection = IsSupportedSection(name);

                foreach (KeyData key in section.Keys)
                {
                    if (string.IsNullOrWhiteSpace(key.KeyName)) continue;

                    // 受支持节里的受支持键由强类型字段负责，这里让位，避免两处打架
                    if (supportedSection && IsSupportedKey(name, key.KeyName)) continue;

                    UpsertKey(target.Keys, key.KeyName, key.Value);
                }
            }

            // 4. 节外孤立键写回 Global
            if (data.Global != null)
            {
                foreach (KeyData key in advanced.Global)
                {
                    if (string.IsNullOrWhiteSpace(key.KeyName)) continue;
                    UpsertKey(data.Global, key.KeyName, key.Value);
                }
            }
        }

        /// <summary>与构造函数里那套配置保持一致，供合并时复用。</summary>
        private static IniParser.Parser.IniDataParser CreateParser()
        {
            var parser = new IniParser.Parser.IniDataParser();
            var cfg = parser.Configuration;

            cfg.CaseInsensitive = true;
            cfg.SkipInvalidLines = true;
            cfg.AllowDuplicateKeys = true;
            cfg.AllowDuplicateSections = true;
            cfg.AllowKeysWithoutSection = true;

            return parser;
        }

        /// <summary>按名字找节，找不到就新建，全程不用 SectionDataCollection 的索引器。</summary>
        private static SectionData FindOrAddSection(IniData data, string sectionName)
        {
            SectionData section = FindSection(data, sectionName);
            if (section != null) return section;

            data.Sections.AddSection(sectionName);
            return FindSection(data, sectionName);
        }

        /// <summary>写入或覆盖一个键，只用 ContainsKey / RemoveKey / AddKey 三个成员。</summary>
        private static void UpsertKey(KeyDataCollection keys, string keyName, string value)
        {
            string name = (keyName ?? string.Empty).Trim();
            if (name.Length == 0) return;

            if (keys.ContainsKey(name))
                keys.RemoveKey(name);

            keys.AddKey(name, value);
        }


        // ---------- 写回 ----------

        /// <summary>基于 RawData 写回，未触碰的节与键原样保留。</summary>
        public void Save(IniData data = null, Encoding encoding = null)
        {
            if (string.IsNullOrEmpty(SourcePath))
                throw new InvalidOperationException("必须先通过 Load 加载文件才能写回");

            IniData target = data ?? RawData;
            Encoding enc = encoding ?? LastEncoding;

            // IniData.ToString() 重新序列化整个文件（保留注释，丢失空行与缩进）
            File.WriteAllText(SourcePath, target.ToString(), enc);
        }

        /// <summary>
        /// 带高级设置合并的写回。DIGenerator 生成强类型字段后调用它，
        /// 用户没动过的高级设置会原样保留，动过的以文本为准。
        /// </summary>
        public void Save(DIContent content, IniData data = null, Encoding encoding = null)
        {
            IniData target = data ?? RawData;
            if (content != null)
                ApplyAdvancedSettings(target, content.AdvancedSettings);

            Save(target, encoding);
        }

        // ---------- 编码 ----------

        /// <summary>当前系统的 ANSI 编码（中文系统为 GBK/936）。</summary>
        public static Encoding AnsiEncoding
        {
            get
            {
                if (_ansi != null) return _ansi;

#if NETCOREAPP
                // .NET 6+ 默认只带 UTF-8 / Unicode，非 Unicode 代码页必须显式注册
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
#endif
                int codePage = CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
                if (codePage <= 0) codePage = 1252;   // invariant culture 兜底

                _ansi = Encoding.GetEncoding(codePage,
                                             EncoderFallback.ReplacementFallback,
                                             DecoderFallback.ReplacementFallback);
                return _ansi;
            }
        }

        /// <summary>desktop.ini 只可能是「带 BOM 的 UTF-16 LE」或「ANSI」。</summary>
        public static Encoding DetectEncoding(string path)
        {
            // 文件不存在时，后续保存用 Unicode，避免中文受代码页影响
            if (!File.Exists(path)) return Utf16LeWithBom;

            Span<byte> head = stackalloc byte[2];
            int read;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                                           FileShare.ReadWrite | FileShare.Delete))
                read = fs.Read(head);

            if (read == 2 && head[0] == 0xFF && head[1] == 0xFE)
                return Utf16LeWithBom;

            // 不做 UTF-8 / 无 BOM UTF-16 判断：desktop.ini 不接受这两种形式
            return AnsiEncoding;
        }

        /// <summary>判断一段文本能否无损写入 ANSI 文件，用于 UI 提前提示。</summary>
        public static bool CanEncodeAsAnsi(string text)
        {
            if (string.IsNullOrEmpty(text)) return true;
            try
            {
                int codePage = CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
                Encoding strict = Encoding.GetEncoding(codePage <= 0 ? 1252 : codePage,
                                                       EncoderFallback.ExceptionFallback,
                                                       DecoderFallback.ExceptionFallback);
                strict.GetByteCount(text);
                return true;
            }
            catch (EncoderFallbackException)
            {
                return false;
            }
        }

        // ---------- 内部工具 ----------

        private static SectionData FindSection(IniData data, string name)
        {
            foreach (SectionData section in data.Sections)
            {
                if (string.Equals(section.SectionName?.Trim(), name,
                                  StringComparison.OrdinalIgnoreCase))
                    return section;
            }
            return null;
        }

        private static string GetValue(SectionData section, string key)
        {
            if (section == null) return null;
            foreach (KeyData keyData in section.Keys)
            {
                if (string.Equals(keyData.KeyName?.Trim(), key,
                                  StringComparison.OrdinalIgnoreCase))
                    return keyData.Value;
            }
            return null;
        }

        /// <summary>把 "C:\Icons\app.ico,3" 拆成路径与索引，兼容路径内含逗号、负索引、带引号。</summary>
        public static (string Path, int Index) SplitIconResource(string raw)
        {
            string value = (raw ?? string.Empty).Trim();
            if (value.Length == 0) return (string.Empty, 0);

            int comma = value.LastIndexOf(',');
            if (comma > 0)
            {
                string tail = value.Substring(comma + 1).Trim();
                if (int.TryParse(tail, NumberStyles.Integer,
                                 CultureInfo.InvariantCulture, out int index))
                    return (Unquote(value.Substring(0, comma)), index);
            }

            return (Unquote(value), 0);   // 没有合法索引，整串当路径
        }

        /// <summary>值是否是 "@shell32.dll,-12690" 形式的资源引用。</summary>
        public static bool IsResourceReference(string value)
            => !string.IsNullOrWhiteSpace(value)
               && value.TrimStart().StartsWith("@", StringComparison.Ordinal);

        private static string Unquote(string value)
        {
            string s = (value ?? string.Empty).Trim();
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
                return s.Substring(1, s.Length - 2);
            return s;
        }
    }
}
