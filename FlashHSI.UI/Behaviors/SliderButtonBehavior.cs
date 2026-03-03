using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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
    
    // Popup 관련
    private Popup? _popup;
    private TextBlock? _popupText;
    private double _lastButtonValue;  // 버튼 조작 시 저장할 값
    private DispatcherTimer? _applyTimer;  // 500ms 후 값 적용용 타이머
    
    // 버튼 상태
    private bool _isButtonPressed = false;
    private bool _isHoldMode = false;  // 홀로 진입했는지 여부 (홀드 모드일 때 단일 클릭 값 적용 방지)

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
        
        // 타이머 정리
        _applyTimer?.Stop();
        
        // Popup 정리
        if (_popup != null)
        {
            _popup.IsOpen = false;
        }

        // 버튼 제거
        RemoveButtons();

        base.OnDetaching();
    }

    private void OnSliderLoaded(object sender, RoutedEventArgs e)
    {
        if (_slider == null || _buttonsAdded) return;
        _buttonsAdded = true;

        // Popup 생성
        CreatePopup();
        
        // 타이머 초기화
        _applyTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(700)  // 홀드 모드 종료를 위해 대기 시간 증가
        };
        _applyTimer.Tick += OnApplyTimerTick;

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
    
    private void CreatePopup()
    {
        if (_slider == null) return;

        System.Diagnostics.Debug.WriteLine($"[SliderButtonBehavior] CreatePopup called for slider");

        _popup = new Popup
        {
            PlacementTarget = _slider,
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
        
        System.Diagnostics.Debug.WriteLine($"[SliderButtonBehavior] Popup created: {_popup != null}");
    }
    
    private void UpdatePopupValue()
    {
        if (_popupText != null && _slider != null)
        {
            _popupText.Text = _slider.Value.ToString("F3");
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
        
        // RepeatButton으로 长押し対応
        _decreaseButton = new RepeatButton
        {
            Content = new PackIcon { Kind = PackIconKind.Minus, Width = 16, Height = 16 },
            Width = 28,
            Height = 28,
            VerticalAlignment = sliderVerticalAlignment,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
            Delay = 500,  // 첫 반복까지 500ms 대기
            Interval = 30,  // 30ms마다 반복 (더 빠르게)
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
            Delay = 500,  // 첫 반복까지 500ms 대기
            Interval = 30,  // 30ms마다 반복 (더 빠르게)
            Style = Application.Current.Resources["MaterialDesignIconButton"] as Style
        };
        _increaseButton.Click += OnIncreaseClick;
    }

    private void OnButtonMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isButtonPressed = true;
        _isHoldMode = false;
        
        // 타이머 중단
        _applyTimer?.Stop();
        
        // Popup 표시
        ShowPopup();
    }
    
    private void OnButtonMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isButtonPressed = false;
        
        // 단일 클릭이든 홀이든 500ms 후 적용
        HidePopup();
        _applyTimer?.Stop();
        _applyTimer?.Start();
    }
    
    private void OnApplyTimerTick(object? sender, EventArgs e)
    {
        // 500ms 후 실행
        _applyTimer?.Stop();
        
        // 계속 홀딩 중이면 타이머 리셋해서 계속 대기
        if (_isButtonPressed)
        {
            _applyTimer?.Start();
            return;
        }
        
        // 버튼을 놓았으면
        HidePopup();
        
        // 홀드로 진입했었다면: 값 적용 안 함 (첫 클릭은 무효)
        if (_isHoldMode)
        {
            _isHoldMode = false;  // 플래그 리셋
            System.Diagnostics.Debug.WriteLine("[SliderButtonBehavior] Hold mode - value not applied");
            return;
        }
        
        // 일반 단일 클릭: 값 적용
        if (_slider != null)
        {
            _slider.GetBindingExpression(Slider.ValueProperty)?.UpdateSource();
            System.Diagnostics.Debug.WriteLine("[SliderButtonBehavior] Single click - value applied");
        }
    }

    private void RemoveButtons()
    {
        if (_decreaseButton != null)
        {
            _decreaseButton.PreviewMouseLeftButtonDown -= OnButtonMouseDown;
            _decreaseButton.PreviewMouseLeftButtonUp -= OnButtonMouseUp;
            _decreaseButton.Click -= OnDecreaseClick;
            var parent = VisualTreeHelper.GetParent(_decreaseButton);
            if (parent is Panel panel)
            {
                panel.Children.Remove(_decreaseButton);
            }
        }

        if (_increaseButton != null)
        {
            _increaseButton.PreviewMouseLeftButtonDown -= OnButtonMouseDown;
            _increaseButton.PreviewMouseLeftButtonUp -= OnButtonMouseUp;
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

        System.Diagnostics.Debug.WriteLine($"[SliderButtonBehavior] DecreaseClick called, isHoldMode={_isHoldMode}");

        // 첫 번째 Interval 클릭: 홀로 진입했음을 표시하고 값 변경 안 함
        if (!_isHoldMode)
        {
            _isHoldMode = true;  // 홀드 모드 시작
            return;  // 값 변경 없이 타이머만 시작
        }

        // 두 번째 이후 Interval 클릭부터 값 변경
        double tick = _slider.TickFrequency > 0 ? _slider.TickFrequency : 1;
        _slider.Value = Math.Max(_slider.Minimum, _slider.Value - tick);
        
        // Popup 업데이트 (값만 표시, 적용 X)
        UpdatePopupValue();
        
        // Popup 보이기
        ShowPopup();
        
        // 500ms 타이머 리셋
        _applyTimer?.Stop();
        _applyTimer?.Start();
    }

    private void OnIncreaseClick(object sender, RoutedEventArgs e)
    {
        if (_slider == null) return;

        System.Diagnostics.Debug.WriteLine($"[SliderButtonBehavior] IncreaseClick called, isHoldMode={_isHoldMode}");

        // 첫 번째 Interval 클릭: 홀로 진입했음을 표시하고 값 변경 안 함
        if (!_isHoldMode)
        {
            _isHoldMode = true;  // 홀드 모드 시작
            return;  // 값 변경 없이 타이머만 시작
        }

        // 두 번째 이후 Interval 클릭부터 값 변경
        double tick = _slider.TickFrequency > 0 ? _slider.TickFrequency : 1;
        _slider.Value = Math.Min(_slider.Maximum, _slider.Value + tick);
        
        // Popup 업데이트 (값만 표시, 적용 X)
        UpdatePopupValue();
        
        // Popup 보이기
        ShowPopup();
        
        // 500ms 타이머 리셋
        _applyTimer?.Stop();
        _applyTimer?.Start();
    }
}
