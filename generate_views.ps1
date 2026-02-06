$views = @("HomeView", "LiveView", "StatisticView", "SettingView", "LogView")
$dir = "c:\Users\user16g\Desktop\FlashHSI\FlashHSI.UI\Views\Pages"
if (!(Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir }

foreach ($view in $views) {
    $xaml = @"
<UserControl x:Class=""FlashHSI.UI.Views.Pages.$view""
             xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
             xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml""
             xmlns:mc=""http://schemas.openxmlformats.org/markup-compatibility/2006"" 
             xmlns:d=""http://schemas.microsoft.com/expression/blend/2008"" 
             mc:Ignorable=""d"" 
             d:DesignHeight=""450"" d:DesignWidth=""800"">
    <Grid Background=""White"">
        <TextBlock Text=""$view Placeholder"" FontSize=""32"" HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
    </Grid>
</UserControl>
"@
    $cs = @"
using System.Windows.Controls;

namespace FlashHSI.UI.Views.Pages
{
    public partial class $view : UserControl
    {
        public $view()
        {
            InitializeComponent();
        }
    }
}
"@
    $xaml | Out-File "$dir\$view.xaml" -Encoding UTF8
    $cs | Out-File "$dir\$view.xaml.cs" -Encoding UTF8
}
