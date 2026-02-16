using gRpcBroker.Interfaces;
using gRpcBroker.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();
builder.Services.AddSingleton<IRoomRegistry, InMemoryRoomRegistry>();

var app = builder.Build();

app.MapGrpcService<BrokerService>();

app.Run();
