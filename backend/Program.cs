using System.Text.Json;
using TicketBookingPoc.Services;

// แก้ไขจาก builder.Args เป็น args
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(options => {
    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

// เพิ่ม Dapr Client เข้าสู่ DI Container
builder.Services.AddDaprClient();

builder.Services.AddCors(options => {
    options.AddPolicy("AllowAll", p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

// 👇 สั่งให้ .NET รันระบบ Warm-up เป็น Background Task อัตโนมัติ
builder.Services.AddHostedService<NatsWarmupService>();

var app = builder.Build();

app.UseCors("AllowAll");
//app.UseCloudEvents(); 
app.MapControllers();
//app.MapSubscribeHandler(); 

app.Run();