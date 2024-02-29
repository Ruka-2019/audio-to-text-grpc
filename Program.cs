using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Grpc.AspNetCore.Web;
using Grpc.Core;
using SpeechRecognitionGrpcService;
using SpeechRecognitionService = Services.SpeechRecognitionService;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddGrpc();
builder.Services.AddGrpc().AddServiceOptions<SpeechRecognitionService>(options =>
{
    options.EnableDetailedErrors = true; // Useful for development and debugging
});
builder.Services.AddSingleton<SpeechRecognitionService>();
builder.Services.AddRouting();

// Add CORS policy for localhost
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowLocalhostAnyPort",
        builder =>
        {
            builder.SetIsOriginAllowed(origin => new Uri(origin).Host == "localhost")
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        });
});


var app = builder.Build();

// Use CORS with the specified policy
app.UseCors("AllowLocalhostAnyPort");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();
app.UseGrpcWeb();
app.UseHttpsRedirection();
app.UseEndpoints(endpoints =>
{
    endpoints.MapGrpcService<SpeechRecognitionService>().EnableGrpcWeb();
    endpoints.MapGet("/", () => "This app provides a gRPC service for speech recognition."); // Example default endpoint
});


app.Run();
