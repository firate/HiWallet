using System.Text.Json.Serialization;
using HiWallet.WalletService.Setup;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

var builder = WebApplication.CreateBuilder(args);

// Wolverine YALNIZCA in-process mediator olarak: RabbitMQ transport'u, durable
// inbox/outbox'ı ve Saga persistence'ı kullanılmıyor (decisions.md madde 1).
builder.Host.UseWolverine();

builder.Services.AddHiWalletPersistence();
builder.Services.AddHiWalletPolicies(builder.Configuration);
builder.Services.AddHiWalletValidation();
builder.Services.AddHiWalletProblemDetails();

builder.Services
    .AddControllers(options => options.Filters.AddService<ValidationFilter>())
    .AddJsonOptions(options =>
        // Enum'lar sözleşmede İSİM olarak geçer: "P2P", sayı değil. Sayı olsaydı
        // enum'a yeni bir değer eklemek mevcut client'ların anlamını kaydırırdı.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.ValidateHiWalletConfiguration();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

app.Run();

// Integration testler WebApplicationFactory<Program> ile ayağa kaldırır.
public partial class Program;
