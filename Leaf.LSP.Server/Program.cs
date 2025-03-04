using Leaf.LSP.Server;
using System.Text;

internal class Program
{
    private static void Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(); // UTF8N for non-Windows platform
        var app = new LeafLanguageServer(Console.OpenStandardInput(), Console.OpenStandardOutput());
        Logger.Instance.Attach(app);
        try
        {
            app.Listen().Wait();
        }
        catch (AggregateException ex)
        {
            Console.Error.WriteLine(ex.InnerExceptions[0]);
            Environment.Exit(-1);
        }
    }
}