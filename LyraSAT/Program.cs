using LyraSAT.Data;
using LyraSAT.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddDbContext<LyraSatDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"),
        npgsqlOptions => npgsqlOptions.CommandTimeout(300)));

builder.Services.AddControllers();

// Video chữa bài của giáo viên có thể vài chục MB, mặc định multipart chỉ 128MB
// và bị Kestrel chặn sớm; nâng lên để endpoint upload video nhận được file.
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 160L * 1024 * 1024;
});

// Tầng service dùng chung
builder.Services.AddScoped<INotificationService, NotificationService>();

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

app.UseStaticFiles();

app.UseAuthorization();

app.MapControllers();

app.Run();
