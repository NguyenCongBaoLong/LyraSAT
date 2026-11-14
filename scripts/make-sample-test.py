"""Tạo đề Word mẫu tổng hợp mọi trường hợp định dạng của LyraSAT.

Chạy: python scripts/make-sample-test.py
Kết quả trên Desktop:
  Mau-De-SAT-Tong-Hop.docx      - đề mẫu để giao bài
  Mau-Huong-Dan-Dinh-Dang.docx  - hướng dẫn cho giáo viên, KHÔNG upload

Cần: Pillow (pip install pillow) để vẽ biểu đồ mẫu.
"""
import io
import os
import zipfile

from PIL import Image, ImageDraw

W = ('xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" '
     'xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" '
     'xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing" '
     'xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" '
     'xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture"')

CONTENT_TYPES = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
<Default Extension="xml" ContentType="application/xml"/>
<Default Extension="png" ContentType="image/png"/>
<Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
</Types>'''

RELS = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
</Relationships>'''

IMAGE_REL = ('<Relationship Id="rId{n}" Type="http://schemas.openxmlformats.org/officeDocument/2006/'
             'relationships/image" Target="media/{name}"/>')


def esc(text):
    return text.replace('&', '&amp;').replace('<', '&lt;').replace('>', '&gt;')


def p(text, bold=False, size=None):
    rpr = ''
    if bold or size:
        rpr = '<w:rPr>%s%s</w:rPr>' % (
            '<w:b/>' if bold else '',
            '<w:sz w:val="%d"/>' % size if size else '',
        )
    return '<w:p><w:r>%s<w:t xml:space="preserve">%s</w:t></w:r></w:p>' % (rpr, esc(text))


def image_paragraph(rid, name, cx, cy):
    return (
        '<w:p><w:r><w:drawing><wp:inline distT="0" distB="0" distL="0" distR="0">'
        '<wp:extent cx="{cx}" cy="{cy}"/><wp:docPr id="{rid}" name="{name}"/>'
        '<a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture">'
        '<pic:pic><pic:nvPicPr><pic:cNvPr id="{rid}" name="{name}"/><pic:cNvPicPr/></pic:nvPicPr>'
        '<pic:blipFill><a:blip r:embed="rId{rid}"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>'
        '<pic:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="{cx}" cy="{cy}"/></a:xfrm>'
        '<a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr></pic:pic>'
        '</a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>'
    ).format(rid=rid, name=name, cx=cx, cy=cy)


def cell(content):
    if not content.startswith('<w:tbl>'):
        content = p(content)
    return '<w:tc><w:tcPr><w:tcW w:w="2400" w:type="dxa"/></w:tcPr>%s</w:tc>' % content


def table(rows):
    cols = max(len(r) for r in rows)
    xml = '<w:tbl><w:tblPr><w:tblW w:w="0" w:type="auto"/></w:tblPr><w:tblGrid>'
    xml += '<w:gridCol w:w="2400"/>' * cols + '</w:tblGrid>'
    for row in rows:
        xml += '<w:tr>' + ''.join(cell(c) for c in row) + '</w:tr>'
    return xml + '</w:tbl>'


def write_docx(name, body_parts, images=()):
    """images: list of (file_name, png_bytes)."""
    doc = ('<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n'
           '<w:document %s><w:body>%s</w:body></w:document>' % (W, ''.join(body_parts)))
    out = os.path.join(os.path.expanduser('~'), 'Desktop', name)
    with zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED) as z:
        z.writestr('[Content_Types].xml', CONTENT_TYPES)
        z.writestr('_rels/.rels', RELS)
        z.writestr('word/document.xml', doc)
        rels = ['<?xml version="1.0" encoding="UTF-8" standalone="yes"?>',
                '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">']
        for index, (file_name, data) in enumerate(images, start=10):
            rels.append(IMAGE_REL.format(n=index, name=file_name))
            z.writestr('word/media/' + file_name, data)
        rels.append('</Relationships>')
        z.writestr('word/_rels/document.xml.rels', '\n'.join(rels))
    print('wrote', out)


