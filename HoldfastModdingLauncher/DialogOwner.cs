using System.Windows.Forms;
using System.Drawing;

namespace HoldfastModdingLauncher
{
    /// <summary>
    /// Centers borderless dialogs on the launcher. CenterParent with no owner opens at 0,0.
    /// </summary>
    internal static class DialogOwner
    {
        public static Form Find()
        {
            if (Form.ActiveForm is { IsDisposed: false, Visible: true } active)
                return active;

            foreach (Form form in Application.OpenForms)
            {
                if (!form.IsDisposed && form.Visible && form is MainForm)
                    return form;
            }

            foreach (Form form in Application.OpenForms)
            {
                if (!form.IsDisposed && form.Visible)
                    return form;
            }

            return null;
        }

        public static DialogResult ShowCentered(Form dialog)
        {
            var owner = Find();
            dialog.StartPosition = FormStartPosition.Manual;
            if (owner == null)
            {
                dialog.StartPosition = FormStartPosition.CenterScreen;
                return dialog.ShowDialog();
            }

            CenterOn(dialog, owner);
            dialog.Load += (s, e) => CenterOn(dialog, owner);
            return dialog.ShowDialog(owner);
        }

        public static void CenterOn(Form dialog, Form owner)
        {
            if (dialog == null || owner == null || owner.IsDisposed)
                return;

            int x = owner.Left + (owner.Width - dialog.Width) / 2;
            int y = owner.Top + (owner.Height - dialog.Height) / 2;
            dialog.Location = new Point(x, y);
        }
    }
}
