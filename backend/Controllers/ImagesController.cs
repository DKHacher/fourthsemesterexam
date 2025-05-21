using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ImagesController : ControllerBase
    {
        // GET api/images
        [HttpGet]
        public IActionResult Get()
        {
            // Example: return OK with a test message
            return Ok("Images API is working!");
        }
        
    }
}