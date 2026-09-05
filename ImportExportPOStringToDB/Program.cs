namespace ImportPOStringToDB;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        AppSettings settings;
        try
        {
            settings = AppSettings.Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Không đọc được file appsettings.json.\n\n{ex.Message}",
                "Import PO String To DB",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        Application.Run(new MainForm(settings));
    }
}
