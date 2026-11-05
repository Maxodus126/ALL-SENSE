using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AllSense.Setup;

public sealed class InstallerForm : Form
{
    private readonly TextBox _dir;
    private readonly ProgressBar _bar;
    private readonly Label _status;
    private readonly Button _install;
    private readonly Button _cancel;

    public InstallerForm()
    {
        Text = "AllSense Control — Pemasangan";
        ClientSize = new Size(520, 300);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(11, 18, 28);
        ForeColor = Color.FromArgb(234, 240, 248);
        Font = new Font("Segoe UI", 9.5f);
        // Ikon logo di title bar (dari exe setup).
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!); } catch { }

        // Header
        var header = new Panel { Dock = DockStyle.Top, Height = 78, BackColor = Color.FromArgb(17, 25, 37) };
        header.Paint += (_, e) =>
        {
            using var f = new Font("Segoe UI", 16f, FontStyle.Bold);
            using var fb = new SolidBrush(Color.FromArgb(255, 45, 77));      // merah AllSense
            using var fw = new SolidBrush(Color.FromArgb(234, 240, 248));
            e.Graphics.DrawString("AllSense Control", f, fw, 24, 18);
            using var sub = new Font("Segoe UI", 9f);
            using var fg = new SolidBrush(Color.FromArgb(111, 127, 154));
            e.Graphics.DrawString("v" + Program.DisplayVersion + "  ·  Laptop control center", sub, fg, 25, 50);
            // aksen: segi empat merah kecil
            e.Graphics.FillRectangle(fb, 0, 0, 6, 78);
        };

        var lbl = new Label
        {
            Text = "Folder pemasangan (per-user, tanpa admin):",
            Left = 24, Top = 100, AutoSize = true,
        };
        _dir = new TextBox
        {
            Left = 24, Top = 124, Width = 392,
            Text = SetupCore.DefaultInstallDir,
            BackColor = Color.FromArgb(17, 25, 37), ForeColor = ForeColor, BorderStyle = BorderStyle.FixedSingle,
        };
        var browse = new Button { Text = "...", Left = 424, Top = 122, Width = 44, Height = 27 };
        browse.Click += (_, _) =>
        {
            using var fbd = new FolderBrowserDialog { Description = "Pilih folder pemasangan AllSense" };
            if (fbd.ShowDialog(this) == DialogResult.OK) _dir.Text = fbd.SelectedPath;
        };

        _bar = new ProgressBar
        {
            Left = 24, Top = 168, Width = 444, Height = 18,
            Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 20, Visible = false,
        };
        _status = new Label
        {
            Left = 24, Top = 192, Width = 444,
            ForeColor = Color.FromArgb(174, 189, 211), Text = "Klik 'Pasang' untuk memulai.", AutoSize = false,
        };

        _install = new Button
        {
            Text = "Pasang", Left = 230, Top = 248, Width = 120, Height = 34,
            BackColor = Color.FromArgb(255, 45, 77), ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
        };
        _install.FlatAppearance.BorderSize = 0;
        _install.Click += Install_Click;

        _cancel = new Button
        {
            Text = "Batal", Left = 360, Top = 248, Width = 108, Height = 34,
            BackColor = Color.FromArgb(23, 34, 48), ForeColor = ForeColor,
            FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
        };
        _cancel.FlatAppearance.BorderSize = 0;
        _cancel.Click += (_, _) => Close();

        Controls.Add(header);
        Controls.Add(lbl);
        Controls.Add(_dir);
        Controls.Add(browse);
        Controls.Add(_bar);
        Controls.Add(_status);
        Controls.Add(_install);
        Controls.Add(_cancel);
    }

    private async void Install_Click(object? sender, EventArgs e)
    {
        _install.Enabled = false;
        _cancel.Enabled = false;
        _bar.Visible = true;
        _status.Text = "Memasang…";
        try
        {
            await Task.Run(() => Program.Install(_dir.Text.Trim(), quiet: true));
            _bar.Visible = false;
            _status.Text = "Selesai. AllSense terpasang.";
            if (MessageBox.Show(this, "AllSense Control terpasang.\nJalankan sekarang?", "AllSense",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    Path.Combine(_dir.Text.Trim(), Program.AppExe)) { UseShellExecute = true });
            }
            Close();
        }
        catch (Exception ex)
        {
            _bar.Visible = false;
            _status.Text = "Gagal: " + ex.Message;
            _install.Enabled = true;
            _cancel.Enabled = true;
        }
    }
}
