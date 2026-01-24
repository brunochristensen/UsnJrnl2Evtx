namespace UsnJrnl2Evtx
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = Host.CreateApplicationBuilder(args);
            builder.Services.AddHostedService<Worker>();

            builder.Services.AddWindowsService();

            var host = builder.Build();
            host.Run();
        }
    }
}
