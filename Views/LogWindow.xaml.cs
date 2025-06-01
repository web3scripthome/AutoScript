using System.Windows;
using System.Windows.Documents;

namespace web3script.Views
{
    public partial class LogWindow : Window
    {
        public LogWindow()
        {
            InitializeComponent();
        }

        public void AppendLog(string message)
        {
            var document = LogTextBox.Document;
            var paragraphs = document.Blocks.OfType<Paragraph>().ToList();

            // �������100�������
            if (paragraphs.Count >= 100)
            {
                document.Blocks.Clear();
            }

            // �������־��
            document.Blocks.Add(new Paragraph(new Run(message)));

            // ������ĩβ
            LogTextBox.ScrollToEnd();
        }

        public void ClearLogs()
        {
            LogTextBox.Document.Blocks.Clear();
        }
    }
} 