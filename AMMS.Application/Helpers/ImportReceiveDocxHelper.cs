using AMMS.Shared.DTOs.Productions;
using AMMS.Shared.Helpers;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Application.Helpers
{
    public static class ImportReceiveDocxHelper
    {
        public static void Generate(string filePath, ImportReceiveSourceDto source)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

            using var doc = WordprocessingDocument.Create(filePath, WordprocessingDocumentType.Document);
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());

            var body = mainPart.Document.Body!;

            body.Append(
                CreateParagraph("PHIẾU NHẬP KHO THÀNH PHẨM", true, JustificationValues.Center, 28),
                CreateParagraph($"Ngày tạo phiếu: {AppTime.NowVnUnspecified():dd/MM/yyyy HH:mm}", false),
                CreateParagraph($"Mã đơn: {source.order_code}", false),
                CreateParagraph($"ID Production: {source.prod_id}", false),
                CreateParagraph($"ID Order: {source.order_id}", false),
                CreateParagraph("", false)
            );

            var table = new Table();

            var props = new TableProperties(
                new TableBorders(
                    new TopBorder { Val = BorderValues.Single, Size = 8 },
                    new BottomBorder { Val = BorderValues.Single, Size = 8 },
                    new LeftBorder { Val = BorderValues.Single, Size = 8 },
                    new RightBorder { Val = BorderValues.Single, Size = 8 },
                    new InsideHorizontalBorder { Val = BorderValues.Single, Size = 8 },
                    new InsideVerticalBorder { Val = BorderValues.Single, Size = 8 }
                )
            );

            table.AppendChild(props);

            table.Append(
                CreateHeaderRow("STT", "ID", "Mã đơn", "Tên thành phẩm", "Quy cách đóng gói", "Số lượng")
            );

            int stt = 1;
            foreach (var item in source.items)
            {
                table.Append(
                    CreateDataRow(
                        stt.ToString(),
                        item.item_id.ToString(),
                        source.order_code,
                        item.product_name,
                        string.IsNullOrWhiteSpace(item.packaging_standard)
                            ? "Chưa có dữ liệu"
                            : item.packaging_standard!,
                        item.quantity.ToString()
                    )
                );
                stt++;
            }

            body.Append(table);
            body.Append(CreateParagraph("", false));
            body.Append(CreateParagraph("Phiếu được tạo tự động từ hệ thống.", false));
        }

        private static TableRow CreateHeaderRow(params string[] values)
        {
            var row = new TableRow();
            foreach (var v in values)
            {
                row.Append(new TableCell(
                    new Paragraph(
                        new Run(
                            new RunProperties(new Bold()),
                            new Text(v)
                        )
                    )
                ));
            }
            return row;
        }

        private static TableRow CreateDataRow(params string[] values)
        {
            var row = new TableRow();
            foreach (var v in values)
            {
                row.Append(new TableCell(
                    new Paragraph(
                        new Run(new Text(v ?? ""))
                    )
                ));
            }
            return row;
        }

        private static Paragraph CreateParagraph(
    string text,
    bool bold,
    JustificationValues? justification = null,
    int fontSize = 24)
        {
            var runProps = new RunProperties();
            if (bold) runProps.Append(new Bold());
            runProps.Append(new FontSize { Val = fontSize.ToString() });

            var actualJustification = justification ?? JustificationValues.Left;

            return new Paragraph(
                new ParagraphProperties(
                    new Justification { Val = actualJustification }
                ),
                new Run(runProps, new Text(text ?? ""))
            );
        }
    }
}
