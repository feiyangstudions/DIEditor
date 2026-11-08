namespace DIEditor.Core
{
    public interface IDIContent
    {
        public string CustomName { get; set; }
        public bool HasCustomName { get
            {
                if (CustomName == string.Empty || CustomName == null) return false;
                else return true;
            } }
        public string CustomIconPath { get; set; }
        public bool HasCustomIcon { get
            {
                if (CustomIconPath == string.Empty || CustomIconPath == null) return false;
                else return true;
            } }
        public int CustomIconIndex { get; set; }
        public string CustomTooltip { get; set; }
        public string AdvancedSettings { get; set; }
        public bool HasCustomTooltip
        {
            get
            {
                if (CustomTooltip == string.Empty || CustomTooltip == null) return false;
                else return true;
            }
        }
        public Dictionary<string, string> CustomFileName { get; set; }
        public bool HasCustomFileNameSet
        {
            get
            {
                if (CustomFileName.Count == 0 || CustomFileName == null) return false;
                else return true;
            }
        }
        public bool HasAdvancedSettings
        {
            get
            {
                if (AdvancedSettings == string.Empty || AdvancedSettings == null) return false;
                else return true;
            }
        }
        public bool SystemFileAttribDisabled { get; set; }
        public bool HiddenFileAttribDisabled { get; set; }
    }
}
