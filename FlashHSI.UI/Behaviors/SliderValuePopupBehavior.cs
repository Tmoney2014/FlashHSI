using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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

    protected override void OnAttached()
    {
        base.OnAttached();

        if (AssociatedObject == null) return;

        AssociatedObject.Loaded += OnLoaded;
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject != null)
        {
            AssociatedObject.Loaded -= OnLoaded;
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
        // ★★★ 저장한 마지막 값을 Popup에 표시 ★★★
        double finalValue = _lastDragValue;
        System.Diagnostics.Debug.WriteLine($"[SliderPopup] finalValue={finalValue}");
        
        // Popup에 최종 값 표시 (소수점 3자리)
        if (_popupText != null)
        {
            _popupText.Text = finalValue.ToString("F3");
        }
        
        // ViewModel에 저장한 값으로 직접 설정
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
            // TextBox와 동일하게 소수점 3자리까지 표시
            _popupText.Text = _lastDragValue.ToString("F3");
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
