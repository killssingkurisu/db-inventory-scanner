using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using DbScanner.Core;
using DbScanner.UI;
using DbScanner.Win;

namespace DbScanner
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Native.MakeDpiAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Catalog catalog;
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("catalog.json"))
            using (var r = new StreamReader(s))
                catalog = Catalog.Load(r.ReadToEnd());
            Application.Run(new MainForm(catalog));
        }
    }
}
