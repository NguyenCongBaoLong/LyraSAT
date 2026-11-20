using LyraSAT.Models;
using ICSharpCode.SharpZipLib.Zip;
using NPOI.XWPF.UserModel;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LyraSAT.Services
{
    /// <summary>
    /// Bóc tách file Word (.docx) đề SAT thành danh sách Question.
    /// Quy tắc nhận dạng giữ nguyên bản gốc, chỉ gia cố phần đáp án A/B/C/D vì đây là
    /// chỗ hay sinh lỗi dữ liệu: nhãn chữ cái bị lặp lại, đáp án bị dính vào nhau trên
    /// một dòng, hoặc bị nuốt vào đáp án A khi cả bốn lựa chọn nằm chung một đoạn.
    /// </summary>
    public static class SatWordTestParser
    {
        /// <summary>Nhãn đáp án ở đầu dòng: "A.", "A:", "A)" (không phân biệt hoa thường).</summary>
        private static readonly Regex OptionLabel = new(
            @"^\s*([A-Da-d])\s*[:.)]\s*", RegexOptions.Compiled);

        /// <summary>Ký hiệu bullet do Word tự sinh khi đoạn văn là danh sách.</summary>
        private static readonly Regex BulletPrefix = new(
            @"^[\s\u00A0]*(?:[•·▪◦‣∙]\s*|[-–—]\s+|\*\s+)", RegexOptions.Compiled);

        /// <summary>Nhãn đáp án nằm giữa dòng, dùng để tách khi cả bốn đáp án chung một dòng.</summary>
        private static readonly Regex InlineLabel = new(
            @"(?<![A-Za-z0-9])[A-Da-d]\s*[:.)]\s*", RegexOptions.Compiled);

        /// <summary>
        /// Chữ cái nhìn giống A-D nhưng khác bảng mã: А/В/С (Cyrillic), Α/Β (Hy Lạp),
        /// Ａ/Ｂ/Ｃ/Ｄ (full-width). File Word hay bị lẫn khi copy từ nguồn khác; nếu không
        /// chuẩn hoá thì nhãn "А:" không được nhận, đáp án A rỗng và cả câu bị bỏ.
        /// </summary>
        private static readonly Regex LookAlikeLabel = new(
            @"(?<![A-Za-z0-9])([\u0410\u0412\u0421\u0391\u0392\uFF21-\uFF24\uFF41-\uFF44])(?=\s*[:.)])",
            RegexOptions.Compiled);

        private static char ToAsciiLabel(char c) => c switch
        {
            '\u0410' or '\u0391' or '\uFF21' or '\uFF41' => 'A',
            '\u0412' or '\u0392' or '\uFF22' or '\uFF42' => 'B',
            '\u0421' or '\uFF23' or '\uFF43' => 'C',
            '\uFF24' or '\uFF44' => 'D',
            _ => c,
        };

        private static string NormalizeLabelLetters(string text) =>
            LookAlikeLabel.Replace(text, m => ToAsciiLabel(m.Groups[1].Value[0]).ToString());

        /// <summary>
        /// Dòng mở đầu câu hỏi theo đúng mẫu đề của trung tâm: "Question 1." / "Question 12:" / "Câu 3."
        /// (có số thứ tự, dấu chấm hoặc hai chấm) và cả dạng cũ "Question:" không số.
        /// </summary>
        private static readonly Regex QuestionHeading = new(
            @"^\s*(?:Question|Câu|Cau)\s*(?:(\d+)\s*[.:)]?|:)\s*(.*)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static List<string> ExtractDocumentLines(XWPFDocument document, IReadOnlyList<string?>? paragraphMath = null)
        {
            var extractedLines = new List<string>();
            int paragraphIndex = 0;

            foreach (var element in document.BodyElements)
            {
                if (element is XWPFParagraph paragraph)
                {
                    // Đoạn có công thức Word (OMML) đã được dựng sẵn thành HTML ở tầng XML;
                    // dùng bản đó thay cho RenderParagraph vì NPOI không đọc được OMML.
                    string? mathHtml = paragraphMath != null && paragraphIndex < paragraphMath.Count
                        ? paragraphMath[paragraphIndex]
                        : null;
                    paragraphIndex++;

                    AddParagraphLines(extractedLines, paragraph, mathHtml);
                }
                else if (element is XWPFTable table)
                {
                    // Bảng 2x2 hoặc 1x4 mà mỗi ô là một đáp án (thường gặp ở đề Toán):
                    // tách thành 4 dòng "A. ...", "B. ..." để phần đọc câu hỏi gắn đúng vào đáp án.
                    var optionCells = ReadOptionGrid(table);
                    if (optionCells != null)
                    {
                        // Mỗi đáp án thành 2 dòng: nhãn "A." rồi nội dung (bảng/chữ) để phần
                        // đọc câu hỏi gắn đúng nội dung, kể cả bảng HTML.
                        for (int i = 0; i < 4; i++)
                        {
                            extractedLines.Add($"{(char)('A' + i)}.");
                            extractedLines.Add(optionCells[i]);
                        }
                    }
                    else
                    {
                        extractedLines.Add(RenderTable(table));
                    }
                }
            }

            return extractedLines;
        }

        /// <summary>
        /// Một số file .docx do công cụ khác tạo (Google Docs, WPS, add-in) lưu entry zip ở dạng
        /// "stored + data descriptor" — .NET ZipArchive không đọc được và NPOI văng lỗi
        /// "The combination of Stored compression method and Descriptor flag...".
        /// Đọc lại bằng SharpZipLib rồi ghi ra zip sạch (deflate, không descriptor).
        /// </summary>
        public static byte[]? TryNormalizeDocx(byte[] bytes)
        {
            try
            {
                using var input = new MemoryStream(bytes);
                using var zip = new ICSharpCode.SharpZipLib.Zip.ZipFile(input);
                using var output = new MemoryStream();

                using (var zipOut = new ZipOutputStream(output) { IsStreamOwner = false })
                {
                    zipOut.SetLevel(6);
                    foreach (ZipEntry entry in zip)
                    {
                        if (!entry.IsFile) continue;

                        var newEntry = new ZipEntry(entry.Name) { DateTime = entry.DateTime };
                        zipOut.PutNextEntry(newEntry);
                        using var entryStream = zip.GetInputStream(entry);
                        entryStream.CopyTo(zipOut);
                        zipOut.CloseEntry();
                    }
                }

                var normalized = output.ToArray();
                return normalized.Length > 0 ? normalized : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Dựng các đoạn có công thức Word (OMML) thành HTML bằng cách đọc thẳng
        /// word/document.xml, vì NPOI bỏ qua OMML nên công thức bị mất khỏi đề.
        /// Trả về danh sách theo thứ tự đoạn cấp body; đoạn không có công thức là null.
        /// </summary>
        public static List<string?> ExtractParagraphMath(Stream docxStream)
        {
            var result = new List<string?>();

            try
            {
                using var archive = new ZipArchive(docxStream, ZipArchiveMode.Read, leaveOpen: true);
                var entry = archive.GetEntry("word/document.xml");
                if (entry == null) return result;

                using var xmlStream = entry.Open();
                var document = XDocument.Load(xmlStream);
                var body = document.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "body");
                if (body == null) return result;

                var relationships = ReadDocumentRelationships(archive);

                foreach (var child in body.Elements())
                {
                    if (child.Name.LocalName != "p") continue;
                    bool hasMath = child.Descendants().Any(e => e.Name.LocalName == "oMath");
                    result.Add(hasMath ? RenderParagraphXml(child, archive, relationships) : null);
                }
            }
            catch
            {
                // File lạ hoặc xml hỏng: bỏ qua, dùng cách đọc thường.
                result.Clear();
            }

            return result;
        }

        /// <summary>Bảng ánh xạ rId -> đường dẫn media trong file docx (để dựng lại ảnh).</summary>
        private static Dictionary<string, string> ReadDocumentRelationships(ZipArchive archive)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            var entry = archive.GetEntry("word/_rels/document.xml.rels");
            if (entry == null) return map;

            using var stream = entry.Open();
            var document = XDocument.Load(stream);
            foreach (var relationship in document.Root?.Elements() ?? Enumerable.Empty<XElement>())
            {
                var id = relationship.Attribute("Id")?.Value;
                var target = relationship.Attribute("Target")?.Value;
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(target)) continue;

                var path = target.StartsWith("/") ? target.TrimStart('/') : "word/" + target;
                map[id] = path;
            }
            return map;
        }

        /// <summary>Dựng một đoạn từ XML: chữ, ngắt dòng, công thức và ảnh đúng thứ tự.</summary>
        private static string RenderParagraphXml(XElement paragraph, ZipArchive archive, Dictionary<string, string> relationships)
        {
            var content = new StringBuilder();
            AppendParagraphXml(paragraph, content, archive, relationships);
            return content.ToString();
        }

        private static void AppendParagraphXml(XElement container, StringBuilder content, ZipArchive archive, Dictionary<string, string> relationships)
        {
            foreach (var child in container.Elements())
            {
                switch (child.Name.LocalName)
                {
                    case "r":
                        AppendRunXml(child, content, archive, relationships);
                        break;
                    case "oMath":
                        content.Append("<span class='math-inline'>")
                            .Append(WebUtility.HtmlEncode(OmmlToLatex(child)))
                            .Append("</span>");
                        break;
                    case "oMathPara":
                        content.Append("<div class='math-block'>")
                            .Append(WebUtility.HtmlEncode(OmmlToLatex(child)))
                            .Append("</div>");
                        break;
                    case "hyperlink":
                    case "ins":
                    case "smartTag":
                    case "sdt":
                    case "sdtContent":
                        AppendParagraphXml(child, content, archive, relationships);
                        break;
                }
            }
        }

        private static void AppendRunXml(XElement run, StringBuilder content, ZipArchive archive, Dictionary<string, string> relationships)
        {
            foreach (var child in run.Elements())
            {
                switch (child.Name.LocalName)
                {
                    case "t":
                        content.Append(WebUtility.HtmlEncode(child.Value));
                        break;
                    case "br":
                        content.Append("<br>");
                        break;
                    case "tab":
                        content.Append(' ');
                        break;
                    case "drawing":
                        AppendDrawingXml(child, content, archive, relationships);
                        break;
                    case "oMath":
                        content.Append("<span class='math-inline'>")
                            .Append(WebUtility.HtmlEncode(OmmlToLatex(child)))
                            .Append("</span>");
                        break;
                }
            }
        }

        /// <summary>
        /// Ảnh nằm trong đoạn có công thức: đọc file media trong docx và nhúng thẳng dạng
        /// data URI, giống đường xử lý ảnh thường, để không mất hình khi đoạn có OMML.
        /// </summary>
        private static void AppendDrawingXml(XElement drawing, StringBuilder content, ZipArchive archive, Dictionary<string, string> relationships)
        {
            var blip = drawing.Descendants().FirstOrDefault(e => e.Name.LocalName == "blip");
            var embedId = blip?.Attributes().FirstOrDefault(a => a.Name.LocalName == "embed")?.Value;
            if (string.IsNullOrEmpty(embedId) || !relationships.TryGetValue(embedId, out var path)) return;

            var entry = archive.GetEntry(path);
            if (entry == null) return;

            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var bytes = memory.ToArray();
            if (bytes.Length == 0) return;

            var mimeType = GetImageMimeType(path);
            var dataUri = $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}";

            // Giữ đúng kích thước hiển thị trong Word (wp:extent), nếu không ảnh công thức
            // sẽ phình to bằng pixel gốc và làm đáp án lệch cỡ chữ.
            var extent = drawing.Descendants().FirstOrDefault(e => e.Name.LocalName == "extent");
            long.TryParse(extent?.Attribute("cx")?.Value, out var widthEmu);
            long.TryParse(extent?.Attribute("cy")?.Value, out var heightEmu);

            content.Append($"<img src=\"{dataUri}\" alt=\"Hình minh họa\"{ImageSizeAttributes(widthEmu, heightEmu)} />");
        }

        /// <summary>
        /// Đổi kích thước hiển thị của ảnh trong Word (EMU) sang pixel 96dpi để trình duyệt
        /// vẽ đúng cỡ như bản Word. Không có thông tin thì để trình duyệt tự tính.
        /// </summary>
        private static string ImageSizeAttributes(long widthEmu, long heightEmu)
        {
            if (widthEmu <= 0 || heightEmu <= 0) return string.Empty;

            var width = Math.Max(1, (int)Math.Round(widthEmu / 9525.0));
            var height = Math.Max(1, (int)Math.Round(heightEmu / 9525.0));
            return $" width=\"{width}\" height=\"{height}\"";
        }

        /// <summary>
        /// Công thức Word (OMML) sang LaTeX để frontend vẽ bằng KaTeX: phân số xếp tầng,
        /// căn có bậc, số mũ, chỉ số... nhìn giống bản Word thay vì chữ tuyến tính.
        /// </summary>
        private static string OmmlToLatex(XElement math)
        {
            var content = new StringBuilder();
            foreach (var child in math.Elements())
            {
                AppendMathLatex(child, content);
            }
            return content.ToString().Trim();
        }

        private static string MathLatex(XElement? node)
        {
            if (node == null) return string.Empty;
            var content = new StringBuilder();
            foreach (var child in node.Elements())
            {
                AppendMathLatex(child, content);
            }
            return content.ToString();
        }

        private static XElement? MathPart(XElement node, string name) =>
            node.Elements().FirstOrDefault(e => e.Name.LocalName == name);

        /// <summary>Thuộc tính ký tự của khối công thức, ví dụ dấu ngoặc của m:d.</summary>
        private static string MathChar(XElement node, string propsName, string charName, string fallback)
        {
            var props = MathPart(node, propsName);
            var value = props?.Elements().FirstOrDefault(e => e.Name.LocalName == charName)
                ?.Attribute("val")?.Value;
            return string.IsNullOrEmpty(value) ? fallback : value;
        }

        private static string EscapeLatex(string text)
        {
            var content = new StringBuilder();
            foreach (char ch in text)
            {
                switch (ch)
                {
                    // Giáo viên hay gõ thẳng LaTeX vào equation, nên giữ nguyên các ký tự
                    // có nghĩa trong LaTeX: \ ^ _ { } — chỉ thoát những ký tự phá cú pháp.
                    case '%':
                    case '&':
                    case '#':
                    case '$':
                        content.Append('\\').Append(ch);
                        break;
                    case '~':
                        content.Append("\\sim ");
                        break;
                    // Trong chế độ toán, dấu cách bị bỏ qua; đổi thành lệnh cách để câu chữ
                    // gõ trong công thức (Word hay gõ cả câu vào equation) không bị dính chữ.
                    case ' ':
                        content.Append("\\ ");
                        break;
                    // '<' '>' phải đổi sang lệnh LaTeX, nếu để ký tự thật thì bộ lọc HTML
                    // của frontend hiểu nhầm là thẻ và nuốt mất đoạn chữ phía sau.
                    case '<':
                        content.Append("\\lt ");
                        break;
                    case '>':
                        content.Append("\\gt ");
                        break;
                    // Ký hiệu toán hay gặp khi gõ trực tiếp: đổi sang lệnh LaTeX để KaTeX vẽ được.
                    case '≠':
                        content.Append("\\neq ");
                        break;
                    case '≤':
                        content.Append("\\le ");
                        break;
                    case '≥':
                        content.Append("\\ge ");
                        break;
                    case '≈':
                        content.Append("\\approx ");
                        break;
                    case '×':
                        content.Append("\\times ");
                        break;
                    case '÷':
                        content.Append("\\div ");
                        break;
                    case '±':
                        content.Append("\\pm ");
                        break;
                    case '∞':
                        content.Append("\\infty ");
                        break;
                    case 'π':
                        content.Append("\\pi ");
                        break;
                    case 'θ':
                        content.Append("\\theta ");
                        break;
                    case 'α':
                        content.Append("\\alpha ");
                        break;
                    case 'β':
                        content.Append("\\beta ");
                        break;
                    case '°':
                        content.Append("^{\\circ}");
                        break;
                    case '·':
                        content.Append("\\cdot ");
                        break;
                    case '−':
                        content.Append('-');
                        break;
                    case '∈':
                        content.Append("\\in ");
                        break;
                    default:
                        content.Append(ch);
                        break;
                }
            }
            return content.ToString();
        }

        private static string NaryToLatex(string chr) => chr switch
        {
            "∑" => "\\sum",
            "∏" => "\\prod",
            "∫" => "\\int",
            "∬" => "\\iint",
            "∭" => "\\iiint",
            "∮" => "\\oint",
            "⋃" => "\\bigcup",
            "⋂" => "\\bigcap",
            _ => "\\sum",
        };

        private static void AppendMathLatex(XElement node, StringBuilder content)
        {
            switch (node.Name.LocalName)
            {
                case "t":
                    content.Append(EscapeLatex(node.Value));
                    break;

                case "r":
                    foreach (var text in node.Elements().Where(e => e.Name.LocalName == "t"))
                    {
                        content.Append(EscapeLatex(text.Value));
                    }
                    break;

                case "f":
                    content.Append("\\frac{").Append(MathLatex(MathPart(node, "num")).Trim())
                        .Append("}{").Append(MathLatex(MathPart(node, "den")).Trim()).Append('}');
                    break;

                case "sSup":
                    content.Append('{').Append(MathLatex(MathPart(node, "e")))
                        .Append("}^{").Append(MathLatex(MathPart(node, "sup"))).Append('}');
                    break;

                case "sSub":
                    content.Append('{').Append(MathLatex(MathPart(node, "e")))
                        .Append("}_{").Append(MathLatex(MathPart(node, "sub"))).Append('}');
                    break;

                case "sSubSup":
                    content.Append('{').Append(MathLatex(MathPart(node, "e")))
                        .Append("}_{").Append(MathLatex(MathPart(node, "sub")))
                        .Append("}^{").Append(MathLatex(MathPart(node, "sup"))).Append('}');
                    break;

                case "sPre":
                    content.Append("{}").Append("_{").Append(MathLatex(MathPart(node, "sub")))
                        .Append("}^{").Append(MathLatex(MathPart(node, "sup")))
                        .Append('}').Append(MathLatex(MathPart(node, "e")));
                    break;

                case "rad":
                    {
                        string degree = MathLatex(MathPart(node, "deg")).Trim();
                        content.Append(degree.Length > 0 ? "\\sqrt[" + degree + "]{" : "\\sqrt{");
                        content.Append(MathLatex(MathPart(node, "e")).Trim()).Append('}');
                        break;
                    }

                case "d":
                    {
                        string begin = MathChar(node, "dPr", "begChr", "(");
                        string end = MathChar(node, "dPr", "endChr", ")");
                        content.Append("\\left").Append(begin)
                            .Append(MathLatex(MathPart(node, "e")).Trim())
                            .Append("\\right").Append(end);
                        break;
                    }

                case "nary":
                    {
                        string chr = MathChar(node, "naryPr", "chr", "∫");
                        content.Append(NaryToLatex(chr));
                        string sub = MathLatex(MathPart(node, "sub")).Trim();
                        string sup = MathLatex(MathPart(node, "sup")).Trim();
                        if (sub.Length > 0) content.Append("_{").Append(sub).Append('}');
                        if (sup.Length > 0) content.Append("^{").Append(sup).Append('}');
                        content.Append(' ').Append(MathLatex(MathPart(node, "e")).Trim());
                        break;
                    }

                case "eqArr":
                    {
                        var rows = node.Elements().Where(e => e.Name.LocalName == "e")
                            .Select(row => MathLatex(row).Trim())
                            .Where(row => row.Length > 0)
                            .ToList();
                        if (rows.Count > 0)
                        {
                            content.Append("\\begin{aligned}").Append(string.Join(" \\\\ ", rows)).Append("\\end{aligned}");
                        }
                        break;
                    }

                case "func":
                    content.Append(MathLatex(MathPart(node, "fName")).Trim());
                    content.Append(MathLatex(MathPart(node, "e")));
                    break;

                case "limLow":
                    content.Append('{').Append(MathLatex(MathPart(node, "e")))
                        .Append("}_{").Append(MathLatex(MathPart(node, "lim"))).Append('}');
                    break;

                case "limUpp":
                    content.Append('{').Append(MathLatex(MathPart(node, "e")))
                        .Append("}^{").Append(MathLatex(MathPart(node, "lim"))).Append('}');
                    break;

                case "m":
                    {
                        var rows = node.Elements().Where(e => e.Name.LocalName == "mr")
                            .Select(row => string.Join(" & ", row.Elements()
                                .Where(c => c.Name.LocalName == "e")
                                .Select(cell => MathLatex(cell).Trim())))
                            .ToList();
                        if (rows.Count > 0)
                        {
                            content.Append("\\begin{matrix}").Append(string.Join(" \\\\ ", rows)).Append("\\end{matrix}");
                        }
                        break;
                    }

                case "acc":
                    {
                        string chr = MathChar(node, "accPr", "chr", "^");
                        string command = chr switch
                        {
                            "¯" => "\\bar",
                            "→" => "\\vec",
                            "˙" => "\\dot",
                            "¨" => "\\ddot",
                            _ => "\\hat",
                        };
                        content.Append(command).Append('{').Append(MathLatex(MathPart(node, "e"))).Append('}');
                        break;
                    }

                case "bar":
                    content.Append("\\overline{").Append(MathLatex(MathPart(node, "e"))).Append('}');
                    break;

                case "groupChr":
                    {
                        string chr = MathChar(node, "groupChrPr", "chr", "⏟");
                        string command = chr == "⏞" ? "\\overbrace" : "\\underbrace";
                        content.Append(command).Append('{').Append(MathLatex(MathPart(node, "e"))).Append('}');
                        break;
                    }

                case "box":
                case "borderBox":
                case "phant":
                case "e":
                default:
                    content.Append(MathLatex(node));
                    break;
            }
        }

        /// <summary>
        /// Bảng Word thành HTML, giữ cả bảng lồng trong ô (đề Toán hay có bảng trong đáp án).
        /// </summary>
        private static string RenderTable(XWPFTable table)
        {
            int columnCount = Math.Max(1, table.Rows.FirstOrDefault()?.GetTableCells().Count ?? 1);

            // Số cột gắn vào class (không dùng style vì frontend lọc thuộc tính): CSS dựa vào
            // đó để bảng nhiều cột đủ rộng, thà cuộn ngang còn hơn ép cột đến mức vỡ chữ.
            var html = new StringBuilder("<div class='rich-table-wrapper'><table class='rich-table cols-")
                .Append(Math.Min(columnCount, 10))
                .Append("'><tbody>");

            foreach (var row in table.Rows)
            {
                html.Append("<tr>");
                foreach (var cell in row.GetTableCells())
                {
                    html.Append("<td>").Append(RenderCell(cell)).Append("</td>");
                }
                html.Append("</tr>");
            }

            html.Append("</tbody></table></div>");
            return html.ToString();
        }

        private static string RenderCell(XWPFTableCell cell)
        {
            var parts = new List<string>();

            foreach (var element in cell.BodyElements)
            {
                if (element is XWPFParagraph paragraph)
                {
                    var html = RenderParagraph(paragraph);
                    if (!string.IsNullOrWhiteSpace(html)) parts.Add(html);
                }
                else if (element is XWPFTable nested)
                {
                    parts.Add(RenderTable(nested));
                }
            }

            return string.Join("<br>", parts);
        }

        /// <summary>
        /// Bảng 2x2 (hoặc 1x4) mà mỗi ô là một đáp án. Chỉ nhận khi mỗi ô có nhãn A/B/C/D
        /// hoặc chứa bảng riêng, để không nhầm bảng số liệu 2x2 của đề bài thành đáp án.
        /// </summary>
        private static List<string>? ReadOptionGrid(XWPFTable table)
        {
            var cells = table.Rows.SelectMany(row => row.GetTableCells()).ToList();
            if (cells.Count != 4) return null;

            var contents = new List<string>();
            bool hasLabelledCell = false;
            bool hasNestedTable = false;

            foreach (var cell in cells)
            {
                var html = RenderCell(cell).Trim();
                if (html.Length == 0) return null;

                var labelMatch = OptionLabel.Match(html);
                if (labelMatch.Success)
                {
                    hasLabelledCell = true;
                    html = NormalizeOptionText(html.Substring(labelMatch.Length), char.ToUpperInvariant(labelMatch.Groups[1].Value[0]));
                }

                if (html.Contains("<table", StringComparison.OrdinalIgnoreCase)) hasNestedTable = true;
                contents.Add(html);
            }

            if (!hasLabelledCell && !hasNestedTable) return null;

            return contents;
        }

        public static List<Question> Parse(List<string> lines, int testId) => Parse(lines, testId, out _);

        /// <summary>
        /// Như Parse(lines, testId) nhưng trả thêm danh sách câu bị bỏ vì không đọc được dữ liệu.
        /// Không được bỏ im lặng: mất một câu làm lệch số thứ tự của toàn bộ đề.
        /// </summary>
        public static List<Question> Parse(List<string> lines, int testId, out List<string> skipped)
        {
            var questions = new List<Question>();
            skipped = new List<string>();
            Question? currentQ = null;
            var passageLines = new List<string>();
            var questionLines = new List<string>();
            bool isReadingPassage = true;
            char? pendingOption = null;

            foreach (var rawLine in lines)
            {
                // Chuẩn hoá chữ cái nhìn giống A-D (Cyrillic/Hy Lạp/full-width) về ASCII
                // trước khi nhận dạng nhãn đáp án.
                string line = NormalizeLabelLetters(rawLine.Trim());
                string plainLine = StripHtml(line).Trim();
                if (string.IsNullOrEmpty(plainLine))
                {
                    // Dòng chỉ có ảnh (không có chữ) vẫn phải vào đúng đáp án đang mở.
                    if (currentQ != null && pendingOption.HasValue && !string.IsNullOrWhiteSpace(line))
                    {
                        AppendOptionContent(currentQ, pendingOption.Value, line);
                    }
                    else if (currentQ != null && pendingOption == null)
                    {
                        if (isReadingPassage) passageLines.Add(rawLine);
                        else questionLines.Add(rawLine);
                    }
                    continue;
                }

                // Dòng do Word tự đánh bullet thì bỏ bullet khi nhận dạng,
                // nhưng vẫn giữ nguyên văn cho phần đoạn đọc để không mất bullet.
                string probe = StripBullet(plainLine);

                if (plainLine.StartsWith("Text:", StringComparison.OrdinalIgnoreCase))
                {
                    FinalizeQuestion(questions, skipped, currentQ, passageLines, questionLines);
                    currentQ = new Question { TestId = testId };
                    isReadingPassage = true;
                    pendingOption = null;

                    string contentAfterColon = RemovePrefix(line, "Text:");
                    if (!string.IsNullOrEmpty(contentAfterColon))
                    {
                        passageLines.Add(contentAfterColon);
                    }
                }
                else if (TryReadQuestionHeading(plainLine, out string questionHeadingRest))
                {
                    // Câu tự luận thường không có "Text:" ngăn cách, nên gặp dòng mở đầu câu mới
                    // là biết câu trước đã xong; chốt câu trước rồi mở câu mới.
                    if (currentQ != null &&
                        (!string.IsNullOrWhiteSpace(currentQ.QuestionText) ||
                         !string.IsNullOrWhiteSpace(currentQ.CorrectAnswer) ||
                         questionLines.Any(line => !string.IsNullOrWhiteSpace(line))))
                    {
                        FinalizeQuestion(questions, skipped, currentQ, passageLines, questionLines);
                        currentQ = null;
                    }

                    if (currentQ == null)
                    {
                        currentQ = new Question { TestId = testId };
                    }

                    isReadingPassage = false;
                    pendingOption = null;

                    if (!string.IsNullOrEmpty(questionHeadingRest))
                    {
                        questionLines.Add(questionHeadingRest);
                    }

                    // Đoạn mở đầu câu có thể kèm ảnh (đồ thị) ngay sau chữ "Question 6.".
                    // Nếu chỉ lấy phần chữ thì ảnh trong đoạn đó bị mất.
                    foreach (Match image in Regex.Matches(line, "<img[^>]*>"))
                    {
                        questionLines.Add(image.Value);
                    }
                }
                else if (currentQ != null)
                {
                    if (IsAnswerLine(probe))
                    {
                        pendingOption = null;
                        string? answer = ExtractAnswerLetter(probe);
                        bool hasOptions = !string.IsNullOrWhiteSpace(currentQ.OptionA)
                            || !string.IsNullOrWhiteSpace(currentQ.OptionB)
                            || !string.IsNullOrWhiteSpace(currentQ.OptionC)
                            || !string.IsNullOrWhiteSpace(currentQ.OptionD);

                        if (answer != null)
                        {
                            currentQ.CorrectAnswer = answer;
                            currentQ.QuestionType = AnswerMatcher.MultipleChoice;
                        }
                        else if (!hasOptions)
                        {
                            // Không có A/B/C/D và đáp án không phải chữ cái => câu tự luận.
                            var accepted = ExtractFreeResponseAnswers(probe);
                            if (accepted.Count > 0)
                            {
                                currentQ.QuestionType = AnswerMatcher.FreeResponse;
                                currentQ.AcceptedAnswers = string.Join(" | ", accepted);
                                currentQ.CorrectAnswer = accepted[0];
                            }
                        }
                    }
                    else if (probe.StartsWith("VIDEO", StringComparison.OrdinalIgnoreCase) || probe.StartsWith("Link video", StringComparison.OrdinalIgnoreCase))
                    {
                        var colonIdx = probe.IndexOf(':');
                        if (colonIdx >= 0 && colonIdx < probe.Length - 1)
                        {
                            currentQ.VideoExplanationUrl = probe.Substring(colonIdx + 1).Trim();
                        }
                    }
                    else if (TryExtractInlineOptions(currentQ, probe))
                    {
                        // Bốn đáp án nằm chung một dòng: đã tách xong.
                        pendingOption = null;
                    }
                    else if (OptionLabel.IsMatch(probe))
                    {
                        var match = OptionLabel.Match(probe);
                        char optKey = char.ToUpperInvariant(match.Groups[1].Value[0]);
                        AssignOption(currentQ, optKey, NormalizeOptionText(probe.Substring(match.Length), optKey));
                        pendingOption = optKey;
                    }
                    else if (pendingOption.HasValue)
                    {
                        // Dòng/bảng/hình nằm ngay sau nhãn đáp án (bảng trong đáp án, phân số
                        // xếp dọc kiểu A. / 1 / 400) thì thuộc về đáp án đó.
                        AppendOptionContent(currentQ, pendingOption.Value, line);
                    }
                    else
                    {
                        // Câu đang đọc đã có đáp án đúng rồi thì dòng này không thể thuộc câu đó:
                        // đó là khối của câu tiếp theo. Đề của giáo viên hay quên chữ "Text:" ở
                        // câu dạng ghi chú, nếu vẫn gắn vào câu cũ thì đoạn văn bị dồn sang câu
                        // trước và câu sau mất đoạn văn.
                        if (!isReadingPassage &&
                            pendingOption == null &&
                            !string.IsNullOrWhiteSpace(currentQ.CorrectAnswer))
                        {
                            FinalizeQuestion(questions, skipped, currentQ, passageLines, questionLines);
                            currentQ = new Question { TestId = testId };
                            isReadingPassage = true;
                            passageLines.Add(line);
                        }
                        else if (isReadingPassage)
                        {
                            passageLines.Add(line);
                        }
                        else
                        {
                            questionLines.Add(line);
                        }
                    }
                }
                else
                {
                    passageLines.Add(line);
                }
            }

            FinalizeQuestion(questions, skipped, currentQ, passageLines, questionLines);

            return questions;
        }

        /// <summary>
        /// Chốt câu hỏi đang đọc: gắn đoạn văn + nội dung câu, thêm vào danh sách nếu dùng được,
        /// rồi xoá buffer để đọc câu tiếp theo.
        /// </summary>
        private static void FinalizeQuestion(List<Question> questions, List<string> skipped, Question? current, List<string> passageLines, List<string> questionLines)
        {
            if (current == null) return;

            // Khối rỗng (ví dụ dòng "Text:" đứng riêng sau khi câu trước vừa chốt) thì bỏ qua
            // im lặng, không tính là câu bị mất.
            bool hasAnything =
                !string.IsNullOrWhiteSpace(current.CorrectAnswer) ||
                !string.IsNullOrWhiteSpace(current.OptionA) ||
                !string.IsNullOrWhiteSpace(current.OptionB) ||
                !string.IsNullOrWhiteSpace(current.OptionC) ||
                !string.IsNullOrWhiteSpace(current.OptionD) ||
                passageLines.Any(line => !string.IsNullOrWhiteSpace(line)) ||
                questionLines.Any(line => !string.IsNullOrWhiteSpace(line));

            if (!hasAnything)
            {
                passageLines.Clear();
                questionLines.Clear();
                return;
            }

            // Đề Toán thường không có câu hỏi tách riêng: cả khối đề bài (có công thức, bảng)
            // chính là câu hỏi. Nếu chưa có dòng Question: nào mà đã có đáp án/lựa chọn thì
            // lấy toàn bộ phần đã đọc làm nội dung câu hỏi, không coi là đoạn văn.
            bool hasAnswerOrOptions =
                !string.IsNullOrWhiteSpace(current.CorrectAnswer) ||
                !string.IsNullOrWhiteSpace(current.OptionA) ||
                !string.IsNullOrWhiteSpace(current.OptionB) ||
                !string.IsNullOrWhiteSpace(current.OptionC) ||
                !string.IsNullOrWhiteSpace(current.OptionD);

            if (hasAnswerOrOptions &&
                !questionLines.Any(line => !string.IsNullOrWhiteSpace(line)) &&
                passageLines.Any(line => !string.IsNullOrWhiteSpace(line)))
            {
                questionLines.AddRange(passageLines);
                passageLines.Clear();
            }

            current.PassageText = JoinPreservingLineBreaks(passageLines);
            current.QuestionText = JoinPreservingLineBreaks(questionLines);

            // Cột Options trong DB là NOT NULL: câu tự luận để chuỗi rỗng chứ không được null.
            current.OptionA ??= string.Empty;
            current.OptionB ??= string.Empty;
            current.OptionC ??= string.Empty;
            current.OptionD ??= string.Empty;
            current.CorrectAnswer ??= string.Empty;
            current.QuestionType = string.IsNullOrWhiteSpace(current.QuestionType)
                ? AnswerMatcher.MultipleChoice
                : current.QuestionType;

            if (IsUsableQuestion(current))
            {
                questions.Add(current);
            }
            else
            {
                // Bỏ câu là chuyện lớn: mất một câu làm lệch số thứ tự của cả đề và giáo viên
                // không biết mình thiếu gì. Ghi lại kèm trích đoạn để còn báo cho giáo viên.
                var found = new List<string>();
                if (!string.IsNullOrWhiteSpace(current.OptionB)) found.Add("B");
                if (!string.IsNullOrWhiteSpace(current.OptionC)) found.Add("C");
                if (!string.IsNullOrWhiteSpace(current.OptionD)) found.Add("D");

                string reason = string.IsNullOrWhiteSpace(current.QuestionText)
                    ? "không đọc được nội dung câu hỏi"
                    : found.Count > 0
                        ? $"có đáp án {string.Join(", ", found)} nhưng không đọc được đáp án A"
                        : "không đọc được đáp án A, B, C, D";

                skipped.Add($"Một câu bị bỏ vì {reason}: \"{QuestionSnippet(current)}\"");
            }

            passageLines.Clear();
            questionLines.Clear();
        }

        /// <summary>
        /// Câu chỉ được lưu khi có nội dung câu hỏi. Câu tự luận không cần lựa chọn;
        /// câu trắc nghiệm bắt buộc có OptionA vì cột này NOT NULL trong DB.
        /// </summary>
        private static bool IsUsableQuestion(Question q)
        {
            if (string.IsNullOrWhiteSpace(q.QuestionText)) return false;
            if (AnswerMatcher.IsFreeResponse(q.QuestionType)) return true;
            return !string.IsNullOrWhiteSpace(q.OptionA);
        }

        /// <summary>
        /// Soát lại toàn bộ câu hỏi vừa bóc tách. Trả về danh sách lỗi đọc được bằng tiếng Việt;
        /// rỗng nghĩa là dữ liệu đáp án sạch. Mục đích: không lưu vào DB những câu mà đáp án
        /// bị thiếu, bị trùng, hoặc còn dính nhãn chữ cái.
        /// </summary>
        public static List<string> Validate(IReadOnlyList<Question> questions)
        {
            var issues = new List<string>();
            string[] letters = { "A", "B", "C", "D" };

            for (int i = 0; i < questions.Count; i++)
            {
                var q = questions[i];
                var options = new[] { q.OptionA ?? "", q.OptionB ?? "", q.OptionC ?? "", q.OptionD ?? "" };
                // Ghi kèm trích đoạn câu hỏi: giáo viên tìm theo nội dung luôn đúng, không phụ
                // thuộc việc đếm số thứ tự (đề có thể bị lệch nếu một câu không đọc được).
                string where = QuestionLabel(q, i + 1);

                if (AnswerMatcher.IsFreeResponse(q.QuestionType))
                {
                    ValidateFreeResponse(q, where, options, issues);
                    continue;
                }

                var missing = new List<string>();
                for (int k = 0; k < 4; k++)
                {
                    if (string.IsNullOrWhiteSpace(options[k])) missing.Add(letters[k]);
                }
                if (missing.Count > 0)
                {
                    issues.Add($"{where}: thiếu nội dung đáp án {string.Join(", ", missing)}.");
                }

                var labelled = new List<string>();
                for (int k = 0; k < 4; k++)
                {
                    var labelMatch = OptionLabel.Match(options[k]);
                    if (labelMatch.Success &&
                        char.ToUpperInvariant(labelMatch.Groups[1].Value[0]) == letters[k][0])
                    {
                        labelled.Add(letters[k]);
                    }
                }
                if (labelled.Count > 0)
                {
                    issues.Add($"{where}: đáp án {string.Join(", ", labelled)} vẫn còn dính nhãn chữ cái, hệ thống không đọc được.");
                }

                var duplicated = options
                    .Select((text, index) => new { text = text.Trim(), letter = letters[index] })
                    .Where(x => x.text.Length > 0)
                    .GroupBy(x => x.text, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1)
                    .Select(g => new
                    {
                        Letters = string.Join("=", g.Select(x => x.letter)),
                        Text = g.Key.Length > 50 ? g.Key.Substring(0, 47) + "..." : g.Key
                    })
                    .ToList();
                if (duplicated.Count > 0)
                {
                    var detail = string.Join("; ", duplicated.Select(d => $"{d.Letters} đều là \u201c{d.Text}\u201d"));
                    issues.Add($"{where}: {detail} — thường do copy-paste, sửa lại nội dung một trong hai đáp án.");
                }

                var answer = (q.CorrectAnswer ?? "").Trim().ToUpperInvariant();
                if (answer.Length == 0)
                {
                    issues.Add($"{where}: không tìm thấy dòng đáp án đúng (ANSWER / Đáp án).");
                }
                else if (answer.Length != 1 || answer[0] < 'A' || answer[0] > 'D')
                {
                    issues.Add($"{where}: đáp án đúng phải là A, B, C hoặc D (đang là '{q.CorrectAnswer}').");
                }
            }

            return issues;
        }

        /// <summary>
        /// Câu tự luận: không có A/B/C/D, đáp án là danh sách cách ghi đúng cách nhau bằng dấu |,
        /// mỗi cách ghi phải đọc được thành số (phân số, thập phân, số âm...).
        /// </summary>
        private static void ValidateFreeResponse(Question q, string where, string[] options, List<string> issues)
        {
            if (options.Any(text => !string.IsNullOrWhiteSpace(text)))
            {
                issues.Add($"{where}: câu tự luận không được có đáp án A/B/C/D.");
            }

            var accepted = AnswerMatcher.SplitAccepted(q.AcceptedAnswers);
            if (accepted.Count == 0)
            {
                issues.Add($"{where}: không tìm thấy đáp án tự luận (cần dòng ANSWER: 3/2 | 1.5).");
                return;
            }

            var bad = accepted.Where(answer => !AnswerMatcher.TryParseNumber(answer, out _)).ToList();
            if (bad.Count > 0)
            {
                issues.Add($"{where}: đáp án tự luận {string.Join(", ", bad.Select(x => $"'{x}'"))} không đọc được thành số.");
            }

            var stored = (q.AcceptedAnswers ?? "").Trim();
            if (stored.EndsWith("|") || stored.EndsWith(";") || stored.Contains("||"))
            {
                issues.Add($"{where}: danh sách đáp án tự luận bị thừa dấu phân tách.");
            }
        }

        /// <summary>
        /// Nhãn để giáo viên tìm đúng câu: "Câu 6 (“Which choice best describes…”)".
        /// Số thứ tự chỉ là vị trí trong file; trích đoạn mới là thứ đối chiếu được.
        /// </summary>
        private static string QuestionLabel(Question q, int number)
        {
            var snippet = QuestionSnippet(q);
            return string.IsNullOrWhiteSpace(snippet) ? $"Câu {number}" : $"Câu {number} (“{snippet}”)";
        }

        /// <summary>Trích đoạn ngắn của câu hỏi (hoặc đoạn văn/đáp án nếu câu hỏi rỗng), đã bỏ HTML.</summary>
        private static string QuestionSnippet(Question q, int maxLength = 60)
        {
            var text = StripHtml(q.QuestionText ?? "");
            if (string.IsNullOrWhiteSpace(text)) text = StripHtml(q.PassageText ?? "");
            if (string.IsNullOrWhiteSpace(text))
            {
                // Câu bị bỏ vì không có nội dung: lấy tạm đáp án đầu tiên để giáo viên nhận ra câu.
                text = StripHtml(q.OptionA ?? "");
                if (string.IsNullOrWhiteSpace(text)) text = StripHtml(q.OptionB ?? "");
                if (string.IsNullOrWhiteSpace(text)) text = StripHtml(q.OptionC ?? "");
                if (string.IsNullOrWhiteSpace(text)) text = StripHtml(q.OptionD ?? "");
            }
            text = Regex.Replace(text, @"\s+", " ").Trim();
            if (text.Length > maxLength) text = text.Substring(0, maxLength - 3).TrimEnd() + "...";
            return text;
        }

        /// <summary>
        /// Bỏ nhãn chữ cái bị lặp ở đầu đáp án, chỉ khi nhãn đó trùng đúng chữ cái của ô
        /// ("A. A. throughput" -> "throughput"), và bỏ bullet do Word tự sinh.
        /// Không bỏ chữ cái khác vì đáp án có thể mở đầu bằng tên riêng như "D. H. Lawrence".
        /// </summary>
        private static string NormalizeOptionText(string raw, char expected)
        {
            var text = (raw ?? string.Empty).Trim();
            var label = new Regex($@"^{expected}\s*[:.)]\s*", RegexOptions.IgnoreCase);

            for (int guard = 0; guard < 4; guard++)
            {
                var before = text;
                text = BulletPrefix.Replace(text, string.Empty).Trim();

                var match = label.Match(text);
                if (match.Success && match.Length < text.Length)
                {
                    text = text.Substring(match.Length).Trim();
                }

                if (string.Equals(before, text, StringComparison.Ordinal)) break;
            }

            return text;
        }

        private static string StripBullet(string line)
        {
            var stripped = BulletPrefix.Replace(line, string.Empty).Trim();
            return stripped.Length == 0 ? line : stripped;
        }

        private static bool IsAnswerLine(string probe)
        {
            return probe.StartsWith("ANSWER", StringComparison.OrdinalIgnoreCase)
                || probe.StartsWith("Answer", StringComparison.OrdinalIgnoreCase)
                || probe.StartsWith("Đáp án", StringComparison.OrdinalIgnoreCase)
                || probe.StartsWith("Dap an", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Nhận dòng mở đầu câu hỏi. Chấp nhận cả mẫu của trung tâm ("Question 1.", "Question 12:")
        /// lẫn mẫu cũ ("Question:", "Câu 1: ..."). Trả về phần nội dung nằm cùng dòng nếu có.
        /// </summary>
        private static bool TryReadQuestionHeading(string plainLine, out string rest)
        {
            rest = string.Empty;

            // "Questioning the premise..." không phải tiêu đề câu hỏi.
            var match = QuestionHeading.Match(plainLine);
            if (!match.Success) return false;

            rest = match.Groups[2].Value.Trim();
            return true;
        }

        /// <summary>
        /// Lấy chữ cái đáp án đúng, chịu được "ANSWER D", "ANSWER: D", "Đáp án: d.".
        /// </summary>
        private static string? ExtractAnswerLetter(string probe)
        {
            var colonIdx = probe.IndexOf(':');
            var tail = colonIdx >= 0 ? probe.Substring(colonIdx + 1) : probe;

            foreach (var token in tail.Split(new[] { ' ', '\t', '.', ',', '-', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var upper = token.Trim().ToUpperInvariant();
                if (upper.Length == 1 && upper[0] >= 'A' && upper[0] <= 'D') return upper;
            }

            return null;
        }

        /// <summary>
        /// Đọc đáp án tự luận trong dòng ANSWER / Đáp án: "ANSWER: 3/2 | 1.5 | 1,5".
        /// Dấu | (hoặc ;) phân tách các cách ghi đều được tính đúng. Bỏ dấu chấm/dấu phẩy
        /// cuối câu nếu phần còn lại đọc được thành số.
        /// </summary>
        private static List<string> ExtractFreeResponseAnswers(string probe)
        {
            var colonIdx = probe.IndexOf(':');
            var tail = colonIdx >= 0 ? probe.Substring(colonIdx + 1) : probe;

            var accepted = new List<string>();
            foreach (var rawPart in tail.Split(new[] { '|', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var part = rawPart.Trim().TrimEnd('.', '。', ',').Trim();
                if (part.Length == 0) continue;
                accepted.Add(part);
            }

            return accepted;
        }

        /// <summary>
        /// Trường hợp cả bốn đáp án bị dồn vào một dòng (Word/PDF đổ thành một đoạn).
        /// Chỉ tách khi tìm đủ A, B, C, D theo đúng thứ tự, mỗi phần đều có nội dung.
        /// </summary>
        private static bool TryExtractInlineOptions(Question q, string line)
        {
            var matches = new List<Match>();

            foreach (Match match in InlineLabel.Matches(line))
            {
                char letter = char.ToUpperInvariant(match.Value.Trim()[0]);

                if (matches.Count == 0)
                {
                    if (letter != 'A') continue;
                }
                else
                {
                    char previous = char.ToUpperInvariant(matches[^1].Value.Trim()[0]);
                    if (letter != previous + 1) continue;
                }

                matches.Add(match);
                if (matches.Count == 4) break;
            }

            if (matches.Count < 4) return false;

            // Thử tách trước, chỉ ghi vào câu hỏi khi cả bốn phần đều có nội dung,
            // tránh làm hỏng câu mà dòng đó thật ra chỉ là câu văn bình thường.
            var values = new string[4];
            for (int i = 0; i < 4; i++)
            {
                int start = matches[i].Index + matches[i].Length;
                int end = i < 3 ? matches[i + 1].Index : line.Length;
                if (end <= start) return false;

                var value = NormalizeOptionText(line.Substring(start, end - start), (char)('A' + i));
                if (value.Length == 0) return false;
                values[i] = value;
            }

            for (int i = 0; i < 4; i++)
            {
                AssignOption(q, (char)('A' + i), values[i]);
            }

            return true;
        }

        /// <summary>
        /// Đáp án/câu chỉ toàn công thức: bọc vào span để frontend vẽ bằng KaTeX. Đáp án đã là HTML
        /// (có span/div công thức) thì giữ nguyên.
        /// </summary>
        private static string WrapMathOption(string value) => WrapPlainMath(value);

        private static readonly Dictionary<char, string> VulgarFractions = new()
        {
            ['½'] = "\\frac{1}{2}", ['⅓'] = "\\frac{1}{3}", ['⅔'] = "\\frac{2}{3}",
            ['¼'] = "\\frac{1}{4}", ['¾'] = "\\frac{3}{4}", ['⅕'] = "\\frac{1}{5}",
            ['⅖'] = "\\frac{2}{5}", ['⅗'] = "\\frac{3}{5}", ['⅘'] = "\\frac{4}{5}",
            ['⅙'] = "\\frac{1}{6}", ['⅚'] = "\\frac{5}{6}", ['⅛'] = "\\frac{1}{8}"
        };

        private static readonly Dictionary<char, char> SuperscriptDigits = new()
        {
            ['⁰'] = '0', ['¹'] = '1', ['²'] = '2', ['³'] = '3', ['⁴'] = '4',
            ['⁵'] = '5', ['⁶'] = '6', ['⁷'] = '7', ['⁸'] = '8', ['⁹'] = '9'
        };

        /// <summary>
        /// Giáo viên thường gõ công thức bằng chữ: 48(2)^(6x), √(4r - 7), 38/7.
        /// Hàm này đổi sang LaTeX và bọc span.math-inline để frontend vẽ bằng KaTeX,
        /// kể cả khi đề đã được tải lên từ trước (gọi lúc đọc đề).
        /// </summary>
        public static string WrapPlainMath(string? value)
        {
            var text = (value ?? string.Empty).Trim();
            if (text.Length == 0) return text;
            if (text.Contains("math-inline") || text.Contains("math-block")) return text;

            // Nội dung nhiều dòng (hệ phương trình, công thức riêng một dòng rồi mới tới câu văn):
            // xử lý từng dòng, giữ nguyên thẻ xuống dòng.
            if (text.Contains("<br") || text.Contains('\n') || text.Contains('\r'))
            {
                var parts = LineBreakRegex.Split(text);
                for (var i = 0; i < parts.Length; i += 2)
                    parts[i] = WrapPlainMathLine(parts[i]);
                return string.Concat(parts);
            }

            return WrapPlainMathLine(text);
        }

        private static string WrapPlainMathLine(string value)
        {
            var text = NormalizeMathEntities(value.Trim());
            if (text.Length == 0) return value;
            if (text.Contains("math-inline") || text.Contains("math-block")) return value;

            // Đã là LaTeX gõ tay.
            if (Regex.IsMatch(text, @"\\[a-zA-Z]{2,}|\^\{|_\{"))
                return $"<span class='math-inline'>{WebUtility.HtmlEncode(text)}</span>";

            if (IsPlainMathText(text))
                return $"<span class='math-inline'>{WebUtility.HtmlEncode(PlainTextToLatex(text))}</span>";

            // Công thức nằm giữa câu văn: "The function f is defined by f(x) = (8x + 4) / (7p), ..."
            return WrapInlinePlainMath(text);
        }

        private static readonly Regex NumericEntityRegex = new(
            @"&#(x?[0-9A-Fa-f]+);", RegexOptions.Compiled);

        private static readonly Dictionary<string, string> MathNamedEntities = new()
        {
            ["&sup2;"] = "²", ["&sup3;"] = "³", ["&sup1;"] = "¹",
            ["&radic;"] = "√", ["&times;"] = "×", ["&divide;"] = "÷",
            ["&plusmn;"] = "±", ["&ne;"] = "≠", ["&le;"] = "≤", ["&ge;"] = "≥",
            ["&minus;"] = "−", ["&deg;"] = "°", ["&pi;"] = "π", ["&infin;"] = "∞",
            ["&frac12;"] = "½", ["&frac14;"] = "¼", ["&frac34;"] = "¾"
        };

        /// <summary>
        /// Word/NPOI hay lưu dấu mũ dưới dạng entity (x&#178;). Đổi về ký tự thật để nhận ra
        /// công thức, nhưng không đụng tới các entity có thể phá cấu trúc HTML.
        /// </summary>
        private static string NormalizeMathEntities(string text)
        {
            if (!text.Contains('&')) return text;

            text = NumericEntityRegex.Replace(text, match =>
            {
                var raw = match.Groups[1].Value;
                int code;
                var ok = raw.StartsWith("x", StringComparison.OrdinalIgnoreCase)
                    ? int.TryParse(raw.Substring(1), System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out code)
                    : int.TryParse(raw, out code);

                if (!ok || code < 0xA0 || code > 0xFFFF) return match.Value;
                if (code == '<' || code == '>' || code == '&' || code == '"' || code == '\'')
                    return match.Value;

                return char.ConvertFromUtf32(code);
            });

            foreach (var pair in MathNamedEntities) text = text.Replace(pair.Key, pair.Value);
            return text;
        }

        private static readonly Regex LineBreakRegex = new(
            @"(<br\s*/?>|\r?\n)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex InlineFormulaRegex = new(
            @"(?<tag><[^>]+>)|(?<formula>[A-Za-z]\([^()]{0,12}\)\s*=\s*[^,;<>]+?(?=\.(?:\s|$)|[,;]|$|<))",
            RegexOptions.Compiled);

        private static string WrapInlinePlainMath(string value)
        {
            return InlineFormulaRegex.Replace(value, match =>
            {
                if (!match.Groups["formula"].Success) return match.Value;

                var formula = match.Value.Trim();
                if (!IsPlainMathText(formula)) return match.Value;

                return $"<span class='math-inline'>{WebUtility.HtmlEncode(PlainTextToLatex(formula))}</span>";
            });
        }

        private static bool IsPlainMathText(string text)
        {
            if (text.Length > 200) return false;
            if (text.Contains('\\')) return false;
            // Hệ phương trình nhiều dòng để nguyên, bọc span sẽ làm mất xuống dòng.
            if (text.Contains('\n') || text.Contains('\r')) return false;

            const string allowedSymbols = "√²³⁴⁵⁶⁷⁸⁹⁰⁻⁺₀₁₂₃₄₅₆₇₈₉π∞·×÷±≠≤≥–—−°½⅓⅔¼¾⅕⅖⅗⅘⅙⅚⅛";
            if (text.Any(ch => ch > 0x7F && !allowedSymbols.Contains(ch))) return false;

            if (!Regex.IsMatch(text, @"[\^√=]") && !Regex.IsMatch(text, @"\d\s*/\s*\d")) return false;

            if (!Regex.IsMatch(text, @"^[A-Za-z0-9\s\+\-\*/\^\(\)\[\]\{\}\.,\=<>√²³⁴⁵⁶⁷⁸⁹⁰⁻⁺₀₁₂₃₄₅₆₇₈₉π∞·×÷±≠≤≥–—−°½⅓⅔¼¾⅕⅖⅗⅘⅙⅚⅛\|]+$"))
                return false;

            var withoutFunctions = Regex.Replace(text, @"\b(?:sin|cos|tan|cot|log|ln|max|min)\b", "", RegexOptions.IgnoreCase);
            return !Regex.IsMatch(withoutFunctions, @"[A-Za-z]{3,}");
        }

        private static string PlainTextToLatex(string text)
        {
            var latex = text.Trim();

            latex = Regex.Replace(latex, "[–—−]", "-");
            latex = latex.Replace("×", "\\times ").Replace("÷", "\\div ").Replace("·", "\\cdot ")
                .Replace("±", "\\pm ").Replace("∞", "\\infty ").Replace("π", "\\pi ")
                .Replace("≠", "\\neq ").Replace("≤", "\\le ").Replace("≥", "\\ge ");
            latex = latex.Replace("°", "^{\\circ}");

            foreach (var pair in VulgarFractions) latex = latex.Replace(pair.Key.ToString(), pair.Value);

            latex = Regex.Replace(latex, "[⁰¹²³⁴⁵⁶⁷⁸⁹]+", match =>
                "^{" + string.Concat(match.Value.Select(ch => SuperscriptDigits[ch])) + "}");

            latex = Regex.Replace(latex, @"√\s*\(([^()]*)\)", @"\sqrt{$1}");
            latex = Regex.Replace(latex, @"√\s*([A-Za-z0-9.]+)", @"\sqrt{$1}");

            latex = Regex.Replace(latex, @"\^\[([^\]]*)\]", "^{$1}");
            latex = Regex.Replace(latex, @"\^\(([^()]*)\)", "^{$1}");
            latex = Regex.Replace(latex, @"\^\{([^{}]*)\}", "^{$1}");
            latex = Regex.Replace(latex, @"\^([A-Za-z0-9.]+)", "^{$1}");

            latex = Regex.Replace(latex, @"_([A-Za-z0-9]+)", "_{$1}");

            latex = Regex.Replace(latex, @"\(([^()]*)\)\s*/\s*\(([^()]*)\)", @"\frac{$1}{$2}");
            latex = Regex.Replace(latex, @"([A-Za-z]\([^()]*\)[A-Za-z0-9]*)\s*/\s*\(([^()]*)\)", @"\frac{$1}{$2}");
            latex = Regex.Replace(latex, @"(?<![\w.])(\d+(?:\.\d+)?)\s*/\s*(\d+(?:\.\d+)?)(?![\w.])", @"\frac{$1}{$2}");
            latex = Regex.Replace(latex, @"([A-Za-z](?:\^\{[^{}]*\})?)\s*/\s*(\d+|[A-Za-z])", @"\frac{$1}{$2}");
            latex = Regex.Replace(latex, @"\(\\frac\{([^{}]*)\}\{([^{}]*)\}\)", @"\frac{$1}{$2}");
            latex = Regex.Replace(latex, @"(\\frac\{[^{}]*\}\{[^{}]*\})\.\s*", @"$1\cdot ");

            // '<' '>' phải đổi sang lệnh LaTeX để bộ lọc HTML không hiểu nhầm là thẻ.
            latex = latex.Replace("<", "\\lt ").Replace(">", "\\gt ");

            latex = Regex.Replace(latex, @"[%&#$]", match => "\\" + match.Value);
            latex = latex.Replace("_", "\\_");

            return latex;
        }

        private static void AssignOption(Question q, char key, string value)
        {
            var text = WrapMathOption(value);
            switch (key)
            {
                case 'A': q.OptionA = text; break;
                case 'B': q.OptionB = text; break;
                case 'C': q.OptionC = text; break;
                case 'D': q.OptionD = text; break;
            }
        }

        private static string GetOption(Question q, char key) => key switch
        {
            'A' => q.OptionA ?? string.Empty,
            'B' => q.OptionB ?? string.Empty,
            'C' => q.OptionC ?? string.Empty,
            'D' => q.OptionD ?? string.Empty,
            _ => string.Empty
        };

        /// <summary>
        /// Nối thêm nội dung vào một đáp án: bảng/hình thì dán liền, phân số xếp dọc
        /// ("1" rồi "400") thì nối bằng dấu /, chữ thường thì xuống dòng.
        /// </summary>
        private static void AppendOptionContent(Question q, char key, string raw)
        {
            var addition = (raw ?? string.Empty).Trim();
            if (addition.Length == 0) return;

            var existing = GetOption(q, key).Trim();
            if (existing.Length == 0)
            {
                AssignOption(q, key, addition);
                return;
            }

            string separator;
            if (addition.StartsWith("<", StringComparison.Ordinal))
            {
                separator = string.Empty;
            }
            else if (AnswerMatcher.TryParseNumber(existing, out _) && AnswerMatcher.TryParseNumber(addition, out _))
            {
                separator = "/";
            }
            else
            {
                separator = "<br>";
            }

            AssignOption(q, key, existing + separator + addition);
        }

        private static void AddParagraphLines(List<string> lines, XWPFParagraph paragraph, string? mathHtml = null)
        {
            var html = string.IsNullOrWhiteSpace(mathHtml) ? RenderParagraph(paragraph) : mathHtml;
            if (string.IsNullOrWhiteSpace(html))
            {
                // Đoạn trống trong Word chính là dòng cách: giữ lại để web không dồn chữ vào nhau.
                lines.Add(string.Empty);
                return;
            }

            var prefix = string.IsNullOrEmpty(paragraph.GetNumID()) ? string.Empty : "• ";

            // Shift+Enter trong Word sinh <br> ngay trong đoạn. Bộ đọc làm việc theo dòng
            // nên phải tách mỗi <br> thành một dòng riêng: nếu để chung, cả đoạn nhiều dòng
            // (đề bài + đáp án A-D + ANSWER) bị coi là một dòng và không bóc được gì.
            // Khi ghép lại, JoinPreservingLineBreaks vẫn nối bằng <br> như cũ.
            var parts = html.Split("<br>", StringSplitOptions.None);
            for (int i = 0; i < parts.Length; i++)
            {
                lines.Add(i == 0 ? prefix + parts[i] : parts[i]);
            }
        }

        /// <summary>
        /// Nối các dòng của một khối văn bản, giữ cả dòng trống (tối đa một dòng trống liên tiếp)
        /// để khoảng cách giữa các đoạn trong file Word còn nguyên khi hiển thị trên web.
        /// </summary>
        private static string JoinPreservingLineBreaks(List<string> lines)
        {
            var content = new StringBuilder();

            foreach (var rawLine in lines)
            {
                if (string.IsNullOrWhiteSpace(rawLine))
                {
                    if (content.Length > 0) content.Append("<br>");
                    continue;
                }

                if (content.Length > 0) content.Append("<br>");
                content.Append(rawLine.Trim());
            }

            var html = content.ToString().Trim();
            while (html.EndsWith("<br>", StringComparison.OrdinalIgnoreCase))
            {
                html = html[..^4].TrimEnd();
            }

            return html;
        }

        private static string RenderParagraph(XWPFParagraph paragraph)
        {
            var content = new StringBuilder();
            foreach (var run in paragraph.Runs)
            {
                AppendEmbeddedPictures(content, run);

                var text = run.ToString();
                if (!string.IsNullOrEmpty(text))
                {
                    var encoded = WebUtility.HtmlEncode(text).Replace("\r\n", "<br>").Replace("\n", "<br>");
                    if (run.IsBold) encoded = $"<strong>{encoded}</strong>";
                    if (run.IsItalic) encoded = $"<em>{encoded}</em>";
                    if (HasUnderline(run)) encoded = $"<u>{encoded}</u>";
                    content.Append(encoded);
                }

                // Shift+Enter trong Word tạo thẻ <w:br/> nằm trong run, NPOI không trả về như
                // ký tự nên trước đây các dòng bị dính liền. Thêm lại đúng số lần ngắt dòng.
                content.Append(ReadLineBreaks(run));
            }

            return content.ToString();
        }

        private static string ReadLineBreaks(XWPFRun run)
        {
            var ctr = run.GetCTR();
            if (ctr == null) return string.Empty;

            int count = ctr.SizeOfBrArray() + ctr.SizeOfCrArray();
            return count <= 0 ? string.Empty : string.Concat(Enumerable.Repeat("<br>", count));
        }

        private static void AppendEmbeddedPictures(StringBuilder content, XWPFRun run)
        {
            foreach (var picture in run.GetEmbeddedPictures())
            {
                var pictureData = picture.GetPictureData();
                var bytes = pictureData?.Data;
                if (bytes == null || bytes.Length == 0) continue;

                var fileName = pictureData.FileName;
                var mimeType = GetImageMimeType(fileName);
                var dataUri = $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}";
                content.Append($"<img src=\"{dataUri}\" alt=\"Hình minh họa\"{ImageSizeAttributes(picture.Width, picture.Height)} />");
            }
        }

        private static string GetImageMimeType(string? fileName)
        {
            return Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".svg" => "image/svg+xml",
                ".webp" => "image/webp",
                _ => "image/png"
            };
        }

        private static bool HasUnderline(XWPFRun run)
        {
            var runProperties = run.GetCTR()
                .GetType()
                .GetProperty("rPr")?
                .GetValue(run.GetCTR());

            return runProperties?
                .GetType()
                .GetProperty("u")?
                .GetValue(runProperties) != null;
        }

        private static string StripHtml(string value)
        {
            var withLineBreaks = Regex.Replace(value, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase);
            return WebUtility.HtmlDecode(Regex.Replace(withLineBreaks, "<[^>]+>", string.Empty));
        }

        private static string RemovePrefix(string html, string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return html.Trim();
            var index = html.IndexOf(':');
            return index >= 0 ? html[(index + 1)..].TrimStart() : html.Trim();
        }
    }
}
