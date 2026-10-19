using LyraSAT.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddDbContext<LyraSatDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"),
        npgsqlOptions => npgsqlOptions.CommandTimeout(300)));

builder.Services.AddControllers();

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Cấu hình CORS cho phép Frontend kết nối
builder.Services.AddCors(options =>
    options.AddPolicy("AllowAll", policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

// Configure the HTTP request pipeline.
// Đã mở khóa Swagger cho cả môi trường Production để dễ kiểm tra
app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

// Phải đặt UseCors trước UseAuthorization và MapControllers
app.UseCors("AllowAll");

app.UseAuthorization();

app.MapControllers();

app.Run();