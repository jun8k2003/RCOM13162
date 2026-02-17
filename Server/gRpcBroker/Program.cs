using gRpcBroker.Interfaces;
using gRpcBroker.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService();

builder.Services.AddGrpc();
builder.Services.AddSingleton<IRoomRegistry, InMemoryRoomRegistry>();

var app = builder.Build();

app.MapGrpcService<BrokerService>();

app.Run();
