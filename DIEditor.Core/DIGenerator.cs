using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DIEditor.Core
{
    public static class DIGenerator
    {
        public static string Generate(IDIContent content)
        {
            StringBuilder sb = new StringBuilder();
            // desktop.ini中的.ShellClassInfo类
            if (content.HasCustomName || content.HasCustomIcon || content.HasCustomTooltip) sb.AppendLine("[.ShellClassInfo]");
            if (content.HasCustomName) sb.AppendLine($"LocalizedResourceName={content.CustomName}");
            if (content.HasCustomIcon) sb.AppendLine($"IconResource={content.CustomIconPath},{content.CustomIconIndex}");
            if (content.HasCustomTooltip) sb.AppendLine($"InfoTip={content.CustomTooltip}");
            // desktop.ini中的LocalizedFileNames类
            if (content.HasCustomFileNameSet)
            {
                sb.AppendLine("[LocalizedFileNames]");
                foreach(KeyValuePair<string,string> keyValuePair in content.CustomFileName)
                {
                    sb.AppendLine($"{keyValuePair.Key}={keyValuePair.Value}");
                }
            }
            return sb.ToString();
        }
    }
}
