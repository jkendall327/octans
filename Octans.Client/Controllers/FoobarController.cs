using Microsoft.AspNetCore.Mvc;

namespace Octans.Client.Controllers;

public class FoobarController : Controller
{
    public IActionResult AyyLmao(string path)
    {
        throw new Exception();
        return File(System.IO.File.OpenRead(path), "image/jpeg");
    }
}