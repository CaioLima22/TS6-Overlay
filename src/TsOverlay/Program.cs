using Velopack;

namespace TsOverlay;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Precisa ser a primeira coisa: o Velopack chama o exe com argumentos próprios
        // na instalação, atualização e desinstalação, e sai logo em seguida
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
