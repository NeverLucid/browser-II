using System;
using System.Windows.Forms;

namespace MyBrowserShell
{
    
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            _ = BrowserRuntime.GetEnvironmentAsync();
            Application.Run(new Form1());
        }
    }
}


