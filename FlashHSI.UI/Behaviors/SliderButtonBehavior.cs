using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using Microsoft.Xaml.Behaviors;

namespace FlashHSI.UI.Behaviors;

/// <summary>
/// Slider 좌우에 +/- 버튼을 추가하는 Behavior
/// </summary>
public class SliderButtonBehavior : Behavior<Slider>
{
    private RepeatButton? _decreaseButton;
    private RepeatButton? _increaseButton;
    private Slider? _slider;
    private bool _buttonsAdded = false;

    protected override void OnAttached()
    {
        base.OnAttached();
        _slider = AssociatedObject;

        if (_slider == null) return;

        _slider.Loaded += OnSliderLoaded;
    }

    protected override void OnDetaching()
    {
        if (_slider != null)
        {
            _slider.Loaded -= OnSliderLoaded;
        }

        // 버튼 제거
        RemoveButtons();

        base.OnDetaching();
    }

    private void OnSliderLoaded(object sender, RoutedEventArgs e)
    {
        if (_slider == null || _buttonsAdded) return;
        _buttonsAdded = true;

        // 부모 찾기 (Grid 또는 DockPanel)
        var parent = VisualTreeHelper.GetParent(_slider);
        
        if (parent is Grid grid)
        {
            AddButtonsToGrid(grid);
        }
        else if (parent is DockPanel dockPanel)
        {
            AddButtonsToDockPanel(dockPanel);
        }
    }

    private void AddButtonsToGrid(Grid parent)
    {
        if (_slider == null) return;

        // 버튼 생성
        CreateButtons();

        // Slider의 현재 위치 파악
        int sliderIndex = -1;
        for (int i = 0; i < parent.Children.Count; i++)
        {
            if (parent.Children[i] == _slider)
            {
                sliderIndex = i;
                break;
            }
        }

        if (sliderIndex >= 0)
        {
            // Slider 좌측에 Decrease 버튼 추가
            parent.Children.Insert(sliderIndex, _decreaseButton);
            // Slider 우측에 Increase 버튼 추가
            parent.Children.Insert(sliderIndex + 2, _increaseButton);
        }
    }

    private void AddButtonsToDockPanel(DockPanel parent)
    {
        if (_slider == null) return;

        // 버튼 생성
        CreateButtons();

        // DockPanel의 마지막 자식이 Slider이므로, Slider 앞에 버튼 추가
        _decreaseButton.SetValue(DockPanel.DockProperty, Dock.Left);
        _increaseButton.SetValue(DockPanel.DockProperty, Dock.Right);
        
        // 버튼을 Slider 앞에 추가
        int sliderIndex = -1;
        for (int i = 0; i < parent.Children.Count; i++)
        {
            if (parent.Children[i] == _slider)
            {
                sliderIndex = i;
                break;
            }
        }

        if (sliderIndex > 0)
        {
            parent.Children.Insert(sliderIndex - 1, _decreaseButton);
            parent.Children.Insert(parent.Children.Count - 1, _increaseButton);
        }
    }

    private void CreateButtons()
    {
        // Slider의 VerticalAlignment를 가져오거나 기본값 Center 사용
        var sliderVerticalAlignment = _slider?.VerticalAlignment ?? VerticalAlignment.Center;
        
        // RepeatButton으로 長押し対応
        _decreaseButton = new RepeatButton
        {
            Content = new PackIcon { Kind = PackIconKind.Minus, Width = 16, Height = 16 },
            Width = 28,
            Height = 28,
            VerticalAlignment = sliderVerticalAlignment,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
            Delay = 500,  // 첫 반복까지 500ms 대기
            Interval = 100,  // 그 후 100ms마다 반복 (10틱/초)
            Style = Application.Current.Resources["MaterialDesignIconButton"] as Style
        };
        _decreaseButton.Click += OnDecreaseClick;

        _increaseButton = new RepeatButton
        {
            Content = new PackIcon { Kind = PackIconKind.Plus, Width = 16, Height = 16 },
            Width = 28,
            Height = 28,
            VerticalAlignment = sliderVerticalAlignment,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0),
            Delay = 500,
            Interval = 100,
            Style = Application.Current.Resources["MaterialDesignIconButton"] as Style
        };
        _increaseButton.Click += OnIncreaseClick;
    }

    private void RemoveButtons()
    {
        if (_decreaseButton != null)
        {
            _decreaseButton.Click -= OnDecreaseClick;
            var parent = VisualTreeHelper.GetParent(_decreaseButton);
            if (parent is Panel panel)
            {
                panel.Children.Remove(_decreaseButton);
            }
        }

        if (_increaseButton != null)
        {
            _increaseButton.Click -= OnIncreaseClick;
            var parent = VisualTreeHelper.GetParent(_increaseButton);
            if (parent is Panel panel)
            {
                panel.Children.Remove(_increaseButton);
            }
        }
    }

    private void OnDecreaseClick(object sender, RoutedEventArgs e)
    {
        if (_slider == null) return;

        // TickFrequency 또는 1만큼 감소
        double tick = _slider.TickFrequency > 0 ? _slider.TickFrequency : 1;
        _slider.Value = Math.Max(_slider.Minimum, _slider.Value - tick);
        
        // Binding Source 업데이트 (UpdateSourceTrigger=Explicit 대응)
        _slider.GetBindingExpression(Slider.ValueProperty)?.UpdateSource();
    }

    private void OnIncreaseClick(object sender, RoutedEventArgs e)
    {
        if (_slider == null) return;

        // TickFrequency 또는 1만큼 증가
        double tick = _slider.TickFrequency > 0 ? _slider.TickFrequency : 1;
        _slider.Value = Math.Min(_slider.Maximum, _slider.Value + tick);
        
        // Binding Source 업데이트 (UpdateSourceTrigger=Explicit 대응)
        _slider.GetBindingExpression(Slider.ValueProperty)?.UpdateSource();
    }
}
