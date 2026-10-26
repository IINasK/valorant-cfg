using System;
using System.Linq;
using System.Windows.Forms;

namespace ValCfgSync;

static class Program
{
    // Um exe só (WinExe: nunca abre console):
    //   sem argumentos  menu
    //   --watcher       watcher minimizado na bandeja (usado pelo início com o Windows e pelo menu)
    [STAThread]
    static void Main(string[] args)
    {
        var vcs = new Vcs(new RiotClient());
        vcs.MigrateLegacyProfile();
        if (args.Contains("--watcher")) { Watcher.Run(vcs, Application.ExecutablePath); return; }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var form = new MainForm(vcs);
        Application.ThreadException += (_, e) => form.ShowError(e.Exception);   // erros aparecem no cartão, sem popup
        Application.Run(form);
    }
}
