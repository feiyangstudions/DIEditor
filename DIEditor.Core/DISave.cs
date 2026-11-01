using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DIEditor.Core
{
    public static class DISave
    {
        public static void SaveAsDI(string path, IDIContent diContent, SupportingEncodings encoding)
        {
            string content = DIGenerator.Generate(diContent);
            if (content?.Trim() == string.Empty) return;
            var dipath = Path.Combine(path, "desktop.ini");
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            if (!Path.Exists(path)) throw new IOException($"{path} not exists!");
            if (Path.Exists(dipath)) File.SetAttributes(dipath, FileAttributes.None);
            if (encoding == SupportingEncodings.UTF16_LE) File.WriteAllText(dipath, content, Encoding.Unicode);
            else File.WriteAllText(dipath, content, Encoding.GetEncoding(0));
            if (diContent.HiddenFileAttribDisabled != true) (new FileInfo(dipath)).Attributes |= FileAttributes.Hidden;
            if (diContent.SystemFileAttribDisabled != true) (new FileInfo(dipath)).Attributes |= FileAttributes.System;
            (new DirectoryInfo(path)).Attributes |= FileAttributes.ReadOnly;
        }
    }
}
