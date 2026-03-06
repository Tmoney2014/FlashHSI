using System.ComponentModel;
using System.Windows;
using FlashHSI.UI.ViewModels;
using MaterialDesignThemes.Wpf;

namespace FlashHSI;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 종료 버튼 클릭 - Confirm Dialog 표시 후 종료
    /// </summary>
    private async void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        // Confirm Dialog 표시
        var confirmDialog = CreateConfirmDialog();
        var result = await DialogHost.Show(confirmDialog, "RootDialog");

        // 확인을 누른 경우 종료 로직 실행
        if (result is true)
        {
            // ViewModel의 종료 로직 실행 (오버레이 표시됨)
            await vm.WindowClosingCommand.ExecuteAsync(null);
        }
    }

    /// <summary>
    /// Confirm Dialog 생성
    /// </summary>
    private static System.Windows.Controls.StackPanel CreateConfirmDialog()
    {
        var panel = new System.Windows.Controls.StackPanel
        {
            Width = 350,
            Margin = new Thickness(16)
        };

        // 아이콘
        panel.Children.Add(new PackIcon
        {
            Kind = PackIconKind.HelpCircle,
            Width = 48,
            Height = 48,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 16)
        });

        // 제목
        panel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = "Confirm Exit",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12)
        });

        // 메시지
        panel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = "Are you sure you want to exit?",
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 24)
        });

        // 버튼 영역
        var buttonPanel = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        // 취소 버튼
        var cancelButton = new System.Windows.Controls.Button
        {
            Content = "Cancel",
            Width = 100,
            Height = 36,
            Margin = new Thickness(0, 0, 12, 0),
            Style = (Style)Application.Current.FindResource("MaterialDesignOutlinedButton"),
            IsCancel = true
        };
        cancelButton.Click += (s, args) => DialogHost.CloseDialogCommand.Execute(false, null);

        // 확인 버튼
        var okButton = new System.Windows.Controls.Button
        {
            Content = "Exit",
            Width = 100,
            Height = 36,
            Style = (Style)Application.Current.FindResource("MaterialDesignRaisedButton"),
            IsDefault = true
        };
        okButton.Click += (s, args) => DialogHost.CloseDialogCommand.Execute(true, null);

        buttonPanel.Children.Add(cancelButton);
        buttonPanel.Children.Add(okButton);
        panel.Children.Add(buttonPanel);

        return panel;
    }

    /// <summary>
    /// 윈도우 종료 시 Confirm Dialog 표시
    /// </summary>
    private async void Window_Closing(object sender, CancelEventArgs e)
    {
        e.Cancel = true; // 창 닫기 일시 중단

        if (DataContext is MainViewModel vm)
        {
            var confirmDialog = CreateConfirmDialog();
            var result = await DialogHost.Show(confirmDialog, "RootDialog");

            if (result is true)
            {
                await vm.WindowClosingCommand.ExecuteAsync(null);
            }
        }
    }
}
