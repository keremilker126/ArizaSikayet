using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using ArizaSikayet.Models;

namespace ArizaSikayet.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        return View();
    }


}
