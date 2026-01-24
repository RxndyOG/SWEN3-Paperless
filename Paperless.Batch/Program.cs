using Paperless.Batch;
using Paperless.Contracts;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHostedService<AccessBatchWorker>();

var host = builder.Build();
host.Run();
