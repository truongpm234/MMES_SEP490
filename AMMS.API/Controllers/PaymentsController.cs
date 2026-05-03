using AMMS.Application.Interfaces;
using AMMS.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AMMS.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PaymentsController : ControllerBase
    {
        private readonly IPaymentsService _paymentsService;

        public PaymentsController(IPaymentsService paymentsService)
        {
            _paymentsService = paymentsService;
        }

        [AllowAnonymous]
        [HttpGet("receipt/{orderCode:long}")]
        public async Task<IActionResult> GetReceiptByOrderCode(long orderCode, CancellationToken ct)
        {
            if (orderCode <= 0)
            {
                return BadRequest(new
                {
                    message = "orderCode must be greater than 0"
                });
            }

            var receipt = await _paymentsService.GetReceiptByOrderCodeAsync(orderCode, ct);

            if (receipt == null)
            {
                return NotFound(new
                {
                    message = "Payment receipt not found",
                    order_code = orderCode
                });
            }

            if (!string.Equals(receipt.payment_status, "PAID", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(receipt.payment_status, "SUCCESS", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(receipt.payment_status, "PENDING", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(receipt.payment_status, "PendingPaid", StringComparison.OrdinalIgnoreCase)
                )
            {
                return BadRequest(new
                {
                    message = "Payment is not completed yet",
                    order_code = orderCode,
                    status = receipt.payment_status
                });
            }

            return Ok(receipt);
        }

        [AllowAnonymous]
        [HttpGet("payment-receipt-docx/{orderCode:long}")]
        public async Task<IActionResult> GeneratePaymentReceiptDocx(
    [FromRoute] long orderCode,
    CancellationToken ct)
        {
            try
            {
                if (orderCode <= 0)
                {
                    return BadRequest(new
                    {
                        message = "orderCode must be greater than 0"
                    });
                }

                var result = await _paymentsService.GenerateReceiptPdfByOrderCodeAsync(orderCode, ct);

                if (result == null)
                {
                    return NotFound(new
                    {
                        message = "Payment not found",
                        order_code = orderCode
                    });
                }

                return File(result.Value.FileBytes, result.Value.ContentType, result.Value.FileName);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new
                {
                    message = ex.Message,
                    order_code = orderCode
                });
            }
            catch (FileNotFoundException ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    message = "Receipt template file not found",
                    detail = ex.Message
                });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    message = "Generate payment receipt failed",
                    detail = ex.Message
                });
            }
        }
    }
}