# ---------------------------------------------------------------------------
# Vẽ biểu đồ mẫu bằng Pillow
# ---------------------------------------------------------------------------

def _canvas(width=900, height=560):
    image = Image.new('RGB', (width, height), 'white')
    return image, ImageDraw.Draw(image)


def _axes(draw, width=900, height=560, ymax=20, xlabels=('-14', '-12', '-10', '-8', '-6', '-4', '-2', '0')):
    left, top, right, bottom = 90, 40, width - 60, height - 70
    draw.rectangle([left, top, right, bottom], outline=(120, 120, 120))
    for step in range(0, 5):
        y = bottom - step * (bottom - top) / 4
        draw.line([left, y, right, y], fill=(220, 220, 220))
        draw.text((left - 40, y - 6), str(int(ymax * step / 4)), fill=(80, 80, 80))
    for i, label in enumerate(xlabels):
        x = left + i * (right - left) / (len(xlabels) - 1)
        draw.line([x, top, x, bottom], fill=(235, 235, 235))
        draw.text((x - 10, bottom + 8), label, fill=(80, 80, 80))
    draw.line([right, top, right, bottom], fill=(80, 80, 80))
    draw.line([left, bottom, right, bottom], fill=(80, 80, 80))
    return left, top, right, bottom


def scatter_chart():
    image, draw = _canvas()
    left, top, right, bottom = _axes(draw)

    points = [(-14, 17), (-12, 16), (-10, 15), (-8, 12), (-6, 11)]
    coords = []
    for xv, yv in points:
        x = left + (xv + 14) / 14 * (right - left)
        y = bottom - yv / 20 * (bottom - top)
        coords.append((x, y))
        draw.ellipse([x - 5, y - 5, x + 5, y + 5], fill=(40, 40, 40))

    # đường hồi quy gần đúng
    draw.line([coords[0], coords[-1]], fill=(60, 60, 60), width=2)
    draw.text((right + 8, top), 'y', fill=(40, 40, 40))
    draw.text((right + 8, bottom + 8), 'x', fill=(40, 40, 40))
    buffer = io.BytesIO()
    image.save(buffer, format='PNG')
    return buffer.getvalue()


def line_chart():
    image, draw = _canvas(width=900, height=520)
    left, top, right, bottom = _axes(draw, 900, 520, ymax=0.4, xlabels=('March', 'April', 'May', 'June'))

    series = [
        ('Main-stem site', [0.31, 0.30, 0.31, 0.30], (30, 30, 30)),
        ('Cedar Creek', [0.29, 0.23, 0.18, 0.13], (90, 90, 90)),
        ('Pine Creek', [0.22, 0.24, 0.27, 0.29], (140, 140, 140)),
    ]

    for name, values, color in series:
        coords = []
        for i, value in enumerate(values):
            x = left + i * (right - left) / 3
            y = bottom - value / 0.4 * (bottom - top)
            coords.append((x, y))
        draw.line(coords, fill=color, width=3)
        for x, y in coords:
            draw.ellipse([x - 5, y - 5, x + 5, y + 5], fill=color)
        draw.text((left, bottom + 30 + series.index((name, values, color)) * 18), name, fill=color)

    draw.text((left, 10), 'EE2-to-E2 ratio of water samples, March to June', fill=(30, 30, 30))
    buffer = io.BytesIO()
    image.save(buffer, format='PNG')
    return buffer.getvalue()


def small_chart(values, title):
    image, draw = _canvas(width=420, height=260)
    left, top, right, bottom = 50, 30, 400, 210
    draw.rectangle([left, top, right, bottom], outline=(120, 120, 120))
    peak = max(values) or 1
    for i, value in enumerate(values):
        x = left + 20 + i * 80
        y = bottom - value / peak * (bottom - top - 20)
        draw.rectangle([x, y, x + 40, bottom], fill=(200, 200, 200), outline=(80, 80, 80))
    draw.text((left, 6), title, fill=(30, 30, 30))
    buffer = io.BytesIO()
    image.save(buffer, format='PNG')
    return buffer.getvalue()


