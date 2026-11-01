using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;   // 仅为返回 Visibility，用于控制错误提示的显隐

namespace DIEditor.Controls;

/// <summary>
/// 列表中的“一行”：左边一个键、右边一个值。
/// 实现 INotifyPropertyChanged 的目的有两个：
/// 1) TextBox 的 TwoWay 绑定在文本变化时能把值回写到对象上；
/// 2) 校验结果（如“键重复”）变化时，界面上的红色提示能实时刷新。
/// </summary>
public class KeyValueItem : INotifyPropertyChanged
{
    private string _key = string.Empty;
    private string _value = string.Empty;
    private string _validationMessage = string.Empty;

    /// <summary>左输入框的文本，将作为 Dictionary 的 Key。</summary>
    public string Key
    {
        get => _key;
        // 绑定源可能传入 null，这里统一收敛为空字符串，避免后续 Trim() 空引用
        set => SetProperty(ref _key, value ?? string.Empty);
    }

    /// <summary>右输入框的文本，将作为 Dictionary 的 Value。</summary>
    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value ?? string.Empty);
    }

    /// <summary>
    /// 校验信息。空字符串表示这一行没有问题；
    /// 非空时会在行下方显示红色提示文字。
    /// </summary>
    public string ValidationMessage
    {
        get => _validationMessage;
        set
        {
            if (SetProperty(ref _validationMessage, value ?? string.Empty))
            {
                // 这两个派生属性依赖 ValidationMessage，必须一并通知
                OnPropertyChanged(nameof(HasError));
                OnPropertyChanged(nameof(ErrorVisibility));
            }
        }
    }

    /// <summary>是否存在校验错误。</summary>
    public bool HasError => !string.IsNullOrEmpty(_validationMessage);

    /// <summary>
    /// 供 XAML 直接绑定到 Visibility，省去写一个 BoolToVisibilityConverter。
    /// </summary>
    public Visibility ErrorVisibility =>
        HasError ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>键和值都为空的行，转换字典时可选择跳过。</summary>
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(_key) && string.IsNullOrEmpty(_value);

    /// <summary>属性变更通知，XAML 绑定引擎依赖它刷新界面。</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>
    /// 通用赋值辅助方法：值没变就不发通知，避免无谓的界面刷新和校验重入。
    /// </summary>
    private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
        {
            return false;
        }

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
