using System.Windows;

namespace iikoServiceHelper
{
    public partial class OverlayWindow : Window
    {
        public OverlayWindow()
        {
            InitializeComponent();
            this.Left = 0;
            this.Top = 0;
        }

        public void ShowMessage(string text)
        {
            txtMessage.Text = text;
            this.Show();
        }

        public void HideMessage()
        {
            this.Hide();
        }
    }
}