namespace ScheduleICompanion.Installer;

internal static class Program
{
    [STAThread]
    private static async Task Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length == 2 && args[0].Equals("--silent-update", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var backpackEnabled = File.Exists(Path.Combine(args[1], "Mods", "ScheduleICompanion.Backpack.dll"));
                var service = new InstallationService();
                await service.InstallAsync(args[1], installMelonLoader: false, createDesktopShortcut: false,
                    new Progress<string>(_ => { }), CancellationToken.None, installBackpack: backpackEnabled);
                InstallationService.LaunchCompanion(args[1]);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Companion update failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return;
        }
        Application.Run(new InstallerForm());
    }
}
