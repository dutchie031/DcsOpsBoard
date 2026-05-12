using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DcsOpsBoard.Controllers
{
    [ApiController]
    [Route("/api/admin")]
    public class Health : ControllerBase
    {
        [HttpGet("health")]
        public IActionResult GetHealth()
        {
            return Ok(new { success = true, message= "healthy" });
        }
    }
}
