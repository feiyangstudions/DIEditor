using System;
using System.Globalization;
using System.IO;
using System.Text;
using IniParser.Model;

namespace DIEditor.Core
{
    /// <summary>
    /// desktop.ini 读取器，与 <see cref="DIGenerator"/> 互为逆操作。
    /// 声明即全限定名，避免 IniParser.Parser 命名空间缺失导致的编译错误。
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
            RawData = _parser.Parse(iniText ?? string.Empty);
            return ToContent(RawData);
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
                    string fileName = Unquote(keyData.KeyName);
                    if (string.IsNullOrEmpty(fileName)) continue;
                    content.CustomFileName[fileName] = keyData.Value ?? string.Empty;
                }
            }

            return content;
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
