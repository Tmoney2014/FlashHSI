using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Xaml.Behaviors;

namespace FlashHSI.UI.Behaviors;

/// <summary>
/// Slider 드래그 중 값 팝업을 표시하는 Behavior
/// </summary>
public class SliderValuePopupBehavior : Behavior<Slider>
{
    private Popup? _popup;
    private TextBlock? _popupText;
    private bool _isDragging;
    private TextBox? _linkedTextBox;
    private double _lastDragValue;  // DragDelta 중에 마지막으로 읽은 값 저장

    /// <summary>
    /// Popup에 표시할 숫자 형식 (예: "N0" = 정수, "N2" = 소수점 2자리)
    /// </summary>
    public static readonly DependencyProperty FormatProperty =
        DependencyProperty.RegisterAttached(
            "Format",
            typeof(string),
            typeof(SliderValuePopupBehavior),
            new PropertyMetadata("N0"));

    public static string GetFormat(DependencyObject obj) => (string)obj.GetValue(FormatProperty);
    public static void SetFormat(DependencyObject obj, string value) => obj.SetValue(FormatProperty, value);

    // AI가 수정함: 이벤트 누수(Memory Leak) 방지를 위해 Unloaded 이벤트 핸들러 추가
    protected override void OnAttached()
    {
        base.OnAttached();

        if (AssociatedObject == null) return;

        AssociatedObject.Loaded += OnLoaded;
        AssociatedObject.Unloaded += OnUnloaded;
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject != null)
        {
            AssociatedObject.Loaded -= OnLoaded;
            AssociatedObject.Unloaded -= OnUnloaded;
        }

        if (_popup != null)
        {
            _popup.IsOpen = false;
        }

        base.OnDetaching();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var thumb = FindThumb(AssociatedObject);
        if (thumb != null)
        {
            thumb.DragStarted += OnThumbDragStarted;
            thumb.DragCompleted += OnThumbDragCompleted;
            thumb.DragDelta += OnThumbDragDelta;
        }

        // 같은 DockPanel에 있는 TextBox 찾기
        _linkedTextBox = FindTextBox(AssociatedObject);

        CreatePopup();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        var thumb = FindThumb(AssociatedObject);
        if (thumb != null)
        {
            thumb.DragStarted -= OnThumbDragStarted;
            thumb.DragCompleted -= OnThumbDragCompleted;
            thumb.DragDelta -= OnThumbDragDelta;
        }

        if (_popup != null)
        {
            _popup.IsOpen = false;
        }
    }

    private TextBox? FindTextBox(DependencyObject slider)
    {
        var parent = VisualTreeHelper.GetParent(slider);
        if (parent is DockPanel dockPanel)
        {
            foreach (var child in dockPanel.Children)
            {
                if (child is TextBox textBox)
                {
                    return textBox;
                }
            }
        }
        return null;
    }

    private Thumb? FindThumb(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);

            if (child is Thumb thumb)
            {
                return thumb;
            }

            var result = FindThumb(child);
            if (result != null)
            {
                return result;
            }
        }
        return null;
    }

    private void CreatePopup()
    {
        if (AssociatedObject == null) return;

        _popup = new Popup
        {
            PlacementTarget = AssociatedObject,
            Placement = PlacementMode.Right,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade,
            IsOpen = false,
            HorizontalOffset = 5
        };

        _popupText = new TextBlock
        {
            Background = new SolidColorBrush(Color.FromArgb(200, 33, 33, 33)),
            Foreground = Brushes.White,
            Padding = new Thickness(8, 4, 8, 4),
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            MinWidth = 50,
            TextAlignment = TextAlignment.Center
        };

        _popup.Child = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(200, 33, 33, 33)),
            CornerRadius = new CornerRadius(4),
            Child = _popupText,
            BorderBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
            BorderThickness = new Thickness(1)
        };
    }

    private void OnThumbDragStarted(object sender, RoutedEventArgs e)
    {
        _isDragging = true;
        HideTextBox();
        ShowPopup();
    }

    private void OnThumbDragDelta(object sender, DragDeltaEventArgs e)
    {
        // Slider.Value를 읽어서 Popup에 표시 + 저장
        if (AssociatedObject != null)
        {
            _lastDragValue = AssociatedObject.Value;
            UpdatePopupValue();
        }
    }

    private void OnThumbDragCompleted(object sender, DragCompletedEventArgs e)
    {
        double finalValue = _lastDragValue;

        // Popup에 최종 값 표시 (DependencyProperty Format 사용)
        if (_popupText != null)
        {
            string format = GetFormat(this);
            _popupText.Text = finalValue.ToString(format);
        }

        // AI가 수정함: _isDragging 플래그를 Binding Source 업데이트 전에 해제
        // (DragCompletedBehavior와의 충돌 방지)
        _isDragging = false;

        if (AssociatedObject != null)
        {
            AssociatedObject.Value = finalValue;
            var bindingExpr = AssociatedObject.GetBindingExpression(Slider.ValueProperty);
            bindingExpr?.UpdateSource();
        }

        _isDragging = false;

        // Popup 숨기고 TextBox 보이기
        HidePopup();
        ShowTextBox();
    }

    private void HideTextBox()
    {
        if (_linkedTextBox != null)
        {
            _linkedTextBox.Opacity = 0;
        }
    }

    private void ShowTextBox()
    {
        if (_linkedTextBox != null)
        {
            _linkedTextBox.Opacity = 1;
        }
    }

    private void UpdatePopupValue()
    {
        if (_popupText != null)
        {
            // DependencyProperty로 등록된 Format 속성 활용 (기본값 N0)
            string format = GetFormat(this);
            _popupText.Text = _lastDragValue.ToString(format);
        }
    }

    private void ShowPopup()
    {
        if (_popup != null && _popupText != null)
        {
            UpdatePopupValue();
            _popup.IsOpen = true;
        }
    }

    private void HidePopup()
    {
        if (_popup != null)
        {
            _popup.IsOpen = false;
        }
    }
}
