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
        // Slider.Value를 읽어서 Popup에 표시 (원래 방식)
        UpdatePopupValue();
    }

    private void OnThumbDragCompleted(object sender, RoutedEventArgs e)
    {
        // 드래그 완료 시점의 값으로 popup 한 번 더 업데이트 (SliderDragCompletedBehavior가 적용하는 값과 일치시킴)
        if (AssociatedObject != null)
        {
            System.Diagnostics.Debug.WriteLine($"[SliderPopup-DragComplete-THUMB] Value={AssociatedObject.Value}");
        }
        UpdatePopupValue();
        
        _isDragging = false;
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

    private void ShowPopup()
    {
        if (_popup != null && _popupText != null && AssociatedObject != null)
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

    private void UpdatePopupValue()
    {
        if (_popupText != null && AssociatedObject != null)
        {
            // Slider 값을 그대로 표시 (적용되는 값과 동일한 포맷)
            double rawValue = AssociatedObject.Value;
            
            // 디버그 로그
            System.Diagnostics.Debug.WriteLine($"[SliderPopup] Raw={rawValue}, Display={rawValue}");
            
            _popupText.Text = rawValue.ToString();
        }
    }
}
