// Runs the fake cTrader on its own, for trying the app locally:
//   dotnet run --project tests/FakeCTrader -- 5099
// then start the app with CTrader__AuthorizeUrl=http://127.0.0.1:5099/authorize,
// CTrader__TokenUrl=http://127.0.0.1:5099/apps/token, CTrader__DemoEndpoint=ws://127.0.0.1:5099/
// and CTrader__ClientId / CTrader__ClientSecret = fake-client-id / fake-client-secret.
using FakeCTrader;

var port = args.Length > 0 ? int.Parse(args[0]) : 5099;
await using var server = await FakeCTraderServer.StartAsync(port);
Console.WriteLine($"Fake cTrader listening on {server.BaseUrl}");
await Task.Delay(Timeout.Infinite);
