"""Tạo đề Word mẫu tổng hợp mọi trường hợp định dạng của LyraSAT.

Chạy: python scripts/make-sample-test.py
Kết quả: Mau-De-SAT-Tong-Hop.docx (để giao bài) và
         Mau-Huong-Dan-Dinh-Dang.docx (để gửi giáo viên đọc, KHÔNG upload).
"""
import os
import zipfile

W = 'xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"'

CONTENT_TYPES = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
<Default Extension="xml" ContentType="application/xml"/>
<Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
</Types>'''

RELS = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
</Relationships>'''


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


def p_runs(parts):
    runs = []
    for kind, value in parts:
        if kind == 'br':
            runs.append('<w:r><w:br/></w:r>')
        else:
            runs.append('<w:r><w:t xml:space="preserve">%s</w:t></w:r>' % esc(value))
    return '<w:p>%s</w:p>' % ''.join(runs)


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


def write_docx(name, body_parts):
    doc = ('<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n'
           '<w:document %s><w:body>%s</w:body></w:document>' % (W, ''.join(body_parts)))
    out = os.path.join(os.path.expanduser('~'), 'Desktop', name)
    with zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED) as z:
        z.writestr('[Content_Types].xml', CONTENT_TYPES)
        z.writestr('_rels/.rels', RELS)
        z.writestr('word/document.xml', doc)
        z.writestr('word/_rels/document.xml.rels', RELS)
    print('wrote', out)


def option_table(values):
    rows = [['x', 'h(x)']] + [[str(i + 1), str(v)] for i, v in enumerate(values)]
    return table(rows)


# ---------------------------------------------------------------------------
# ĐỀ MẪU (upload được)
# ---------------------------------------------------------------------------

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
    option_table([23, 29, 47]),
    p('B.'),
    option_table([3, 9, 27]),
    p('C.'),
    option_table([20, 40, 80]),
    p('D.'),
    option_table([1, 2, 3]),
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
    option_table([0, 6, 24]),
    p('B.'),
    option_table([6, 12, 30]),
    p('C.'),
    option_table([3, 9, 27]),
    p('D.'),
    option_table([23, 29, 47]),
    p('ANSWER: A'),
    p(''),
]

# 7. Toán trắc nghiệm, đáp án xếp 2x2, mỗi ô là một bảng con
test += [
    p('Question 7.'),
    p('Which table represents the function h(x) = j(x) + 20?'),
    table([
        [option_table([23, 29, 47]), option_table([3, 9, 27])],
        [option_table([20, 40, 80]), option_table([1, 2, 3])],
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
]

write_docx('Mau-De-SAT-Tong-Hop.docx', test)


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
    p('- Không cần chèn ảnh: bảng và công thức để dạng chữ/bảng Word bình thường.'),
    p(''),
    p('2. Câu Văn (Verbal)', bold=True, size=26),
    p('Text: đoạn văn'),
    p('Question: câu hỏi'),
    p('A. ...'),
    p('B. ...'),
    p('C. ...'),
    p('D. ...'),
    p('ANSWER: B'),
    p(''),
    p('3. Câu Toán trắc nghiệm', bold=True, size=26),
    p('Question 2.'),
    p('Đề bài gõ thẳng, có thể nhiều dòng, có công thức và bảng.'),
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
    p('Dấu gạch đứng ngăn các cách ghi đều được tính đúng. Có thể ghi 3/2, 1.5, 1,5, -0.25 ...'),
    p(''),
    p('5. Bảng số liệu trong đề bài', bold=True, size=26),
    p('Chèn bảng Word bình thường vào giữa đề bài. Hệ thống hiển thị thành bảng trên web.'),
    p(''),
    p('6. Đáp án là bảng', bold=True, size=26),
    p('Cách A (khuyên dùng): mỗi đáp án một bảng, đặt ngay dưới nhãn.'),
    p('A.'),
    p('[bảng]'),
    p('B.'),
    p('[bảng]'),
    p('Cách B: bảng 2x2, mỗi ô là một bảng con (như Bluebook).'),
    p('Cách C: bảng 2x2, trong mỗi ô ghi sẵn A. / B. / C. / D.'),
    p('Lưu ý: bảng 2x2 chỉ có số trơn, không nhãn và không phải bảng con sẽ bị hiểu là bảng số liệu của đề.'),
    p(''),
    p('7. Đáp án là phân số', bold=True, size=26),
    p('Gõ gọn trên một dòng: A. 1/400'),
    p('Hoặc để phân số xếp dọc:'),
    p('A.'),
    p('1'),
    p('400'),
    p('Hệ thống tự ghép thành 1/400.'),
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
