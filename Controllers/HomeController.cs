using CustomerSupportAI.Models;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace CustomerSupportAI.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        return User.Identity?.IsAuthenticated == true ? RedirectToAction("Index", "Dashboard") : View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
