using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FlashHSI.UI.Behaviors;

/// <summary>
/// TextBox에 숫자(정수/소수)만 입력 가능하게 제한하는 Attached Behavior
/// 사용: behaviors:NumericOnlyBehavior.IsEnabled="True"
/// </summary>
/// <ai>AI가 작성함</ai>
public static class NumericOnlyBehavior
{
    // AI가 작성함: 숫자(정수 및 소수점, 음수 부호 포함) 허용 정규식
    private static readonly Regex NumericRegex = new(@"^-?\d*\.?\d*$", RegexOptions.Compiled);

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(NumericOnlyBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox textBox) return;

        if ((bool)e.NewValue)
        {
            textBox.PreviewTextInput += OnPreviewTextInput;
            DataObject.AddPastingHandler(textBox, OnPaste);
        }
        else
        {
            textBox.PreviewTextInput -= OnPreviewTextInput;
            DataObject.RemovePastingHandler(textBox, OnPaste);
        }
    }

    /// <summary>
    /// 키보드 입력 시 숫자가 아니면 차단
    /// </summary>
    private static void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is not TextBox textBox) return;

        // 입력 후 예상 텍스트를 조합해서 검증
        var newText = textBox.Text.Remove(textBox.SelectionStart, textBox.SelectionLength)
                                  .Insert(textBox.SelectionStart, e.Text);

        if (!NumericRegex.IsMatch(newText))
        {
            e.Handled = true; // 입력 차단
        }
    }

    /// <summary>
    /// 붙여넣기 시 숫자가 아니면 차단
    /// </summary>
    private static void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetDataPresent(typeof(string)))
        {
            var pastedText = (string)e.DataObject.GetData(typeof(string))!;
            if (!NumericRegex.IsMatch(pastedText))
            {
                e.CancelCommand();
            }
        }
        else
        {
            e.CancelCommand();
        }
    }
}
