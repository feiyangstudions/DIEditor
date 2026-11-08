namespace DIEditor.Core
{
    public sealed class DIContent : IDIContent
    {
        public string CustomName { get; set; } = string.Empty;
        public string CustomIconPath { get; set; } = string.Empty;
        public int CustomIconIndex { get; set; } = 0;
        public string CustomTooltip { get; set; } = string.Empty;
        public Dictionary<string, string> CustomFileName { get; set; }
            = new Dictionary<string, string>();
        public string AdvancedSettings { get; set; } = string.Empty;
        public bool HasCustomName => !string.IsNullOrEmpty(CustomName);
        public bool HasCustomIcon => !string.IsNullOrEmpty(CustomIconPath);
        public bool HasCustomTooltip => !string.IsNullOrEmpty(CustomTooltip);
        public bool HasCustomFileNameSet => CustomFileName is { Count: > 0 };
        public bool SystemFileAttribDisabled { get; set; } = false;
        public bool HiddenFileAttribDisabled { get; set; } = false;
        public bool HasAdvancedSettings => !string.IsNullOrEmpty(AdvancedSettings);
    }
}
