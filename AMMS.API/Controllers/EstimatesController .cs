using AMMS.Application.Interfaces;
using AMMS.Application.Services;
using AMMS.Infrastructure.Interfaces;
using AMMS.Shared.DTOs.Estimates;
using AMMS.Shared.DTOs.Planning;
using Microsoft.AspNetCore.Mvc;
using UglyToad.PdfPig.Core;

namespace AMMS.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EstimatesController : ControllerBase
    {
        private readonly IEstimateService _service;
        private readonly IBaseConfigService _baseConfig;
        private readonly IOrderPlanningService _planning;
        private readonly IAccessService _accessService;
        private readonly ICloudinaryFileStorageService _cloudinaryStorage;

        public EstimatesController(IEstimateService service, IBaseConfigService baseConfig, IOrderPlanningService planning, IAccessService accessService, ICloudinaryFileStorageService cloudinaryFileStorageService)
        {
            _service = service;
            _baseConfig = baseConfig;
            _planning = planning;
            _accessService = accessService;
            _cloudinaryStorage = cloudinaryFileStorageService;
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

        [HttpGet("remaining/by-request/{requestId:int}")]
        public async Task<IActionResult> GetRemainingByRequestId(int requestId, CancellationToken ct)
        {
            var result = await _service.GetRemainingByRequestIdAsync(requestId, ct);
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

        [HttpPost("upload-consultant-contract")]
        [RequestSizeLimit(50_000_000)]
        public async Task<IActionResult> UploadConsultantContract(
    [FromForm] UploadConsultantContractRequest req,
    CancellationToken ct)
        {
            try
            {
                if (req.request_id <= 0) return BadRequest(new { message = "request_id must be > 0" });
                if (req.estimate_id <= 0) return BadRequest(new { message = "estimate_id must be > 0" });
                if (req.file == null || req.file.Length == 0) return BadRequest(new { message = "file is required" });

                var ext = Path.GetExtension(req.file.FileName)?.ToLowerInvariant();
                if (ext != ".docx")
                    return BadRequest(new { message = "Only .docx is allowed" });

                await using var stream = req.file.OpenReadStream();

                var url = await _service.UploadConsultantContractAsync(
                    req.request_id,
                    req.estimate_id,
                    stream,
                    req.file.FileName,
                    req.file.ContentType ?? "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                    ct);

                return Ok(new
                {
                    message = "Upload consultant contract successfully",
                    request_id = req.request_id,
                    estimate_id = req.estimate_id,
                    consultant_contract_path = url
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
        }

        [HttpPost("upload-customer-signed-contract")]
        [RequestSizeLimit(50_000_000)]
        public async Task<IActionResult> UploadCustomerSignedContract(
            [FromForm] UploadCustomerSignedContractRequest req,
            CancellationToken ct)
        {
            try
            {
                if (req == null)
                    return LoiBadRequest("Thiếu dữ liệu tải lên hợp đồng khách hàng đã ký.");

                if (req.request_id <= 0)
                    return LoiBadRequest("request_id phải lớn hơn 0.");

                if (req.estimate_id <= 0)
                    return LoiBadRequest("estimate_id phải lớn hơn 0.");

                if (req.file == null || req.file.Length == 0)
                    return LoiBadRequest("Bạn chưa chọn file hợp đồng khách hàng đã ký.");

                var ext = Path.GetExtension(req.file.FileName)?.ToLowerInvariant();
                if (ext != ".pdf")
                    return LoiBadRequest("Hợp đồng khách hàng đã ký chỉ được phép tải lên file .pdf.");

                await using var stream = req.file.OpenReadStream();

                var result = await _service.UploadCustomerSignedContractAsync(
                    req.request_id,
                    req.estimate_id,
                    stream,
                    req.file.FileName,
                    req.file.ContentType ?? "application/pdf",
                    ct);

                if (result.customer_signed_contract_path == null)
                {
                    return LoiBadRequest(
                        TaoThongBaoLoiHopDongDaKy(result.compare_result),
                        new
                        {
                            request_id = result.request_id,
                            estimate_id = result.estimate_id,
                            compare_warning = result.compare_warning,
                            compare_result = result.compare_result
                        });
                }

                return ThanhCong("Tải lên hợp đồng khách hàng đã ký thành công.", new
                {
                    request_id = result.request_id,
                    estimate_id = result.estimate_id,
                    customer_signed_contract_path = result.customer_signed_contract_path,
                    compare_result = result.compare_result
                });
            }
            catch (PdfDocumentFormatException)
            {
                return LoiBadRequest("File PDF không hợp lệ hoặc nội dung file bị lỗi nên hệ thống không thể đọc để đối chiếu.");
            }
            catch (ArgumentException ex)
            {
                return LoiBadRequest("Dữ liệu tải lên hợp đồng khách hàng đã ký không hợp lệ.", ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return LoiKhongTimThay("Không tìm thấy yêu cầu, báo giá hoặc hợp đồng tư vấn để đối chiếu.", ex.Message);
            }
            catch (Exception ex)
            {
                return LoiHeThong("Tải lên hợp đồng khách hàng đã ký thất bại.", ex.Message);
            }
        }


        [HttpPut("alternative-materials")]
        public async Task<IActionResult> UpdateAlternativeMaterials([FromBody] UpdateAlternativeMaterialRequest req, CancellationToken ct)
        {
            if (req == null || req.request_id <= 0)
                return BadRequest(new { message = "request_id must be > 0" });

            await _service.UpdateAlternativeMaterialsAsync(
                req.request_id,
                req.estimate_id,
                req.paper_alternative,
                req.wave_alternative,
                req.alternative_material_reason,
                ct);

            return NoContent();
        }

        [HttpPost("generate-consultant-contract")]
        public async Task<IActionResult> GenerateConsultantContract(
    [FromBody] GenerateConsultantContractRequest req,
    CancellationToken ct)
        {
            try
            {
                if (req == null)
                    return BadRequest(new { message = "Request body is required" });

                if (req.request_id <= 0)
                    return BadRequest(new { message = "request_id must be > 0" });

                if (req.estimate_id <= 0)
                    return BadRequest(new { message = "estimate_id must be > 0" });

                var result = await _service.GenerateConsultantContractAsync(
                    req.request_id,
                    req.estimate_id,
                    ct);

                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (FileNotFoundException ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    message = "Contract template not found",
                    detail = ex.Message
                });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    message = "Generate consultant contract failed",
                    detail = ex.Message
                });
            }
        }

        [HttpPut("save-consultant-contract-path")]
        public async Task<IActionResult> SaveConsultantContractPath([FromBody] SaveConsultantContractPathRequest req, CancellationToken ct)
        {
            try
            {
                if (req == null)
                    return BadRequest(new { message = "Request body is required" });

                if (req.request_id <= 0)
                    return BadRequest(new { message = "request_id must be > 0" });

                if (req.estimate_id <= 0)
                    return BadRequest(new { message = "estimate_id must be > 0" });

                if (string.IsNullOrWhiteSpace(req.consultant_contract_path))
                    return BadRequest(new { message = "consultant_contract_path is required" });

                await _service.SaveConsultantContractPathAsync(
                    req.request_id,
                    req.estimate_id,
                    req.consultant_contract_path,
                    ct);

                return Ok(new
                {
                    message = "Save consultant_contract_path successfully",
                    request_id = req.request_id,
                    estimate_id = req.estimate_id,
                    consultant_contract_path = req.consultant_contract_path
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
                    message = "Save consultant_contract_path failed",
                    detail = ex.Message
                });
            }
        }

        private IActionResult ThanhCong(string thongBao, object? duLieu = null)
        {
            return Ok(new
            {
                thanh_cong = true,
                thong_bao = thongBao,
                du_lieu = duLieu
            });
        }

        private IActionResult LoiBadRequest(string thongBao, object? chiTiet = null)
        {
            return BadRequest(new
            {
                thanh_cong = false,
                thong_bao = thongBao,
                chi_tiet = chiTiet
            });
        }

        private IActionResult LoiKhongTimThay(string thongBao, object? chiTiet = null)
        {
            return NotFound(new
            {
                thanh_cong = false,
                thong_bao = thongBao,
                chi_tiet = chiTiet
            });
        }

        private IActionResult LoiHeThong(string thongBao, object? chiTiet = null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                thanh_cong = false,
                thong_bao = thongBao,
                chi_tiet = chiTiet
            });
        }

        private static string TaoThongBaoLoiHopDongDaKy(CompareContractResponse? compareResult)
        {
            if (compareResult == null)
                return "Hợp đồng khách hàng tải lên không hợp lệ.";

            var loi = new List<string>();

            if (!compareResult.body_text_exact_match && compareResult.similarity_percent < 95m)
            {
                loi.Add($"Nội dung hợp đồng đã bị thay đổi đáng kể. Độ tương đồng hiện tại: {compareResult.similarity_percent:0.##}%.");
            }
            else if (!compareResult.body_text_exact_match)
            {
                loi.Add("Nội dung hợp đồng có khác biệt so với bản gốc.");
            }

            if (!compareResult.signature_name_present)
            {
                loi.Add("Không phát hiện đúng vùng họ tên của khách hàng tại khu vực ký.");
            }

            if (!compareResult.signature_mark_present && !compareResult.digital_signature_valid)
            {
                loi.Add("Không phát hiện chữ ký hiển thị trong vùng ký quy định và cũng không có chữ ký số hợp lệ.");
            }

            if (loi.Count == 0 && !string.IsNullOrWhiteSpace(compareResult.reject_reason))
            {
                loi.Add(compareResult.reject_reason!);
            }

            if (loi.Count == 0)
            {
                loi.Add("Hợp đồng tải lên không vượt qua bước đối chiếu.");
            }

            return string.Join(" ", loi);
        }
    }
}
