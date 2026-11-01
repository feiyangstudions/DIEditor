using DIEditor.Core;
using FToolkit.WinUI3.Windows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using System;
using System.Collections.Generic;
using System.IO;
using Windows.Globalization.NumberFormatting;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace DIEditor
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private string _rawDirPath;
        private bool _diGenerated;
        private IDIContent _diContent;
        private string _diContentString;
        private SupportingEncodings encoding = SupportingEncodings.UTF16_LE;

        public MainWindow()
        {
            InitializeComponent();
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(Titlebar);
            this.ResizeUser32(1200, 600);
            AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

            var formatter = new DecimalFormatter
            {
                IntegerDigits = 1,
                FractionDigits = 0,
                NumberRounder = new IncrementNumberRounder
                {
                    Increment = 1,
                    RoundingAlgorithm = RoundingAlgorithm.RoundHalfAwayFromZero
                }
            };
            CustomIconIndexNumberBox.NumberFormatter = formatter;
        }

        private void Window_Closed(object sender, WindowEventArgs args)
        {
            Environment.Exit(0);
        }

        private void CustomIconCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            // 检测自定义图标路径输入框、索引输入框和浏览按钮是否被禁用，是则启用
            if (CustomIconPathTextBox.IsEnabled == false) { CustomIconPathTextBox.IsEnabled = true; }
            if (CustomIconIndexNumberBox.IsEnabled == false) { CustomIconIndexNumberBox.IsEnabled = true; }
            if (CustomIconPathSelectButton.IsEnabled == false) { CustomIconPathSelectButton.IsEnabled = true; }
        }

        private void CustomIconCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            // 检测自定义图标路径输入框、索引输入框和浏览按钮是否被启用，是则禁用
            if (CustomIconPathTextBox.IsEnabled == true) { CustomIconPathTextBox.IsEnabled = false; }
            if (CustomIconIndexNumberBox.IsEnabled == true) { CustomIconIndexNumberBox.IsEnabled = false; }
            if (CustomIconPathSelectButton.IsEnabled == true) { CustomIconPathSelectButton.IsEnabled = false; }
        }

        private void CustomNameCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            // 检测自定义名称输入框是否被禁用，是则启用
            if (CustomNameTextBox.IsEnabled == false) { CustomNameTextBox.IsEnabled = true; }
        }

        private void CustomNameCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            // 检测自定义名称输入框是否被启用，是则禁用
            if (CustomNameTextBox.IsEnabled == true) { CustomNameTextBox.IsEnabled = false; }
        }

        private void CustomTooltipCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            // 检测自定义悬停提示输入框是否被禁用，是则启用
            if (CustomTooltipTextBox.IsEnabled == false) { CustomTooltipTextBox.IsEnabled = true; }
        }

        private void CustomTooltipCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            // 检测自定义悬停提示输入框是否被启用，是则禁用
            if (CustomTooltipTextBox.IsEnabled == true) { CustomTooltipTextBox.IsEnabled = false; }
        }

        private async void CustomIconPathSelectButton_Click(object sender, RoutedEventArgs e)
        {
            // 创建一个文件选择器
            var picker = new FileOpenPicker(AppWindow.Id);
            picker.FileTypeFilter.Add(".ico");

            var result = await picker.PickSingleFileAsync();

            if (result is null)
            {
                // 用户点了取消，这不是错误，安静返回即可
                return;
            }

            CustomIconPathTextBox.Text = result.Path;
        }

        private void CustomFileNameCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            // 检测自定义文件名编辑器是否禁用，是则启用
            if (CustomFileNameEditor.IsEnabled == false) { CustomFileNameEditor.IsEnabled = true; }
        }

        private void CustomFileNameCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            // 检测自定义文件名编辑器是否启用，是则禁用
            if (CustomFileNameEditor.IsEnabled == true) { CustomFileNameEditor.IsEnabled = false; }
        }

        private async void RawDirPathPickButton_Click(object sender, RoutedEventArgs e)
        {
            // 创建一个文件选择器
            var picker = new FolderPicker(AppWindow.Id);

            var result = await picker.PickSingleFolderAsync();

            if (result is null)
            {
                // 用户点了取消，这不是错误，安静返回即可
                return;
            }

            RawDirPathTextBox.Text = result.Path;
        }

        private void LoadRawDirButton_Click(object sender, RoutedEventArgs e)
        {
            // 载入程序
            // 判断是否存在这个文件夹
            if (Path.Exists(RawDirPathTextBox.Text) && Directory.Exists(RawDirPathTextBox.Text))
            {
                _rawDirPath = RawDirPathTextBox.Text.Trim();
            } else
            {
                var ed = new ContentDialog
                {
                    Title = "提示",
                    Content = $"{RawDirPathTextBox.Text.Trim()}不存在！\n请选择一个存在的文件夹！",
                    CloseButtonText = "关闭"
                };
                ed.XamlRoot = this.Content.XamlRoot;
                _ = ed.ShowAsync();
                return;
            }

            // 先设 BrowseFolder，否则浏览按钮还指向上一个目录
            CustomFileNameEditor.BrowseFolder = _rawDirPath;

            // 载入配置

            string iniPath = Path.Combine(_rawDirPath, "desktop.ini");

            try
            {
                var parser = new DIParser();
                DIContent content = parser.Load(iniPath);   // 编码自动判断：UTF-16 LE / ANSI
                ApplyToEditor(content);
            } catch (FileNotFoundException)
            {
                // 不存在desktop.ini，跳过
            } catch (Exception ex)
            {
                var ed = new ContentDialog
                {
                    Title = "错误",
                    Content = $"载入配置时发生错误：{ex.Message}\n如果您无法理解错误内容，请告诉开发者",
                    CloseButtonText = "关闭"
                };
                ed.XamlRoot = this.Content.XamlRoot;
                _ = ed.ShowAsync();
            }

            // 启用编辑选项
            CustomNameCheckBox.IsEnabled = true;
            CustomIconCheckBox.IsEnabled = true;
            CustomTooltipCheckBox.IsEnabled = true;
            CustomFileNameCheckBox.IsEnabled = true;
            GenerateButton.IsEnabled = true;
            DIPreviewTextBox.IsEnabled = true;
            DIHiddenCheckBox.IsEnabled = true;
            DISystemCheckBox.IsEnabled = true;
            DIEncodingComboBox.IsEnabled = true;
        }

        private void ResetEditor()
        {
            // 名称
            CustomNameCheckBox.IsChecked = false;
            CustomNameCheckBox.IsEnabled = false;
            CustomNameTextBox.Text = string.Empty;
            CustomNameTextBox.IsEnabled = false;

            // 图标
            CustomIconCheckBox.IsChecked = false;
            CustomIconCheckBox.IsEnabled = false;
            CustomIconPathTextBox.Text = string.Empty;
            CustomIconPathTextBox.IsEnabled = false;
            CustomIconIndexNumberBox.Value = 0;
            CustomIconIndexNumberBox.IsEnabled = false;
            CustomIconPathSelectButton.IsEnabled = false;

            // 悬停提示
            CustomTooltipCheckBox.IsChecked = false;
            CustomTooltipCheckBox.IsEnabled = false;
            CustomTooltipTextBox.Text = string.Empty;
            CustomTooltipTextBox.IsEnabled = false;

            // 文件名映射
            CustomFileNameCheckBox.IsChecked = false;
            CustomFileNameCheckBox.IsEnabled = false;
            CustomFileNameEditor.ClearItems();
            CustomFileNameEditor.IsEnabled = false;

            // 输出
            DIPreviewTextBox.Text = string.Empty;
            DIPreviewTextBox.IsEnabled = false;
            DIHiddenCheckBox.IsEnabled = false;
            DISystemCheckBox.IsEnabled = false;
            DIEncodingComboBox.IsEnabled = false;
            GenerateButton.IsEnabled = false;
            SaveButton.IsEnabled = false;
            _diGenerated = false;
            _diContentString = string.Empty;
        }

        private void ApplyToEditor(DIContent content)
        {
            if (content is null) return;

            // 先赋文本，再勾选：勾选会触发 Checked 事件，把那几个输入框自动启用
            if (content.HasCustomName)
            {
                CustomNameTextBox.Text = content.CustomName;
                CustomNameCheckBox.IsChecked = true;
            }

            if (content.HasCustomIcon)
            {
                CustomIconPathTextBox.Text = content.CustomIconPath;
                CustomIconIndexNumberBox.Value = content.CustomIconIndex;
                CustomIconCheckBox.IsChecked = true;
            }

            if (content.HasCustomTooltip)
            {
                CustomTooltipTextBox.Text = content.CustomTooltip;
                CustomTooltipCheckBox.IsChecked = true;
            }

            if (content.HasCustomFileNameSet)
            {
                CustomFileNameEditor.LoadFromDictionary(content.CustomFileName);
                CustomFileNameCheckBox.IsChecked = true;
            }
        }

        private async void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            static string Gen(bool? ischecked, string? textboxcont) 
            {
                if (ischecked == true)
                {
                    if (!string.IsNullOrEmpty(textboxcont)) return textboxcont;
                }
                return string.Empty;
            }
            static int Gen1(bool? ischecked, int numberboxcont)
            {
                if (ischecked == true) return numberboxcont;
                return 0;
            }

            IDIContent dic = new DIContent();
            dic.CustomName = Gen(CustomNameCheckBox.IsChecked, CustomNameTextBox.Text);
            dic.CustomIconPath = Gen(CustomIconCheckBox.IsChecked, CustomIconPathTextBox.Text);
            dic.CustomIconIndex = Gen1(CustomIconCheckBox.IsChecked, (int)CustomIconIndexNumberBox.Value);
            dic.CustomTooltip = Gen(CustomTooltipCheckBox.IsChecked, CustomTooltipTextBox.Text);
            dic.SystemFileAttribDisabled = DISystemCheckBox.IsChecked == true ? false : true;
            dic.HiddenFileAttribDisabled = DIHiddenCheckBox.IsChecked == true ? false : true;
            if (CustomFileNameCheckBox.IsChecked == true)
            {
                Dictionary<string, string> keyValuePairs;
                string error;
                var he = CustomFileNameEditor.TryToDictionary(out keyValuePairs, out error);
                if (!he)
                {
                    var ed = new ContentDialog
                    {
                        Title = "错误",
                        Content = $"{error}\n如果您无法理解错误内容，请告诉开发者",
                        CloseButtonText = "关闭"
                    };
                    ed.XamlRoot = this.Content.XamlRoot;
                    _ = await ed.ShowAsync();
                } else
                {
                    dic.CustomFileName = keyValuePairs;
                }
            }
            _diContent = dic;
            _diContentString = DIGenerator.Generate(dic);
            DIPreviewTextBox.Text = _diContentString;
            _diGenerated = true;
            SaveButton.IsEnabled = true;
        }

        private void CustomIconIndexNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (double.IsNaN(sender.Value))
                return;

            var rounded = Math.Round(sender.Value, MidpointRounding.AwayFromZero);
            if (Math.Abs(rounded - sender.Value) > 1e-9)
            {
                sender.Value = rounded;   // 第二次进来时已相等，不会无限递归
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_diGenerated) DISave.SaveAsDI(_rawDirPath, _diContent, encoding);
            } catch (Exception ex)
            {
                var ed = new ContentDialog
                {
                    Title = "错误",
                    Content = $"{ex.Message}\n如果您无法理解错误内容，请告诉开发者",
                    CloseButtonText = "关闭"
                };
                ed.XamlRoot = this.Content.XamlRoot;
                _ = await ed.ShowAsync();
                return;
            }
            var sd = new ContentDialog
            {
                Title = "提示",
                Content = $"desktop.ini已成功保存！",
                CloseButtonText = "关闭"
            };
            sd.XamlRoot = this.Content.XamlRoot;
            _ = await sd.ShowAsync();
        }

        private void RawDirPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // 重置编辑器
            ResetEditor();
        }

        private void DIEncodingComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DIEncodingComboBox.SelectedIndex == 0) encoding = SupportingEncodings.UTF16_LE;
            else if (DIEncodingComboBox.SelectedIndex == 1) encoding = SupportingEncodings.ANSI;
        }
    }
}
