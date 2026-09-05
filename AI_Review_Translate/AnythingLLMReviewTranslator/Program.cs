namespace AnythingLLMReviewTranslator;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        // WinForms apps do not always have a valid console handle; avoid assigning Console.Encoding here.
        // File logging already uses UTF-8 explicitly where needed.
        ApplicationConfiguration.Initialize();
        Application.Run(new Form1());
    }
}