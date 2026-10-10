using System.Text;
using SevenLive.Cli;

try
{
    Console.OutputEncoding = Encoding.UTF8;
}
catch
{
}

return CliRunner.Run(args);
