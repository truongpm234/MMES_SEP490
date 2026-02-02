using AMMS.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AMMS.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ProductsController : ControllerBase
    {
        private readonly IProductService _service;
        public ProductsController(IProductService service)
        {
            _service = service;
        }

        [HttpGet("get-all-products")]
        public async Task<IActionResult> GetAllProducts(CancellationToken ct)
        {
            var products = await _service.GetAllActiveAsync(ct);
            return Ok(products);
        }

        [HttpGet("get-product/{productId:int}")]
        public async Task<IActionResult> GetProductById(int productId, CancellationToken ct)
        {
            var product = await _service.GetByIdAsync(productId, ct);
            if (product == null)
                return NotFound(new { message = "Product not found" });
            return Ok(product);
        }
    }
}