# ---------------------------------------------------------------------------
# ĐỀ MẪU (upload được)
# ---------------------------------------------------------------------------

scatter_png = scatter_chart()
line_png = line_chart()
option_pngs = [small_chart(v, t) for v, t in [
    ([3, 9, 27], 'A'), ([23, 29, 47], 'B'), ([20, 40, 80], 'C'), ([1, 2, 3], 'D')
]]

test = []

# 1. Văn: đoạn văn + câu hỏi tách riêng
test += [
    p('Text: The following text is adapted from a 1904 play. Jack and Bartley meet at a country fair.'),
    p('JACK: It is not to the fair I came myself, but up to the Five Acre Meadow I am going.'),
    p('BARTLEY: The rain will be down on it by evening, and on myself too.'),
    p('Question: Which choice best describes what the text suggests about Bartley?'),
    p('A. He is confident about the weather.'),
    p('B. He expects bad luck to follow him.'),
    p('C. He is angry at Jack.'),
    p('D. He is careless with his work.'),
    p('ANSWER: B'),
    p(''),
]

# 2. Toán trắc nghiệm, đề bài nhiều dòng, có dòng trống
test += [
    p('Question 2.'),
    p('The function f is defined by'),
    p('f(x) = 3x + 2'),
    p(''),
    p('What is the value of f(4)?'),
    p('A. 10'),
    p('B. 12'),
    p('C. 14'),
    p('D. 16'),
    p('ANSWER: C'),
    p(''),
]

# 3. Toán trắc nghiệm có bảng số liệu trong đề bài
test += [
    p('Question 3.'),
    p('The table shows three values of x and their corresponding values of j(x) for the exponential function j.'),
    table([['x', 'j(x)'], ['1', '3'], ['2', '9'], ['3', '27']]),
    p(''),
    p('When the graph of y = j(x) in the xy-plane is shifted up 20 units, the resulting graph is defined by function h.'),
    p('Which table gives three values of x and their corresponding values of h(x)?'),
    p('A.'),
    table([['x', 'h(x)'], ['1', '23'], ['2', '29'], ['3', '47']]),
    p('B.'),
    table([['x', 'h(x)'], ['1', '3'], ['2', '9'], ['3', '27']]),
    p('C.'),
    table([['x', 'h(x)'], ['1', '20'], ['2', '40'], ['3', '80']]),
    p('D.'),
    table([['x', 'h(x)'], ['1', '1'], ['2', '2'], ['3', '3']]),
    p('ANSWER: A'),
    p(''),
]

# 4. Toán điền đáp án, nhiều cách ghi đúng
test += [
    p('Question 4.'),
    p('If 2x + 1 = 4, what is the value of x?'),
    p('Answer: 3/2 | 1.5 | 1,5'),
    p(''),
]

# 5. Toán điền đáp án, đáp án âm
test += [
    p('Question 5.'),
    p('What is the value of -3/4 + 1/2?'),
    p('Answer: -1/4 | -0.25 | -0,25'),
    p(''),
]

# 6. Toán trắc nghiệm, bốn đáp án là bảng, xếp dọc
test += [
    p('Question 6.'),
    p('The table shows values of j(x). Which table represents h(x) = j(x) - 3?'),
    p('A.'),
    table([['x', 'h(x)'], ['1', '0'], ['2', '6'], ['3', '24']]),
    p('B.'),
    table([['x', 'h(x)'], ['1', '6'], ['2', '12'], ['3', '30']]),
    p('C.'),
    table([['x', 'h(x)'], ['1', '3'], ['2', '9'], ['3', '27']]),
    p('D.'),
    table([['x', 'h(x)'], ['1', '23'], ['2', '29'], ['3', '47']]),
    p('ANSWER: A'),
    p(''),
]

