using AMMS.Infrastructure.Entities;
using AMMS.Infrastructure.Interfaces;

namespace AMMS.Application.Helpers
{
    public sealed class MaterialSnapshotResult
    {
        public int? material_id { get; init; }
        public string? code { get; init; }
        public string? name { get; init; }
        public string unit { get; init; } = "kg";
        public material? material { get; init; }
    }

    public static class EstimateMaterialSnapshotHelper
    {
        public static async Task<MaterialSnapshotResult?> ResolveAsync(
            IMaterialRepository materialRepo,
            int? materialId,
            string? materialCode,
            string? materialName,
            string defaultName = "Vật tư",
            string defaultUnit = "kg")
        {
            material? entity = null;

            if (materialId.HasValue && materialId.Value > 0)
            {
                entity = await materialRepo.GetByIdAsync(materialId.Value);
            }

            if (entity == null && !string.IsNullOrWhiteSpace(materialCode))
            {
                entity = await materialRepo.GetByCodeAsync(materialCode.Trim());
            }

            if (entity == null &&
                !materialId.HasValue &&
                string.IsNullOrWhiteSpace(materialCode) &&
                string.IsNullOrWhiteSpace(materialName))
            {
                return null;
            }

            return new MaterialSnapshotResult
            {
                material = entity,
                material_id = entity?.material_id ?? materialId,
                code = entity?.code ?? materialCode?.Trim(),
                name = entity?.name ?? materialName?.Trim() ?? defaultName,
                unit = entity?.unit ?? defaultUnit
            };
        }
    }
}