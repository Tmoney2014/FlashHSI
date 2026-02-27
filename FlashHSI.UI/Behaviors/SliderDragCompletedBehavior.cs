using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Xaml.Behaviors;

namespace FlashHSI.UI.Behaviors;

/// <summary>
///     슬라이더 드래그가 완료될 때 Command를 실행하는 Behavior
/// </summary>
/// <ai>AI가 작성함</ai>
public class SliderDragCompletedBehavior : Behavior<Slider>
{
    /// <summary>
    ///     드래그 완료 시 실행할 Command
    /// </summary>
    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(
            nameof(Command),
            typeof(ICommand),
            typeof(SliderDragCompletedBehavior),
            new PropertyMetadata(null));

    /// <summary>
    ///     Command에 전달할 파라미터
    /// </summary>
    public static readonly DependencyProperty CommandParameterProperty =
        DependencyProperty.Register(
            nameof(CommandParameter),
            typeof(object),
            typeof(SliderDragCompletedBehavior),
            new PropertyMetadata(null));

    /// <summary>
    /// Command가 없을 때 호출할 콜백 (Action)
    /// </summary>
    public static readonly DependencyProperty CallbackProperty =
        DependencyProperty.Register(
            nameof(Callback),
            typeof(Action),
            typeof(SliderDragCompletedBehavior),
            new PropertyMetadata(null));

    private bool _isDragging;

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public Action? Callback
    {
        get => (Action?)GetValue(CallbackProperty);
        set => SetValue(CallbackProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();

        if (AssociatedObject != null)
        {
            AssociatedObject.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
            AssociatedObject.PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
            AssociatedObject.MouseLeave += OnMouseLeave;
            AssociatedObject.KeyUp += OnKeyUp;
        }
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject != null)
        {
            AssociatedObject.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
            AssociatedObject.PreviewMouseLeftButtonUp -= OnPreviewMouseLeftButtonUp;
            AssociatedObject.MouseLeave -= OnMouseLeave;
            AssociatedObject.KeyUp -= OnKeyUp;
        }

        base.OnDetaching();
    }

    private void ExecuteCommand()
    {
        // 1. 먼저 Binding Source 업데이트 (값을 ViewModel에 적용)
        var bindingExpr = AssociatedObject?.GetBindingExpression(Slider.ValueProperty);
        if (bindingExpr != null)
        {
            bindingExpr.UpdateSource();
        }
        
        // 2. Command가 있으면 실행
        if (Command?.CanExecute(CommandParameter) == true)
        {
            Command.Execute(CommandParameter);
        }
        // 3. Command가 없으면 콜백만 호출
        else
        {
            Callback?.Invoke();
        }
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        // 키보드로 슬라이더 조작 시에도 Command 실행
        if (e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Up || e.Key == Key.Down ||
            e.Key == Key.Home || e.Key == Key.End || e.Key == Key.PageUp || e.Key == Key.PageDown)
            ExecuteCommand();
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            ExecuteCommand();
        }
    }

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDragging = true;
    }

    private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // 드래그 또는 클릭 모두에서 Command 실행
        _isDragging = false;
        ExecuteCommand();
    }
}
