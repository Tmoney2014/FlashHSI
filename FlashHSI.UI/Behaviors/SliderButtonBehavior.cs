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
    private DispatcherTimer? _applyTimer;  // 500ms 후 값 적용용 타이머

    // 버튼 상태
    private bool _isButtonPressed = false;
    private int _clickCount = 1;  // Interval 클릭 횟수 추적
    private DateTime _buttonPressTime;  // 버튼 누른 시간
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
            _slider.Unloaded -= OnSliderUnloaded;
        }

        CleanupResources();
        base.OnDetaching();
    }

    private void CleanupResources()
    {
        // 타이머 정리
        if (_applyTimer != null)
        {
            _applyTimer.Stop();
            _applyTimer.Tick -= OnApplyTimerTick;
            _applyTimer = null;
        }

        // Popup 정리
        if (_popup != null)
        {
            _popup.IsOpen = false;
            _popup.Child = null;
            _popup = null;
        }
        _popupText = null;

        // 버튼 제거
        RemoveButtons();
    }

    private void OnSliderLoaded(object sender, RoutedEventArgs e)
    {
        if (_slider == null || _buttonsAdded) return;
        _buttonsAdded = true;

        // 언로드 이벤트 연결 추가
        _slider.Unloaded -= OnSliderUnloaded;
        _slider.Unloaded += OnSliderUnloaded;

        // Popup 생성
        CreatePopup();

        // 타이머 초기화
        _applyTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(700)  // 홀드 모드 종료를 위해 대기 시간 증가
        };
        _applyTimer.Tick += OnApplyTimerTick;

        // 부모 찾기 (Grid 또는 DeckPanel)
        // DataTemplate 안에서는 한 단계 위의 부모까지 확인
        var parent = VisualTreeHelper.GetParent(_slider);
        var grandParent = parent != null ? VisualTreeHelper.GetParent(parent) : null;

        // 먼저 직접 부모 확인
        if (parent is Grid grid)
        {
            AddButtonsToGrid(grid);
        }
        else if (parent is DockPanel dockPanel)
        {
            AddButtonsToDockPanel(dockPanel);
        }
        // 한 단계 위 부모 확인 (DataTemplate 같은 경우)
        else if (grandParent is Grid grid2)
        {
            AddButtonsToGrid(grid2);
        }
        else if (grandParent is DockPanel dockPanel2)
        {
            AddButtonsToDockPanel(dockPanel2);
        }
    }

    private void OnSliderUnloaded(object sender, RoutedEventArgs e)
    {
        // 뷰 모델(UI)이 트리에서 내려갈 때 리소스 강제 정리 (Memory Leak 방지)
        CleanupResources();
        _buttonsAdded = false;
    }

    private void CreatePopup()
    {
        if (_slider == null) return;

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
    }

    private void UpdatePopupValue()
    {
        if (_popupText != null && _slider != null)
        {
            string format = SliderValuePopupBehavior.GetFormat(_slider);
            _popupText.Text = _slider.Value.ToString(format);
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
        if (_decreaseButton == null || _increaseButton == null) return;

        // Slider의 현재 위치와 Grid.Column 파악
        int sliderIndex = -1;
        int sliderColumn = 0;

        // Slider의 Column 확인
        if (_slider.GetValue(Grid.ColumnProperty) is int col)
        {
            sliderColumn = col;
        }

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
            if (!parent.Children.Contains(_decreaseButton))
            {
                // Slider의 Column-1에 Decrease 버튼 추가 (같은 행)
                _decreaseButton.SetValue(Grid.ColumnProperty, sliderColumn - 1);
                _decreaseButton.SetValue(Grid.RowProperty, 0);
                parent.Children.Insert(sliderIndex, _decreaseButton);
            }

            if (!parent.Children.Contains(_increaseButton))
            {
                // Slider의 Column+1에 Increase 버튼 추가 (같은 행)
                _increaseButton.SetValue(Grid.ColumnProperty, sliderColumn + 1);
                _increaseButton.SetValue(Grid.RowProperty, 0);
                parent.Children.Insert(sliderIndex + 2, _increaseButton); // Decrease버튼 때문에 index 변동 대비 +2
            }
        }
    }

    private void AddButtonsToDockPanel(DockPanel parent)
    {
        if (_slider == null) return;

        // 버튼 생성
        CreateButtons();
        if (_decreaseButton == null || _increaseButton == null) return;

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
            if (!parent.Children.Contains(_decreaseButton))
            {
                parent.Children.Insert(sliderIndex - 1, _decreaseButton);
            }
            if (!parent.Children.Contains(_increaseButton))
            {
                parent.Children.Insert(parent.Children.Count - 1, _increaseButton);
            }
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
        _clickCount = 0;
        _isHoldMode = false;
        _buttonPressTime = DateTime.Now;

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
            return;
        }

        // 일반 단일 클릭: 값 적용
        if (_slider != null)
        {
            _slider.GetBindingExpression(Slider.ValueProperty)?.UpdateSource();
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

        // 첫 번째 Interval 클릭: 홀로 진입했음을 표시하고 값 변경 안 함
        if (_clickCount == 0)
        {
            _isHoldMode = true;  // 홀드 모드 시작
            _clickCount++;
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

        // 첫 번째 Interval 클릭: 홀로 진입했음을 표시하고 값 변경 안 함
        if (_clickCount == 0)
        {
            _isHoldMode = true;  // 홀드 모드 시작
            _clickCount++;
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
