using System;
using System.Windows;
using System.Windows.Controls;

namespace FlashHSI.UI.Services
{
    /// <summary>
    /// <ai>AI가 작성함</ai>
    /// ViewModels에서 직접 Window를 생성하지 않고 다이얼로그를 제어하기 위한 서비스입니다.
    /// </summary>
    public interface IWindowModalService
    {
        bool? ShowDialog(UserControl content, string title);
        void ShowMessage(string message, string title);
    }

    public class WindowModalService : IWindowModalService
    {
        public bool? ShowDialog(UserControl content, string title)
        {
            var window = new Window
            {
                Title = title,
                Content = content,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize
            };
            return window.ShowDialog();
        }

        public void ShowMessage(string message, string title)
        {
            MessageBox.Show(message, title);
        }
    }
}
