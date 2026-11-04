using System;
using System.IO;
using System.Windows.Forms;

namespace AllSense.Setup;

public sealed class InstallerForm : Form
{
    private readonly TextBox _dir;

    public InstallerForm()
    {
        Text = "AllSense Control — Pemasangan";
        ClientSize = new System.Drawing.Size(460, 200);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        var title = new Label
        {
            Text = "AllSense Control v" + Program.DisplayVersion,
            Left = 20, Top = 18, AutoSize = true,
            Font = new System.Drawing.Font("Segoe UI", 12, System.Drawing.FontStyle.Bold),
        };
        var lbl = new Label { Text = "Folder pemasangan (per-user, tanpa admin):", Left = 20, Top = 54, AutoSize = true };
        _dir = new TextBox
        {
            Left = 20, Top = 78, Width = 340,
            Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "AllSense"),
        };
        var browse = new Button { Text = "...", Left = 366, Top = 76, Width = 40 };
        browse.Click += (_, _) =>
        {
            using var fbd = new FolderBrowserDialog();
            if (fbd.ShowDialog(this) == DialogResult.OK) _dir.Text = fbd.SelectedPath;
        };

        var install = new Button { Text = "Pasang", Left = 120, Top = 128, Width = 110 };
        install.Click += (_, _) =>
        {
            install.Enabled = false;
            try { Program.Install(_dir.Text.Trim(), quiet: false); Close(); }
            catch (Exception ex) { MessageBox.Show("Gagal: " + ex.Message, "AllSense", MessageBoxButtons.OK, MessageBoxIcon.Error); install.Enabled = true; }
        };
        var cancel = new Button { Text = "Batal", Left = 250, Top = 128, Width = 110 };
        cancel.Click += (_, _) => Close();

        Controls.Add(title);
        Controls.Add(lbl);
        Controls.Add(_dir);
        Controls.Add(browse);
        Controls.Add(install);
        Controls.Add(cancel);
    }
}
