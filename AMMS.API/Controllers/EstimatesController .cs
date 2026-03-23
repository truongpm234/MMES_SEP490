using AMMS.Application.Interfaces;
using AMMS.Shared.DTOs.Estimates;
using AMMS.Shared.DTOs.Planning;
using Microsoft.AspNetCore.Mvc;

namespace AMMS.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EstimatesController : ControllerBase
    {
        private readonly IEstimateService _service;
        private readonly IBaseConfigService _baseConfig;
        private readonly IOrderPlanningService _planning;

        public EstimatesController(IEstimateService service, IBaseConfigService baseConfig, IOrderPlanningService planning)
        {
            _service = service;
            _baseConfig = baseConfig;
            _planning = planning;
        }

        [HttpGet("estimate-finish/{orderRequestId:int}")]
        public async Task<ActionResult<EstimateFinishDateResponse>> EstimateFinish(int orderRequestId, CancellationToken ct)
        {
            var res = await _planning.EstimateFinishByOrderRequestAsync(orderRequestId, ct);
            if (res == null) return NotFound();
            return Ok(res);
        }

        [HttpPut("adjust-cost/{orderRequestId:int}")]
        public async Task<IActionResult> AdjustCost(int orderRequestId, [FromBody] AdjustCostRequest req)
        {
            await _service.UpdateFinalCostAsync(orderRequestId, req.final_cost);
            return NoContent();
        }

        [HttpGet("deposit/by-request/{requestId:int}")]
        public async Task<IActionResult> GetDepositByRequestId(int requestId, CancellationToken ct)
        {
            var result = await _service.GetDepositByRequestIdAsync(requestId, ct);
            if (result == null)
                return NotFound(new { message = "Cost estimate not found for this requestId" });

            return Ok(result);
        }

        [HttpGet("base-config")]
        [ProducesResponseType(typeof(EstimateBaseConfigDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetBaseConfig(CancellationToken ct)
        {
            var cfg = await _baseConfig.GetAsync(ct);
            return Ok(cfg);
        }

        [HttpPost("cost-save")]
        public async Task<IActionResult> SaveFeCost([FromBody] CostEstimateInsertRequest req, CancellationToken ct)
        {
            if (req.order_request_id <= 0)
                return BadRequest(new { message = "order_request_id is required and must be greater than 0" });

            var exists = await _service.OrderRequestExistsAsync(req.order_request_id);
            if (!exists)
                return NotFound(new { message = $"Order request with id {req.order_request_id} not found" });

            var estimateId = await _service.SaveFeCostEstimateAsync(req, ct);

            return Ok(new { estimate_id = estimateId });
        }

        [HttpGet("get-all-deal-by-{request_id:int}")]
        public async Task<IActionResult> GetAllEstimatesFlat([FromRoute] int request_id, CancellationToken ct)
        {
            var list = await _service.GetAllEstimatesFlatByRequestIdAsync(request_id, ct);

            if (list.Count == 0)
                return NotFound(new { message = "Order request not found OR no estimates for this request" });

            return Ok(list);
        }

        [HttpGet("email-preview/{requestId:int}")]
        public async Task<IActionResult> QuotePreviewByRequest(int requestId, CancellationToken ct)
        {
            var res = await _service.BuildPreviewAsync(requestId, ct);
            return Ok(res);
        }

        [HttpPost("upload-contract")]
        [RequestSizeLimit(50_000_000)]
        public async Task<IActionResult> UploadContract(
            [FromForm] UploadEstimateContractRequest req,
            CancellationToken ct)
        {
            try
            {
                if (req.request_id <= 0)
                    return BadRequest(new { message = "request_id must be > 0" });

                if (req.estimate_id <= 0)
                    return BadRequest(new { message = "estimate_id must be > 0" });

                if (req.file == null || req.file.Length == 0)
                    return BadRequest(new { message = "file is required" });

                var allowedExtensions = new[] { ".pdf", ".doc", ".docx" };
                var ext = Path.GetExtension(req.file.FileName)?.ToLowerInvariant();

                if (string.IsNullOrWhiteSpace(ext) || !allowedExtensions.Contains(ext))
                {
                    return BadRequest(new
                    {
                        message = "Only .pdf, .doc, .docx files are allowed"
                    });
                }

                await using var stream = req.file.OpenReadStream();

                var url = await _service.UploadContractFileAsync(
                    req.request_id,
                    req.estimate_id,
                    stream,
                    req.file.FileName,
                    req.file.ContentType ?? "application/octet-stream",
                    ct);

                return Ok(new
                {
                    message = "Upload contract file successfully",
                    request_id = req.request_id,
                    estimate_id = req.estimate_id,
                    contract_file_path = url
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    message = "Upload contract file failed",
                    detail = ex.Message
                });
            }
        }

        [HttpPost("upload-couple-contract")]
        [RequestSizeLimit(100_000_000)]
        public async Task<IActionResult> UploadContractBatch(
    [FromForm] UploadEstimateContractsBatchRequest req,
    CancellationToken ct)
        {
            try
            {
                if (req.request_id <= 0)
                    return BadRequest(new { message = "request_id must be > 0" });

                if (req.files == null || req.files.Count == 0)
                    return BadRequest(new { message = "At least 1 file is required" });

                if (req.files.Count > 2)
                    return BadRequest(new { message = "Only up to 2 files are allowed per upload" });

                var allowedExtensions = new[] { ".pdf" };

                foreach (var file in req.files)
                {
                    if (file == null || file.Length == 0)
                        return BadRequest(new { message = "One of the uploaded files is empty" });

                    var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(ext) || !allowedExtensions.Contains(ext))
                    {
                        return BadRequest(new
                        {
                            message = $"File '{file.FileName}' is invalid. Only .pdf files are allowed"
                        });
                    }
                }

                var estimateIds = await _service.GetActiveEstimateIdsForContractUploadAsync(req.request_id, ct);

                if (req.files.Count > estimateIds.Count)
                {
                    return BadRequest(new
                    {
                        message = $"This request only has {estimateIds.Count} active estimate(s), but received {req.files.Count} file(s)"
                    });
                }

                var results = new List<UploadEstimateContractBatchItemResponse>();

                for (int i = 0; i < req.files.Count; i++)
                {
                    var file = req.files[i];
                    var estimateId = estimateIds[i];

                    await using var stream = file.OpenReadStream();

                    var url = await _service.UploadContractFileAsync(
                        req.request_id,
                        estimateId,
                        stream,
                        file.FileName,
                        file.ContentType ?? "application/octet-stream",
                        ct);

                    results.Add(new UploadEstimateContractBatchItemResponse
                    {
                        file_index = i,
                        original_file_name = file.FileName,
                        estimate_id = estimateId,
                        contract_file_path = url
                    });
                }

                return Ok(new
                {
                    message = "Upload contract files successfully",
                    request_id = req.request_id,
                    uploaded_count = results.Count,
                    uploads = results
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    message = "Upload contract files failed",
                    detail = ex.Message
                });
            }
        }
    }
}
