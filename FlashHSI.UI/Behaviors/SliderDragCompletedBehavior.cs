using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

    // AI가 수정함: 마우스 이벤트 억지 조합을 제거하고, WPF 네이티브 Thumb 이벤트를 통해 정확한 상태 추적
    protected override void OnAttached()
    {
        base.OnAttached();

        if (AssociatedObject != null)
        {
            AssociatedObject.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(OnDragStarted));
            AssociatedObject.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnDragCompleted));
            AssociatedObject.PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
            AssociatedObject.KeyUp += OnKeyUp;
        }
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject != null)
        {
            AssociatedObject.RemoveHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(OnDragStarted));
            AssociatedObject.RemoveHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnDragCompleted));
            AssociatedObject.PreviewMouseLeftButtonUp -= OnPreviewMouseLeftButtonUp;
            AssociatedObject.KeyUp -= OnKeyUp;
        }

        base.OnDetaching();
    }

    private void OnDragStarted(object sender, DragStartedEventArgs e)
    {
        _isDragging = true;
    }

    private void OnDragCompleted(object sender, DragCompletedEventArgs e)
    {
        _isDragging = false;
        ExecuteCommand();
    }

    private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // 썸(Thumb) 드래그가 아닐 때(예: 트랙 빈 공간 클릭)만 실행하여 중복 호출 방지
        if (!_isDragging)
        {
            ExecuteCommand();
        }
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        // 키보드로 슬라이더 조작 시 Command 실행
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown)
        {
            ExecuteCommand();
        }
    }

    private void ExecuteCommand()
    {
        if (AssociatedObject == null) return;

        // Binding Source 업데이트 (값을 ViewModel에 적용)
        var bindingExpr = AssociatedObject.GetBindingExpression(Slider.ValueProperty);
        bindingExpr?.UpdateSource();

        // Command가 있으면 실행, 없으면 Action Callback 등록된 내용 실행
        if (Command?.CanExecute(CommandParameter) == true)
        {
            Command.Execute(CommandParameter);
        }
        else
        {
            Callback?.Invoke();
        }
    }
}
