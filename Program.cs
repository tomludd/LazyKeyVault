using Terminal.Gui;
using LazyAzureKeyVault.Views;

namespace LazyAzureKeyVault;

class Program
{
    static async Task Main(string[] args)
    {
        Application.Init();
        
        try
        {
            var mainWindow = new MainWindow();
            
            // Schedule initialization after the main loop starts
            Application.Invoke(async () =>
            {
                await mainWindow.InitializeAsync();
            });
            
            Application.Run(mainWindow);
        }
        finally
        {
            Application.Shutdown();
        }
    }
}