# 7. Toán trắc nghiệm, đáp án xếp 2x2, mỗi ô là một bảng con
test += [
    p('Question 7.'),
    p('Which table represents the function h(x) = j(x) + 20?'),
    table([
        [table([['x', 'h(x)'], ['1', '23'], ['2', '29'], ['3', '47']]),
         table([['x', 'h(x)'], ['1', '3'], ['2', '9'], ['3', '27']])],
        [table([['x', 'h(x)'], ['1', '20'], ['2', '40'], ['3', '80']]),
         table([['x', 'h(x)'], ['1', '1'], ['2', '2'], ['3', '3']])],
    ]),
    p('ANSWER: A'),
    p(''),
]

# 8. Toán trắc nghiệm, đáp án xếp 2x2 dạng chữ có nhãn
test += [
    p('Question 8.'),
    p('A company studied a random sample of 60 lawns in a small town. The company found that 10 of these lawns contain a brown patch.'),
    p('If there are 720 lawns in the town, which of the following is the best estimate of the number of lawns that contain a brown patch?'),
    table([['A. 6', 'B. 8'], ['C. 12', 'D. 120']]),
    p('ANSWER: D'),
    p(''),
]

# 9. Toán trắc nghiệm, đáp án là phân số xếp dọc
test += [
    p('Question 9.'),
    p('A quality control study found that for every 400 lightbulbs produced, 11 are defective.'),
    p('If one lightbulb is selected at random, what is the probability that the lightbulb is defective?'),
    p('A.'), p('1'), p('400'),
    p('B.'), p('11'), p('400'),
    p('C.'), p('11'), p('389'),
    p('D.'), p('1'), p('11'),
    p('ANSWER: B'),
    p(''),
]

# 10. Văn có biểu đồ trong đoạn văn (chèn ảnh)
test += [
    p('Text: Estrogen hormones reach rivers mainly through treated wastewater and runoff from cattle feedlots.'),
    p('A team measured the ratio of synthetic estrogen to natural estrogen in water samples collected each month from March to June at three sites.'),
    image_paragraph(10, 'line-chart.png', 3840480, 2217600),
    p('Question: Which choice best describes data from the graph that would most directly weaken the team conclusion?'),
    p('A. At the main-stem site, the ratio varied by no more than 0.01 over the four months.'),
    p('B. At Pine Creek, the ratio was lower than at the main-stem site every month.'),
    p('C. At Cedar Creek, the ratio decreased from 0.29 in March to 0.13 in June.'),
    p('D. The ratio at Cedar Creek was higher than the ratio at Pine Creek in March.'),
    p('ANSWER: C'),
    p(''),
]

# 11. Toán có biểu đồ trong đề bài (chèn ảnh)
test += [
    p('Question 11.'),
    p('The scatterplot shows the relationship between two variables, x and y.'),
    p('A line of best fit is also shown.'),
    image_paragraph(11, 'scatter.png', 3840480, 2387664),
    p(''),
    p('Which of the following equations best represents the line of best fit shown?'),
    p('A. y = -0.4x + 11'),
    p('B. y = 0.4x + 11'),
    p('C. y = -2.5x + 11'),
    p('D. y = 2.5x + 11'),
    p('ANSWER: A'),
    p(''),
]

# 12. Toán trắc nghiệm, bốn đáp án là ảnh
test += [
    p('Question 12.'),
    p('Which bar chart shows the values 3, 9, 27 increasing by the same factor?'),
    p('A.'),
    image_paragraph(12, 'opt-a.png', 2286000, 1414286),
    p('B.'),
    image_paragraph(13, 'opt-b.png', 2286000, 1414286),
    p('C.'),
    image_paragraph(14, 'opt-c.png', 2286000, 1414286),
    p('D.'),
    image_paragraph(15, 'opt-d.png', 2286000, 1414286),
    p('ANSWER: A'),
]

write_docx(
    'Mau-De-SAT-Tong-Hop.docx',
    test,
    images=[
        ('line-chart.png', line_png),
        ('scatter.png', scatter_png),
        ('opt-a.png', option_pngs[0]),
        ('opt-b.png', option_pngs[1]),
        ('opt-c.png', option_pngs[2]),
        ('opt-d.png', option_pngs[3]),
    ],
)


