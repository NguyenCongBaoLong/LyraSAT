using LyraSAT.Data;
using LyraSAT.DTOs;
using LyraSAT.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LyraSAT.Controllers
{
    /// <summary>
    /// Controller quản trị: tài khoản, lớp học, giáo viên và học sinh.
    /// Được tách thành các file partial theo nhóm chức năng, route và hành vi giữ nguyên.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public partial class AdminController : ControllerBase
    {
        private readonly LyraSatDbContext _context;
        private readonly PasswordHasher<User> _passwordHasher = new PasswordHasher<User>(); // Khởi tạo công cụ băm mật khẩu

        public AdminController(LyraSatDbContext context)
        {
            _context = context;
        }
    }
}
