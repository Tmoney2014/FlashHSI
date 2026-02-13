using System.ComponentModel;
using System.Windows;
using FlashHSI.UI.ViewModels;

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
    
    /// <ai>AI가 작성함: 윈도우 종료 시 안전 종료 처리 (하드웨어 OFF, 설정 저장)</ai>
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            // 비동기 종료 처리를 위해 창 닫기를 일시 보류
            e.Cancel = true;
            
            // 종료 안전 처리 실행
            await vm.WindowClosingCommand.ExecuteAsync(null);
            
            // 이벤트 해제 후 실제 종료
            Closing -= Window_Closing;
            Close();
        }
    }
}