# ---------------------------------------------------------------------------
# HƯỚNG DẪN (không upload)
# ---------------------------------------------------------------------------

guide = []
guide += [
    p('HƯỚNG DẪN SOẠN ĐỀ WORD CHO LYRA SAT', bold=True, size=32),
    p(''),
    p('1. Quy tắc chung', bold=True, size=26),
    p('- Mỗi câu bắt đầu bằng chữ Question (Question: hoặc Question 1. đều được, số thứ tự không bắt buộc).'),
    p('- Mỗi dòng gõ bằng một đoạn riêng (Enter). Dòng trống giữa các đoạn được giữ nguyên khi lên web.'),
    p('- Bảng số liệu: chèn bảng Word bình thường.'),
    p('- Biểu đồ, đồ thị, hình vẽ: chèn ảnh (Copy ảnh rồi Paste vào Word) ngay dưới đoạn muốn hiển thị.'),
    p(''),
    p('2. Câu Văn (Verbal)', bold=True, size=26),
    p('Text: đoạn văn (có thể chèn ảnh biểu đồ ngay dưới đoạn văn)'),
    p('Question: câu hỏi'),
    p('A. ...'),
    p('B. ...'),
    p('C. ...'),
    p('D. ...'),
    p('ANSWER: B'),
    p(''),
    p('3. Câu Toán trắc nghiệm', bold=True, size=26),
    p('Question 2.'),
    p('Đề bài gõ thẳng, có thể nhiều dòng, có công thức, bảng, ảnh biểu đồ.'),
    p('Không cần dòng Question: riêng vì đề bài chính là câu hỏi.'),
    p('A. ...'),
    p('B. ...'),
    p('C. ...'),
    p('D. ...'),
    p('ANSWER: C'),
    p(''),
    p('4. Câu Toán điền đáp án', bold=True, size=26),
    p('Question 4.'),
    p('If 2x + 1 = 4, what is the value of x?'),
    p('Answer: 3/2 | 1.5 | 1,5'),
    p('Dấu gạch đứng ngăn các cách ghi đều được tính đúng.'),
    p(''),
    p('5. Biểu đồ, đồ thị', bold=True, size=26),
    p('Chèn ảnh vào Word như bình thường (Copy hình rồi Paste). Ảnh nằm giữa đoạn văn và câu hỏi thì hiện đúng chỗ đó.'),
    p('Ảnh cũng dùng được làm đáp án: gõ A. rồi Paste ảnh ngay dưới.'),
    p(''),
    p('6. Đáp án là bảng', bold=True, size=26),
    p('Cách A: mỗi đáp án một bảng, đặt ngay dưới nhãn A. / B. / C. / D.'),
    p('Cách B: bảng 2x2, mỗi ô là một bảng con.'),
    p('Cách C: bảng 2x2, trong mỗi ô ghi sẵn A. / B. / C. / D.'),
    p('Lưu ý: bảng 2x2 chỉ có số trơn, không nhãn và không phải bảng con sẽ bị hiểu là bảng số liệu của đề.'),
    p(''),
    p('7. Đáp án là phân số', bold=True, size=26),
    p('Gõ gọn: A. 1/400'),
    p('Hoặc để xếp dọc: A. rồi 1 rồi 400. Hệ thống tự ghép thành 1/400.'),
    p(''),
    p('8. Đáp án đúng', bold=True, size=26),
    p('Trắc nghiệm: ANSWER: A (hoặc B, C, D).'),
    p('Điền đáp án: Answer: 8 hoặc ANSWER: 8.'),
    p(''),
    p('9. Kiểm tra trước khi giao', bold=True, size=26),
    p('- Câu trắc nghiệm phải đủ A, B, C, D.'),
    p('- Không để trống đáp án đúng.'),
    p('- Nếu hệ thống báo lỗi, đọc danh sách câu lỗi rồi sửa đúng câu đó.'),
]

write_docx('Mau-Huong-Dan-Dinh-Dang.docx', guide)
