using Assistant.WireMock;
using WireMock.Server;
using WireMock.Settings;

using var server = WireMockServer.Start(new WireMockServerSettings
{
    Urls = ["http://0.0.0.0:8080"],
    StartAdminInterface = true,
});

TelegramStubs.Install(server);

Console.WriteLine("Stub API listening on http://0.0.0.0:8080");

await Task.Delay(Timeout.Infinite);
