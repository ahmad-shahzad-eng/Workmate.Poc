using Workmate.Poc;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseAutofac();

await builder.AddApplicationAsync<WorkmatePocModule>();

var app = builder.Build();
await app.InitializeApplicationAsync();
await app.RunAsync();
